using System.Text.Json;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/author/12-me/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class MeApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/me";
    private const string Success = "Inquiry Me Success";

    private static (Guid UserId, bool IsPlatformAdmin, List<string> Roles) Data(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["userId", "isPlatformAdmin", "roles"], data.EnumerateObject().Select(p => p.Name));
        return (data.GetProperty("userId").GetGuid(), data.GetProperty("isPlatformAdmin").GetBoolean(),
            data.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToList());
    }

    [Fact]
    public async Task TC_ME_001_the_callers_token_roles()
    {
        var (userId, jwt) = await CallerAsync(new[] { "SHOP_SUPPORT", "CATALOG_MANAGER" });
        var data = Data(await Status(await Get(Path, jwt), 200, "S10000", Success));
        Assert.Equal((userId, false), (data.UserId, data.IsPlatformAdmin));
        Assert.Equal(["SHOP_SUPPORT", "CATALOG_MANAGER"], data.Roles);
    }

    [Fact]
    public async Task TC_ME_002_platform_administrator_and_empty_roles()
    {
        var (_, admin) = await CallerAsync(new[] { "CUSTOMER", "PLATFORM_ADMIN" });
        var data = Data(await Status(await Get(Path, admin), 200, "S10000", Success));
        Assert.True(data.IsPlatformAdmin);
        Assert.Equal(["CUSTOMER", "PLATFORM_ADMIN"], data.Roles);

        // Exact, case-sensitive: platform_admin is not the administrator; [] is valid.
        Assert.False(Data(await Status(await Get(Path, (await CallerAsync(new[] { "platform_admin" })).Jwt), 200, "S10000", Success)).IsPlatformAdmin);
        var empty = Data(await Status(await Get(Path, (await CallerAsync(Array.Empty<string>())).Jwt), 200, "S10000", Success));
        Assert.Equal((false, 0), (empty.IsPlatformAdmin, empty.Roles.Count));
    }

    [Fact]
    public async Task TC_ME_003_the_token_roles_not_the_assignments()
    {
        // A role assigned in the database after the token was issued is not in the answer until a refresh.
        var (userId, jwt) = await CallerAsync(new[] { "CUSTOMER" });
        await UserRoleAsync(userId, await PlatformAdminRoleAsync());
        var data = Data(await Status(await Get(Path, jwt), 200, "S10000", Success));
        Assert.Equal((false, 1), (data.IsPlatformAdmin, data.Roles.Count));
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE author.t_user_role SET row_status = 'DELETED' WHERE user_id = @u", ("u", userId));
    }

    [Fact]
    public async Task TC_ME_004_no_target_user_query_or_body()
    {
        var (_, jwt) = await CallerAsync(new[] { "CUSTOMER" });
        await Status(await Get($"{Path}?userId={Guid.NewGuid()}", jwt), 400, "E10001", "User Id is not a known field.");
        await Status(await Get(Path, jwt, "{}"), 400, "E10001", "Request Body must be empty.");
        await Status(await Get($"{Path}?x=1", null), 400, "E10001", "X is not a known field.");
    }

    [Fact]
    public async Task TC_ME_005_credential_failures()
    {
        await Status(await Get(Path, null), 401, "E10002", Invalid);
        await Status(await Get(Path, "not-a-jwt"), 401, "E10002", Invalid);
        await Status(await Get(Path, (await CallerAsync(null)).Jwt), 401, "E10002", Invalid);
        await Status(await Get(Path, (await CallerAsync("CUSTOMER")).Jwt), 401, "E10002", Invalid);

        var (revoked, revokedJwt) = await CallerAsync(new[] { "CUSTOMER" });
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.t_user_access_token SET row_status = 'INACTIVE' WHERE user_id = @u", ("u", revoked));
        await Status(await Get(Path, revokedJwt), 401, "E10002", Invalid);
        var (locked, lockedJwt) = await CallerAsync(new[] { "CUSTOMER" });
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'LOCKED' WHERE id = @u", ("u", locked));
        await Status(await Get(Path, lockedJwt), 401, "E10002", Invalid);
        var (expired, expiredJwt) = await CallerAsync(new[] { "CUSTOMER" });
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.t_user_access_token SET expired_at = NOW() - INTERVAL '1 minute' WHERE user_id = @u", ("u", expired));
        await Status(await Get(Path, expiredJwt), 401, "E10002", Invalid);
    }

    [Fact]
    public async Task TC_ME_006_a_held_table_lock_times_out_and_no_row_lock_blocks()
    {
        var (userId, jwt) = await CallerAsync(new[] { "CUSTOMER" });
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        var (connection, transaction) = await HoldAsync("SELECT 1 FROM authen.m_user WHERE id = @id FOR UPDATE", userId);
        await Status(await Get(Path, jwt, client: client), 200, "S10000", Success);
        await transaction.RollbackAsync();
        await connection.DisposeAsync();

        await using var table = new NpgsqlConnection(Postgres.ConnectionString);
        await table.OpenAsync();
        await using var tx = await table.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("LOCK TABLE authen.t_user_access_token IN ACCESS EXCLUSIVE MODE", table, tx))
            await command.ExecuteNonQueryAsync();
        await Status(await Get(Path, jwt, client: client), 408, "E90001", "The server timed out waiting for the request.");
        await tx.RollbackAsync();
    }
}
