using System.Text.Json;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/author/17-check-permission/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class CheckPermissionApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/check";
    private const string Success = "Check Permission Success";
    private const string PermissionMessage = "Permission is required and must be a permission claim such as catalog:manage.";
    private const string ScopeMessage = "Scope must be a scope claim such as shop:*.";

    private Task<(Guid UserId, string Jwt)> HolderAsync(string[] permissions, string[] scopes, string[]? roles = null) =>
        CallerAsync(roles ?? ["CUSTOMER"], permissions, scopes);

    private static (string Permission, string? Scope, bool Allowed) Data(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["permission", "scope", "allowed"], data.EnumerateObject().Select(p => p.Name));
        var scope = data.GetProperty("scope");
        return (data.GetProperty("permission").GetString()!, scope.ValueKind == JsonValueKind.Null ? null : scope.GetString(), data.GetProperty("allowed").GetBoolean());
    }

    private async Task<(string, string?, bool)> CheckAsync(string jwt, string query) =>
        Data(await Status(await Get(Path + query, jwt), 200, "S10000", Success));

    [Fact]
    public async Task TC_CHECK_001_allowed_with_and_without_a_scope()
    {
        var (_, jwt) = await HolderAsync(["catalog:manage", "shop.product:manage"], ["all:*", "shop:*"]);
        Assert.Equal(("catalog:manage", "all:*", true), await CheckAsync(jwt, "?permission=catalog:manage&scope=all:*"));
        Assert.Equal(("shop.product:manage", (string?)null, true), await CheckAsync(jwt, "?permission=shop.product:manage"));
    }

    [Fact]
    public async Task TC_CHECK_002_not_allowed_is_still_200()
    {
        var (_, jwt) = await HolderAsync(["catalog:manage", "CATALOG:READ"], ["all:*"]);
        Assert.Equal(("payment:refund", "shop:*", false), await CheckAsync(jwt, "?permission=payment:refund&scope=shop:*"));
        Assert.Equal(("payment:refund", (string?)null, false), await CheckAsync(jwt, "?permission=payment:refund"));
        Assert.Equal(("catalog:manage", "shop:*", false), await CheckAsync(jwt, "?permission=catalog:manage&scope=shop:*"));
        Assert.Equal(("payment:refund", "all:*", false), await CheckAsync(jwt, "?permission=payment:refund&scope=all:*"));
        // Exact and case-sensitive: the token's upper-case claim is not this permission.
        Assert.Equal(("catalog:read", (string?)null, false), await CheckAsync(jwt, "?permission=catalog:read"));
    }

    [Fact]
    public async Task TC_CHECK_003_platform_administrator_has_no_implicit_permission_and_claims_are_flat()
    {
        var (_, admin) = await HolderAsync([], [], ["PLATFORM_ADMIN"]);
        Assert.Equal(("catalog:manage", (string?)null, false), await CheckAsync(admin, "?permission=catalog:manage"));

        // The claims are flat: a scope granted with another permission still answers true (Q2).
        var (_, flat) = await HolderAsync(["catalog:manage", "shop.product:manage"], ["shop:*"]);
        Assert.Equal(("catalog:manage", "shop:*", true), await CheckAsync(flat, "?permission=catalog:manage&scope=shop:*"));
    }

    [Fact]
    public async Task TC_CHECK_004_the_token_not_the_grant_tables()
    {
        var (userId, jwt) = await HolderAsync([], []);
        var permission = await PermissionAsync(NewPermissionName());
        await UserPermissionAsync(userId, permission);
        var claim = await ScalarAsync<string>("SELECT regexp_replace(name, '\\.([^.]*)$', ':\\1') FROM author.m_permission WHERE id = @id", permission);
        Assert.Equal((claim, (string?)null, false), await CheckAsync(jwt, $"?permission={claim}"));
    }

    public static TheoryData<string, string> InvalidQueries => new()
    {
        { "", PermissionMessage },
        { "?permission=", PermissionMessage },
        { "?permission=catalog.manage", PermissionMessage },
        { "?permission=Catalog:manage", PermissionMessage },
        { "?permission=catalog:manage:all", PermissionMessage },
        { "?permission=catalog:manage%0A", PermissionMessage },
        { "?permission=" + new string('a', 58) + ":manage", PermissionMessage },
        { "?permission=catalog:manage&scope=", ScopeMessage },
        { "?permission=catalog:manage&scope=shop", ScopeMessage },
        { "?permission=catalog:manage&scope=SHOP:*", ScopeMessage },
        { "?permission=catalog:manage&scope=" + new string('s', 63) + ":*", ScopeMessage },
        { "?scope=shop", "Permission is required and must be a permission claim such as catalog:manage, Scope must be a scope claim such as shop:*." },
        { "?permission=a:b&permission=a:c", "Permission must not be duplicated." },
        { "?permission=a:b&userId=x", "User Id is not a known field." }
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task TC_CHECK_005_query_validation(string query, string message)
    {
        var (_, jwt) = await HolderAsync(["a:b"], []);
        await Status(await Get(Path + query, jwt), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_CHECK_006_boundaries_body_and_validation_before_the_credential()
    {
        var (_, jwt) = await HolderAsync([new string('a', 57) + ":manage"], [new string('s', 62) + ":*"]);
        Assert.True((await CheckAsync(jwt, $"?permission={new string('a', 57)}:manage&scope={new string('s', 62)}:*")).Item3);
        await Status(await Get($"{Path}?permission=a:b", jwt, "{}"), 400, "E10001", "Request Body must be empty.");
        await Status(await Get($"{Path}?permission=bad", null), 400, "E10001", PermissionMessage);
    }

    [Fact]
    public async Task TC_CHECK_007_credential_and_claim_failures()
    {
        const string query = "?permission=a:b";
        await Status(await Get(Path + query, null), 401, "E10002", Invalid);
        await Status(await Get(Path + query, "not-a-jwt"), 401, "E10002", Invalid);
        await Status(await Get(Path + query, (await CallerAsync(null, Array.Empty<string>(), Array.Empty<string>())).Jwt), 401, "E10002", Invalid);
        await Status(await Get(Path + query, (await CallerAsync(Array.Empty<string>(), null, Array.Empty<string>())).Jwt), 401, "E10002", Invalid);
        await Status(await Get(Path + query, (await CallerAsync(Array.Empty<string>(), Array.Empty<string>(), null)).Jwt), 401, "E10002", Invalid);
        await Status(await Get(Path + query, (await CallerAsync(Array.Empty<string>(), new[] { "a:b", "a:b" }, Array.Empty<string>())).Jwt), 401, "E10002", Invalid);
        await Status(await Get(Path + query, (await CallerAsync(Array.Empty<string>(), "a:b", Array.Empty<string>())).Jwt), 401, "E10002", Invalid);
        await Status(await Get(Path + query, (await CallerAsync(Array.Empty<string>(), Array.Empty<string>(), new[] { "" })).Jwt), 401, "E10002", Invalid);

        foreach (var sql in new[]
        {
            "UPDATE authen.t_user_access_token SET row_status = 'INACTIVE' WHERE user_id = @u",
            "UPDATE authen.m_user SET row_status = 'LOCKED' WHERE id = @u",
            "UPDATE authen.t_user_access_token SET expired_at = NOW() - INTERVAL '1 minute' WHERE user_id = @u"
        })
        {
            var (userId, jwt) = await HolderAsync(["a:b"], []);
            await ExecuteAsync(Postgres.ConnectionString, sql, ("u", userId));
            await Status(await Get(Path + query, jwt), 401, "E10002", Invalid);
        }
    }

    [Fact]
    public async Task TC_CHECK_008_no_grant_table_or_row_lock_blocks_and_a_held_credential_table_times_out()
    {
        var (userId, jwt) = await HolderAsync(["a:b"], []);
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        var (connection, transaction) = await HoldAsync("SELECT 1 FROM authen.m_user WHERE id = @id FOR UPDATE", userId);
        await Status(await Get($"{Path}?permission=a:b", jwt, client: client), 200, "S10000", Success);
        await transaction.RollbackAsync();
        await connection.DisposeAsync();

        foreach (var table in new[] { "author.t_user_permission", "author.t_role_permission", "author.t_user_role", "authen.t_user_access_token" })
        {
            await using var held = new NpgsqlConnection(Postgres.ConnectionString);
            await held.OpenAsync();
            await using var tx = await held.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand($"LOCK TABLE {table} IN ACCESS EXCLUSIVE MODE", held, tx))
                await command.ExecuteNonQueryAsync();
            if (table == "authen.t_user_access_token")
                await Status(await Get($"{Path}?permission=a:b", jwt, client: client), 408, "E90001", "The server timed out waiting for the request.");
            else
                Assert.True(Data(await Status(await Get($"{Path}?permission=a:b", jwt, client: client), 200, "S10000", Success)).Allowed);
            await tx.RollbackAsync();
        }
    }
}
