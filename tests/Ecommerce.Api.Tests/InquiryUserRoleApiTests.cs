using System.Text.Json;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Author;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/author/11-inquiry-user-role/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class InquiryUserRoleApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/user/role";
    private const string Success = "Inquiry User Role Success";

    private sealed record Item(Guid UserRoleId, Guid UserId, string? UserDisplayName, Guid RoleId, string RoleName, string RowStatus);

    private static (List<Item> Items, long TotalItems, long TotalPages) Data(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["items", "pageIndex", "pageSize", "totalItems", "totalPages"], data.EnumerateObject().Select(p => p.Name));
        var items = data.GetProperty("items").EnumerateArray().Select(i =>
        {
            Assert.Equal(["userRoleId", "userId", "userDisplayName", "roleId", "roleName", "rowStatus"], i.EnumerateObject().Select(p => p.Name));
            var name = i.GetProperty("userDisplayName");
            return new Item(i.GetProperty("userRoleId").GetGuid(), i.GetProperty("userId").GetGuid(), name.ValueKind == JsonValueKind.Null ? null : name.GetString(),
                i.GetProperty("roleId").GetGuid(), i.GetProperty("roleName").GetString()!, i.GetProperty("rowStatus").GetString()!);
        }).ToList();
        return (items, data.GetProperty("totalItems").GetInt64(), data.GetProperty("totalPages").GetInt64());
    }

    private async Task SetDisplayNameAsync(Guid userId, string? name) =>
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.m_user SET display_name = @n WHERE id = @u", ("n", (object?)name ?? DBNull.Value), ("u", userId));

    [Fact]
    public async Task TC_UROLEQ_001_by_role_sorted_by_display_name_deleted_excluded()
    {
        var (_, jwt) = await AdminAsync();
        var role = await RoleAsync(NewName());
        var (bob, amy, none, gone) = (await UserAsync(), await UserAsync(), await UserAsync(), await UserAsync());
        await SetDisplayNameAsync(bob, "Bob");
        await SetDisplayNameAsync(amy, "Amy");
        await SetDisplayNameAsync(none, null);
        var (b, a, n) = (await UserRoleAsync(bob, role), await UserRoleAsync(amy, role, "LOCKED"), await UserRoleAsync(none, role));
        await UserRoleAsync(gone, role, "DELETED");
        var roleName = await ScalarAsync<string>("SELECT name FROM author.m_role WHERE id = @id", role);

        var data = Data(await Status(await Get($"{Path}?roleId={role}", jwt), 200, "S10000", Success));
        // Display name order, a null display name last (PostgreSQL ascending order).
        Assert.Equal([new Item(a, amy, "Amy", role, roleName, "LOCKED"), new Item(b, bob, "Bob", role, roleName, "ACTIVE"), new Item(n, none, null, role, roleName, "ACTIVE")], data.Items);
        Assert.Equal((3L, 1L), (data.TotalItems, data.TotalPages));
    }

    private const string ReadForbidden = "You do not have permission to read user roles.";

    [Fact]
    public async Task TC_UROLEQ_002_filters_combine_and_pagination()
    {
        var (_, jwt) = await AdminAsync();
        var (user, other) = (await UserAsync(), await UserAsync());
        var (r1, r2, r3) = (await RoleAsync("A_" + NewName()), await RoleAsync("B_" + NewName()), await RoleAsync("C_" + NewName()));
        var (a1, a2, a3) = (await UserRoleAsync(user, r1), await UserRoleAsync(user, r2, "INACTIVE"), await UserRoleAsync(user, r3));
        await UserRoleAsync(other, r1);

        Assert.Equal([a1, a2, a3], Data(await Status(await Get($"{Path}?userId={user}", jwt), 200, "S10000", Success)).Items.Select(i => i.UserRoleId));
        Assert.Equal([a2], Data(await Status(await Get($"{Path}?userId={user}&rowStatus=INACTIVE", jwt), 200, "S10000", Success)).Items.Select(i => i.UserRoleId));
        Assert.Equal([a1], Data(await Status(await Get($"{Path}?userId={user}&roleId={r1}", jwt), 200, "S10000", Success)).Items.Select(i => i.UserRoleId));
        Assert.Equal([a3], Data(await Status(await Get($"{Path}?id={a3}", jwt), 200, "S10000", Success)).Items.Select(i => i.UserRoleId));
        Assert.Empty(Data(await Status(await Get($"{Path}?id={a3}&userId={other}", jwt), 200, "S10000", Success)).Items);

        var second = Data(await Status(await Get($"{Path}?userId={user}&pageIndex=2&pageSize=2", jwt), 200, "S10000", Success));
        Assert.Equal([a3], second.Items.Select(i => i.UserRoleId));
        Assert.Equal((3L, 2L), (second.TotalItems, second.TotalPages));
        var beyond = Data(await Status(await Get($"{Path}?userId={user}&pageIndex=9&pageSize=2", jwt), 200, "S10000", Success));
        Assert.Equal((0, 3L), (beyond.Items.Count, beyond.TotalItems));
    }

    public static TheoryData<string, string> InvalidQueries => new()
    {
        { "?id=abc", "Id must be a valid UUID." },
        { "?userId=abc", "User Id must be a valid UUID." },
        { "?roleId=", "Role Id must be a valid UUID." },
        { "?pageSize=101", "Page Index and Page Size must be greater than 0, Page Size must not be greater than 100." },
        { "?pageIndex=9223372036854775807&pageSize=100", "Page Index is too large for Page Size." },
        { "?rowStatus=DELETED", "Row status must be one of ACTIVE, INACTIVE, LOCKED, ARCHIVED." },
        { "?search=a", "Search is not a known field." },
        { "?userId=a&userId=b", "User Id must not be duplicated." },
        { "?id=x&roleId=y&pageIndex=0", "Id must be a valid UUID, Role Id must be a valid UUID, Page Index and Page Size must be greater than 0, Page Size must not be greater than 100." }
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task TC_UROLEQ_003_query_validation(string query, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Get(Path + query, jwt), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_UROLEQ_004_body_order_credentials_and_403()
    {
        var (_, admin) = await AdminAsync();
        await Status(await Get(Path, admin, "{}"), 400, "E10001", "Request Body must be empty.");
        await Status(await Get($"{Path}?userId=x", null), 400, "E10001", "User Id must be a valid UUID.");
        await Status(await Get(Path, null), 401, "E10002", Invalid);
        await Status(await Get(Path, (await CallerAsync(null)).Jwt), 401, "E10002", Invalid);
        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
            await Status(await Get(Path, (await CallerAsync(roles)).Jwt), 403, "E10009", ReadForbidden);
    }

    [Fact]
    public async Task TC_UROLEQ_005_no_lock_timeout_and_a_disagreeing_page()
    {
        var (_, jwt) = await AdminAsync();
        var user = await UserAsync();
        var id = await UserRoleAsync(user, await RoleAsync(NewName()));
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        var (connection, transaction) = await HoldAsync("SELECT 1 FROM author.t_user_role WHERE id = @id FOR UPDATE", id);
        Assert.Single(Data(await Status(await Get($"{Path}?id={id}", jwt, client: client), 200, "S10000", Success)).Items);
        await transaction.RollbackAsync();
        await connection.DisposeAsync();

        await using (var table = new NpgsqlConnection(Postgres.ConnectionString))
        {
            await table.OpenAsync();
            await using var tx = await table.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand("LOCK TABLE author.t_user_role IN ACCESS EXCLUSIVE MODE", table, tx))
                await command.ExecuteNonQueryAsync();
            await Status(await Get(Path, jwt, client: client), 408, "E90001", "The server timed out waiting for the request.");
            await tx.RollbackAsync();
        }

        var faulty = Factory.WithWebHostBuilder(w => w.ConfigureTestServices(s =>
        {
            s.AddScoped<UserRoleQueryRepository>();
            s.AddScoped<IUserRoleQueryRepository>(p => new ShortPage(p.GetRequiredService<UserRoleQueryRepository>()));
        })).CreateClient();
        await Status(await Get($"{Path}?id={id}", jwt, client: faulty), 500, null, "An unexpected error occurred.");
    }

    private sealed class ShortPage(IUserRoleQueryRepository inner) : IUserRoleQueryRepository
    {
        public Task<long> CountAsync(UserRoleFilter filter, CancellationToken ct) => inner.CountAsync(filter, ct);
        public async Task<IReadOnlyList<UserRoleListRow>> ListAsync(UserRoleFilter filter, long offset, long limit, CancellationToken ct) =>
            (await inner.ListAsync(filter, offset, limit, ct)).Skip(1).ToList();
    }
}
