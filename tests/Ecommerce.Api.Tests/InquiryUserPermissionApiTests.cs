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

// e-commerce/doc/api/author/16-inquiry-user-permission/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class InquiryUserPermissionApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/user/permission";
    private const string Success = "Inquiry User Permission Success";
    private const string ReadForbidden = "You do not have permission to read user permissions.";

    private sealed record Item(
        Guid UserPermissionId, Guid UserId, string? UserDisplayName, Guid PermissionId, string PermissionName,
        Guid? PermissionScopeId, Guid? ScopeId, string? Scope, string RowStatus);

    private static string? Text(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetString();
    private static Guid? Id(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetGuid();

    private static (List<Item> Items, long TotalItems, long TotalPages) Data(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["items", "pageIndex", "pageSize", "totalItems", "totalPages"], data.EnumerateObject().Select(p => p.Name));
        var items = data.GetProperty("items").EnumerateArray().Select(i =>
        {
            Assert.Equal(["userPermissionId", "userId", "userDisplayName", "permissionId", "permissionName", "permissionScopeId", "scopeId", "scope", "rowStatus"],
                i.EnumerateObject().Select(p => p.Name));
            return new Item(i.GetProperty("userPermissionId").GetGuid(), i.GetProperty("userId").GetGuid(), Text(i.GetProperty("userDisplayName")),
                i.GetProperty("permissionId").GetGuid(), i.GetProperty("permissionName").GetString()!, Id(i.GetProperty("permissionScopeId")),
                Id(i.GetProperty("scopeId")), Text(i.GetProperty("scope")), i.GetProperty("rowStatus").GetString()!);
        }).ToList();
        return (items, data.GetProperty("totalItems").GetInt64(), data.GetProperty("totalPages").GetInt64());
    }

    private async Task<Guid> NamedUserAsync(string? name)
    {
        var id = await UserAsync();
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.m_user SET display_name = @n WHERE id = @u", ("n", (object?)name ?? DBNull.Value), ("u", id));
        return id;
    }

    private async Task<List<Item>> ItemsAsync(string jwt, string query) =>
        Data(await Status(await Get(Path + query, jwt), 200, "S10000", Success)).Items;

    [Fact]
    public async Task TC_UPERMQ_001_items_sorted_with_their_scope_deleted_excluded()
    {
        var (_, jwt) = await AdminAsync();
        var (amy, bob, none) = (await NamedUserAsync("Amy"), await NamedUserAsync("Bob"), await NamedUserAsync(null));
        var tag = Guid.NewGuid().ToString("N");
        var (aName, bName) = ("a" + tag + ".manage", "b" + tag + ".manage");
        var (scoped, plain) = (await PermissionAsync(aName), await PermissionAsync(bName, "LOCKED"));
        var (low, high) = (await ScopeAsync("a_" + tag, "ACTIVE"), await ScopeAsync("z_" + tag, "LOCKED"));
        var (dLow, dHigh) = (await PermissionScopeAsync(scoped, low), await PermissionScopeAsync(scoped, high, "INACTIVE"));

        var bobHigh = await UserPermissionAsync(bob, scoped, dHigh);
        var amyHigh = await UserPermissionAsync(amy, scoped, dHigh, "LOCKED");
        var amyLow = await UserPermissionAsync(amy, scoped, dLow);
        var noneLow = await UserPermissionAsync(none, scoped, dLow);
        var amyUnscoped = await UserPermissionAsync(amy, scoped);
        var bobPlain = await UserPermissionAsync(bob, plain, rowStatus: "ARCHIVED");
        await UserPermissionAsync(amy, plain, rowStatus: "DELETED");

        // Permission name, then display name (null last), then scope code (no scope last), then id; closed masters still shown.
        Assert.Equal(
        [
            new Item(amyLow, amy, "Amy", scoped, aName, dLow, low, "a_" + tag, "ACTIVE"),
            new Item(amyHigh, amy, "Amy", scoped, aName, dHigh, high, "z_" + tag, "LOCKED"),
            new Item(amyUnscoped, amy, "Amy", scoped, aName, null, null, null, "ACTIVE"),
            new Item(bobHigh, bob, "Bob", scoped, aName, dHigh, high, "z_" + tag, "ACTIVE"),
            new Item(noneLow, none, null, scoped, aName, dLow, low, "a_" + tag, "ACTIVE")
        ], await ItemsAsync(jwt, $"?permissionId={scoped}"));
        Assert.Equal([new Item(bobPlain, bob, "Bob", plain, bName, null, null, null, "ARCHIVED")], await ItemsAsync(jwt, $"?permissionId={plain}"));
    }

    [Fact]
    public async Task TC_UPERMQ_002_filters_combine_and_pagination()
    {
        var (_, jwt) = await AdminAsync();
        var (user, other) = (await UserAsync(), await UserAsync());
        var tag = Guid.NewGuid().ToString("N");
        var (p1, p2, p3) = (await PermissionAsync("a" + tag + ".x"), await PermissionAsync("b" + tag + ".x"), await PermissionAsync("c" + tag + ".x"));
        var (g1, g2, g3) = (await UserPermissionAsync(user, p1), await UserPermissionAsync(user, p2, rowStatus: "INACTIVE"), await UserPermissionAsync(user, p3));
        await UserPermissionAsync(other, p1);
        var gone = await UserPermissionAsync(other, p2, rowStatus: "DELETED");

        Assert.Equal([g1, g2, g3], (await ItemsAsync(jwt, $"?userId={user}")).Select(i => i.UserPermissionId));
        Assert.Equal([g2], (await ItemsAsync(jwt, $"?userId={user}&rowStatus=INACTIVE")).Select(i => i.UserPermissionId));
        Assert.Equal([g1], (await ItemsAsync(jwt, $"?userId={user}&permissionId={p1}")).Select(i => i.UserPermissionId));
        Assert.Equal([g3], (await ItemsAsync(jwt, $"?id={g3}")).Select(i => i.UserPermissionId));
        Assert.Empty(await ItemsAsync(jwt, $"?id={g3}&userId={other}"));

        foreach (var missing in new[] { gone, Guid.NewGuid() })
        {
            var data = Data(await Status(await Get($"{Path}?id={missing}", jwt), 200, "S10000", Success));
            Assert.Equal((0, 0L, 0L), (data.Items.Count, data.TotalItems, data.TotalPages));
        }

        var second = Data(await Status(await Get($"{Path}?userId={user}&pageIndex=2&pageSize=2", jwt), 200, "S10000", Success));
        Assert.Equal([g3], second.Items.Select(i => i.UserPermissionId));
        Assert.Equal((3L, 2L), (second.TotalItems, second.TotalPages));
        var beyond = Data(await Status(await Get($"{Path}?userId={user}&pageIndex=9&pageSize=2", jwt), 200, "S10000", Success));
        Assert.Equal((0, 3L, 2L), (beyond.Items.Count, beyond.TotalItems, beyond.TotalPages));
    }

    public static TheoryData<string, string> InvalidQueries => new()
    {
        { "?id=abc", "Id must be a valid UUID." },
        { "?userId=abc", "User Id must be a valid UUID." },
        { "?permissionId=", "Permission Id must be a valid UUID." },
        { "?pageSize=101", "Page Index and Page Size must be greater than 0, Page Size must not be greater than 100." },
        { "?pageIndex=9223372036854775807&pageSize=100", "Page Index is too large for Page Size." },
        { "?rowStatus=DELETED", "Row status must be one of ACTIVE, INACTIVE, LOCKED, ARCHIVED." },
        { "?roleId=a", "Role Id is not a known field." },
        { "?permissionId=a&permissionId=b", "Permission Id must not be duplicated." },
        { "?id=x&permissionId=y&pageIndex=0", "Id must be a valid UUID, Permission Id must be a valid UUID, Page Index and Page Size must be greater than 0, Page Size must not be greater than 100." }
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task TC_UPERMQ_003_query_validation(string query, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Get(Path + query, jwt), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_UPERMQ_004_body_order_credentials_and_403()
    {
        var (_, admin) = await AdminAsync();
        await Status(await Get(Path, admin, "{}"), 400, "E10001", "Request Body must be empty.");
        await Status(await Get($"{Path}?permissionId=x", null), 400, "E10001", "Permission Id must be a valid UUID.");
        await Status(await Get(Path, null), 401, "E10002", Invalid);
        await Status(await Get(Path, (await CallerAsync(null)).Jwt), 401, "E10002", Invalid);
        var (inactive, inactiveJwt) = await AdminAsync();
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'INACTIVE' WHERE id = @u", ("u", inactive));
        await Status(await Get(Path, inactiveJwt), 401, "E10002", Invalid);
        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
            await Status(await Get(Path, (await CallerAsync(roles)).Jwt), 403, "E10009", ReadForbidden);
    }

    [Fact]
    public async Task TC_UPERMQ_005_no_lock_timeout_and_a_disagreeing_page()
    {
        var (_, jwt) = await AdminAsync();
        var (user, permission, scope) = (await UserAsync(), await PermissionAsync(NewPermissionName()), await ScopeAsync());
        var definition = await PermissionScopeAsync(permission, scope);
        var id = await UserPermissionAsync(user, permission, definition);
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        foreach (var (table, target) in new[] { ("author.t_user_permission", id), ("authen.m_user", user), ("author.m_permission", permission), ("author.t_permission_scope", definition), ("author.m_scope", scope) })
        {
            var (connection, transaction) = await HoldAsync($"SELECT 1 FROM {table} WHERE id = @id FOR UPDATE", target);
            Assert.Single(Data(await Status(await Get($"{Path}?id={id}", jwt, client: client), 200, "S10000", Success)).Items);
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }
        Assert.True(await ScalarAsync<bool>("SELECT updated_by IS NULL FROM author.t_user_permission WHERE id = @id", id));

        await using (var held = new NpgsqlConnection(Postgres.ConnectionString))
        {
            await held.OpenAsync();
            await using var tx = await held.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand("LOCK TABLE author.t_user_permission IN ACCESS EXCLUSIVE MODE", held, tx))
                await command.ExecuteNonQueryAsync();
            await Status(await Get(Path, jwt, client: client), 408, "E90001", "The server timed out waiting for the request.");
            await tx.RollbackAsync();
        }

        var faulty = Factory.WithWebHostBuilder(w => w.ConfigureTestServices(s =>
        {
            s.AddScoped<UserPermissionQueryRepository>();
            s.AddScoped<IUserPermissionQueryRepository>(p => new ShortPage(p.GetRequiredService<UserPermissionQueryRepository>()));
        })).CreateClient();
        await Status(await Get($"{Path}?id={id}", jwt, client: faulty), 500, null, "An unexpected error occurred.");
    }

    private sealed class ShortPage(IUserPermissionQueryRepository inner) : IUserPermissionQueryRepository
    {
        public Task<long> CountAsync(UserPermissionFilter filter, CancellationToken ct) => inner.CountAsync(filter, ct);
        public async Task<IReadOnlyList<UserPermissionListRow>> ListAsync(UserPermissionFilter filter, long offset, long limit, CancellationToken ct) =>
            (await inner.ListAsync(filter, offset, limit, ct)).Skip(1).ToList();
    }
}
