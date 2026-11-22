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

// e-commerce/doc/api/author/14-inquiry-role-permission/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class InquiryRolePermissionApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/role/permission";
    private const string Success = "Inquiry Role Permission Success";

    private sealed record Scope(Guid RolePermissionScopeId, Guid PermissionScopeId, Guid ScopeId, string Code, string RowStatus);

    private sealed record Item(Guid RolePermissionId, Guid RoleId, string RoleName, Guid PermissionId, string PermissionName, List<Scope> Scopes, string RowStatus);

    private static (List<Item> Items, long TotalItems, long TotalPages) Data(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["items", "pageIndex", "pageSize", "totalItems", "totalPages"], data.EnumerateObject().Select(p => p.Name));
        var items = data.GetProperty("items").EnumerateArray().Select(i =>
        {
            Assert.Equal(["rolePermissionId", "roleId", "roleName", "permissionId", "permissionName", "scopes", "rowStatus"], i.EnumerateObject().Select(p => p.Name));
            var scopes = i.GetProperty("scopes").EnumerateArray().Select(s =>
            {
                Assert.Equal(["rolePermissionScopeId", "permissionScopeId", "scopeId", "scope", "rowStatus"], s.EnumerateObject().Select(p => p.Name));
                return new Scope(s.GetProperty("rolePermissionScopeId").GetGuid(), s.GetProperty("permissionScopeId").GetGuid(), s.GetProperty("scopeId").GetGuid(),
                    s.GetProperty("scope").GetString()!, s.GetProperty("rowStatus").GetString()!);
            }).ToList();
            return new Item(i.GetProperty("rolePermissionId").GetGuid(), i.GetProperty("roleId").GetGuid(), i.GetProperty("roleName").GetString()!,
                i.GetProperty("permissionId").GetGuid(), i.GetProperty("permissionName").GetString()!, scopes, i.GetProperty("rowStatus").GetString()!);
        }).ToList();
        return (items, data.GetProperty("totalItems").GetInt64(), data.GetProperty("totalPages").GetInt64());
    }

    internal async Task<Guid> GrantAsync(Guid roleId, Guid permissionId, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.t_role_permission (id, created_by, row_status, role_id, permission_id) VALUES (@id, 'seed', @r, @role, @p)",
            ("id", id), ("r", rowStatus), ("role", roleId), ("p", permissionId));
        return id;
    }

    private async Task<Guid> GrantScopeAsync(Guid grantId, Guid definitionId, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.t_role_permission_scope (id, created_by, row_status, role_permission_id, permission_scope_id) VALUES (@id, 'seed', @r, @g, @ps)",
            ("id", id), ("r", rowStatus), ("g", grantId), ("ps", definitionId));
        return id;
    }

    [Fact]
    public async Task TC_RPERMQ_001_by_role_sorted_with_scopes()
    {
        var (_, jwt) = await AdminAsync();
        var role = await RoleAsync(NewName());
        var (pb, pa) = (await PermissionAsync("b" + NewPermissionName()), await PermissionAsync("a" + NewPermissionName()));
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (zulu, alpha) = (await ScopeAsync($"z_{tag}", "ACTIVE"), await ScopeAsync($"a_{tag}", "LOCKED"));
        var (dz, da) = (await PermissionScopeAsync(pa, zulu), await PermissionScopeAsync(pa, alpha));
        var (gb, ga) = (await GrantAsync(role, pb), await GrantAsync(role, pa, "INACTIVE"));
        var (lz, la) = (await GrantScopeAsync(ga, dz), await GrantScopeAsync(ga, da, "INACTIVE"));
        await GrantScopeAsync(ga, await PermissionScopeAsync(pa, await ScopeAsync()), "DELETED");

        var data = Data(await Status(await Get($"{Path}?roleId={role}", jwt), 200, "S10000", Success));
        Assert.Equal([ga, gb], data.Items.Select(i => i.RolePermissionId));
        Assert.Equal([new Scope(la, da, alpha, $"a_{tag}", "INACTIVE"), new Scope(lz, dz, zulu, $"z_{tag}", "ACTIVE")], data.Items[0].Scopes);
        Assert.Equal(("INACTIVE", 0), (data.Items[0].RowStatus, data.Items[1].Scopes.Count));
        Assert.Equal((2L, 1L), (data.TotalItems, data.TotalPages));
    }

    private const string ReadForbidden = "You do not have permission to read role permissions.";

    [Fact]
    public async Task TC_RPERMQ_002_filters_combine_pagination_and_scopes_per_grant()
    {
        var (_, jwt) = await AdminAsync();
        var (role, other) = (await RoleAsync(NewName()), await RoleAsync(NewName()));
        var (p1, p2, p3) = (await PermissionAsync("a" + NewPermissionName()), await PermissionAsync("b" + NewPermissionName()), await PermissionAsync("c" + NewPermissionName()));
        var (g1, g2, g3) = (await GrantAsync(role, p1), await GrantAsync(role, p2, "LOCKED"), await GrantAsync(role, p3));
        var l3 = await GrantScopeAsync(g3, await PermissionScopeAsync(p3, await ScopeAsync()));
        await GrantAsync(other, p1);

        Assert.Equal([g1, g2, g3], Data(await Status(await Get($"{Path}?roleId={role}", jwt), 200, "S10000", Success)).Items.Select(i => i.RolePermissionId));
        Assert.Equal([g2], Data(await Status(await Get($"{Path}?roleId={role}&rowStatus=LOCKED", jwt), 200, "S10000", Success)).Items.Select(i => i.RolePermissionId));
        Assert.Equal(2L, Data(await Status(await Get($"{Path}?permissionId={p1}", jwt), 200, "S10000", Success)).TotalItems);
        Assert.Empty(Data(await Status(await Get($"{Path}?id={g1}&roleId={other}", jwt), 200, "S10000", Success)).Items);

        var second = Data(await Status(await Get($"{Path}?roleId={role}&pageIndex=2&pageSize=2", jwt), 200, "S10000", Success));
        Assert.Equal([g3], second.Items.Select(i => i.RolePermissionId));
        Assert.Equal([l3], second.Items[0].Scopes.Select(sc => sc.RolePermissionScopeId));
        Assert.Equal((3L, 2L), (second.TotalItems, second.TotalPages));
    }

    public static TheoryData<string, string> InvalidQueries => new()
    {
        { "?id=abc", "Id must be a valid UUID." },
        { "?roleId=abc", "Role Id must be a valid UUID." },
        { "?permissionId=", "Permission Id must be a valid UUID." },
        { "?pageSize=101", "Page Index and Page Size must be greater than 0, Page Size must not be greater than 100." },
        { "?rowStatus=DELETED", "Row status must be one of ACTIVE, INACTIVE, LOCKED, ARCHIVED." },
        { "?userId=a", "User Id is not a known field." },
        { "?roleId=x&permissionId=y", "Role Id must be a valid UUID, Permission Id must be a valid UUID." }
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task TC_RPERMQ_003_query_validation(string query, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Get(Path + query, jwt), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_RPERMQ_004_body_order_credentials_and_403()
    {
        var (_, admin) = await AdminAsync();
        await Status(await Get(Path, admin, "{}"), 400, "E10001", "Request Body must be empty.");
        await Status(await Get($"{Path}?roleId=x", null), 400, "E10001", "Role Id must be a valid UUID.");
        await Status(await Get(Path, null), 401, "E10002", Invalid);
        await Status(await Get(Path, (await CallerAsync(null)).Jwt), 401, "E10002", Invalid);
        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
            await Status(await Get(Path, (await CallerAsync(roles)).Jwt), 403, "E10009", ReadForbidden);
    }

    [Fact]
    public async Task TC_RPERMQ_005_no_lock_timeout_and_a_disagreeing_page()
    {
        var (_, jwt) = await AdminAsync();
        var grant = await GrantAsync(await RoleAsync(NewName()), await PermissionAsync(NewPermissionName()));
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        var (connection, transaction) = await HoldAsync("SELECT 1 FROM author.t_role_permission WHERE id = @id FOR UPDATE", grant);
        Assert.Single(Data(await Status(await Get($"{Path}?id={grant}", jwt, client: client), 200, "S10000", Success)).Items);
        await transaction.RollbackAsync();
        await connection.DisposeAsync();

        await using (var table = new NpgsqlConnection(Postgres.ConnectionString))
        {
            await table.OpenAsync();
            await using var tx = await table.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand("LOCK TABLE author.t_role_permission IN ACCESS EXCLUSIVE MODE", table, tx))
                await command.ExecuteNonQueryAsync();
            await Status(await Get(Path, jwt, client: client), 408, "E90001", "The server timed out waiting for the request.");
            await tx.RollbackAsync();
        }

        var faulty = Factory.WithWebHostBuilder(w => w.ConfigureTestServices(sv =>
        {
            sv.AddScoped<RolePermissionQueryRepository>();
            sv.AddScoped<IRolePermissionQueryRepository>(p => new ShortPage(p.GetRequiredService<RolePermissionQueryRepository>()));
        })).CreateClient();
        await Status(await Get($"{Path}?id={grant}", jwt, client: faulty), 500, null, "An unexpected error occurred.");
    }

    private sealed class ShortPage(IRolePermissionQueryRepository inner) : IRolePermissionQueryRepository
    {
        public Task<long> CountAsync(RolePermissionFilter filter, CancellationToken ct) => inner.CountAsync(filter, ct);
        public async Task<IReadOnlyList<RolePermissionListRow>> ListAsync(RolePermissionFilter filter, long offset, long limit, CancellationToken ct) =>
            (await inner.ListAsync(filter, offset, limit, ct)).Skip(1).ToList();
        public Task<IReadOnlyList<RolePermissionScopeListRow>> ListScopesAsync(IReadOnlyCollection<Guid> grantIds, CancellationToken ct) => inner.ListScopesAsync(grantIds, ct);
    }
}
