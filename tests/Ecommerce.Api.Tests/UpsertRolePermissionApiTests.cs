using System.Text.Json;
using System.Text.Json.Nodes;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Author;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/author/13-upsert-role-permission/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class UpsertRolePermissionApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/role/permission/upsert";

    private static JsonObject Body(Guid roleId, Guid permissionId, Guid? id = null, string? rowStatus = null, params JsonNode[] scopes)
    {
        var body = new JsonObject { ["roleId"] = roleId.ToString(), ["permissionId"] = permissionId.ToString() };
        if (id is not null) body["id"] = id.ToString();
        if (rowStatus is not null) body["rowStatus"] = rowStatus;
        if (scopes.Length > 0) body["scopes"] = new JsonArray(scopes);
        return body;
    }

    private static JsonObject Scope(Guid scopeId, Guid? id = null, string? rowStatus = null) => UpsertPermissionApiTests.Scope(scopeId, id, rowStatus);

    private Task<HttpResponseMessage> Send(string? jwt, JsonNode body, HttpClient? client = null) => Post(Path, jwt, body.ToJsonString(), client);

    private static Guid GrantId(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["rolePermissionId"], data.EnumerateObject().Select(p => p.Name));
        return data.GetProperty("rolePermissionId").GetGuid();
    }

    private Task<long> ActiveScopesAsync(Guid grantId) =>
        ScalarAsync<long>("SELECT COUNT(*) FROM author.t_role_permission_scope WHERE role_permission_id = @id AND row_status = 'ACTIVE'", grantId);

    [Fact]
    public async Task TC_RPERM_001_insert_with_a_scope()
    {
        var (adminId, jwt) = await AdminAsync();
        var (role, permission, scope) = (await RoleAsync(NewName()), await PermissionAsync(NewPermissionName()), await ScopeAsync());
        await PermissionScopeAsync(permission, scope);

        var id = GrantId(await Status(await Send(jwt, Body(role, permission, scopes: [Scope(scope)])), 201, "S10000", "Insert Role Permission Success"));
        Assert.Equal(("ACTIVE", adminId.ToString(), 1L),
            (await ScalarAsync<string>("SELECT row_status FROM author.t_role_permission WHERE id = @id", id),
             await ScalarAsync<string>("SELECT created_by FROM author.t_role_permission WHERE id = @id", id), await ActiveScopesAsync(id)));
    }

    private const string AssignForbidden = "You do not have permission to assign permissions.";

    private Task<string> GrantStatusAsync(Guid id) => ScalarAsync<string>("SELECT row_status FROM author.t_role_permission WHERE id = @id", id);

    private Task<string> LinkStatusAsync(Guid id) => ScalarAsync<string>("SELECT row_status FROM author.t_role_permission_scope WHERE id = @id", id);

    private async Task<Guid> GrantAsync(Guid roleId, Guid permissionId, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.t_role_permission (id, created_by, row_status, role_id, permission_id) VALUES (@id, 'seed', @r, @role, @p)",
            ("id", id), ("r", rowStatus), ("role", roleId), ("p", permissionId));
        return id;
    }

    private async Task<Guid> GrantScopeAsync(Guid grantId, Guid permissionScopeId, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.t_role_permission_scope (id, created_by, row_status, role_permission_id, permission_scope_id) VALUES (@id, 'seed', @r, @g, @ps)",
            ("id", id), ("r", rowStatus), ("g", grantId), ("ps", permissionScopeId));
        return id;
    }

    /// <summary>A permission with one ACTIVE scope definition, and a role.</summary>
    private async Task<(Guid Role, Guid Permission, Guid Scope, Guid Definition)> ScopedAsync()
    {
        var (role, permission, scope) = (await RoleAsync(NewName()), await PermissionAsync(NewPermissionName()), await ScopeAsync());
        return (role, permission, scope, await PermissionScopeAsync(permission, scope));
    }

    [Fact]
    public async Task TC_RPERM_002_update_archive_reopen_and_delete_with_its_scopes()
    {
        var (adminId, jwt) = await AdminAsync();
        var (role, permission, scope, definition) = await ScopedAsync();
        var grant = await GrantAsync(role, permission);
        var link = await GrantScopeAsync(grant, definition);

        Assert.Equal(grant, GrantId(await Status(await Send(jwt, Body(role, permission, grant, "LOCKED")), 200, "S10000", "Update Role Permission Success")));
        Assert.Equal(("LOCKED", adminId.ToString()), (await GrantStatusAsync(grant), await ScalarAsync<string>("SELECT updated_by FROM author.t_role_permission WHERE id = @id", grant)));
        await Status(await Send(jwt, Body(role, permission, grant, "ARCHIVED")), 200, "S10000", "Archive Role Permission Success");
        await Status(await Send(jwt, Body(role, permission, grant)), 200, "S10000", "Update Role Permission Success");

        await Status(await Send(jwt, Body(role, permission, grant, "DELETED")), 204, null, null);
        Assert.Equal(("DELETED", "DELETED"), (await GrantStatusAsync(grant), await LinkStatusAsync(link)));
        await Status(await Send(jwt, Body(role, permission, grant)), 404, "E10004", "Role permission not found.");
        Assert.NotEqual(grant, GrantId(await Status(await Send(jwt, Body(role, permission, scopes: [Scope(scope)])), 201, "S10000", "Insert Role Permission Success")));
    }

    public static TheoryData<string, string> InvalidBodies => new()
    {
        { "[]", "Request Body must be an object." },
        { """{"roleId":"x","permissionId":"y","extra":1}""", "Extra is not a known field." },
        { """{"roleId":null}""", "Role Id and Permission Id are required fields." },
        { """{"roleId":"x","permissionId":"6f1c0e1e-0000-4000-8000-000000000001"}""", "Role Id must be a valid UUID." },
        { """{"roleId":"6f1c0e1e-0000-4000-8000-000000000001","permissionId":"6f1c0e1e-0000-4000-8000-000000000002","rowStatus":"DELETED"}""", "Row status must not be DELETED when inserting a role permission." },
        { """{"roleId":"6f1c0e1e-0000-4000-8000-000000000001","permissionId":"6f1c0e1e-0000-4000-8000-000000000002","scopes":null}""", "Scopes must be an array." },
        { """{"roleId":"6f1c0e1e-0000-4000-8000-000000000001","permissionId":"6f1c0e1e-0000-4000-8000-000000000002","scopes":[{"scopeId":"x"}]}""", "scopes[0]: Scope Id must be a valid UUID." },
        { """{"roleId":"6f1c0e1e-0000-4000-8000-000000000001","permissionId":"6f1c0e1e-0000-4000-8000-000000000002","scopes":[{"scopeId":"6f1c0e1e-0000-4000-8000-000000000003"},{"scopeId":"6f1c0e1e-0000-4000-8000-000000000003"}]}""", "scopes[1]: Scope Id is duplicated with scopes[0]." },
        { """{"roleId":"6f1c0e1e-0000-4000-8000-000000000001","permissionId":"6f1c0e1e-0000-4000-8000-000000000002","scopes":[{"scopeId":"6f1c0e1e-0000-4000-8000-000000000003","rowStatus":"DELETED"}]}""", "scopes[0]: Row status must not be DELETED when inserting a scope." }
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task TC_RPERM_003_input_validation(string body, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Post(Path, jwt, body), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_RPERM_004_validation_before_the_credential_and_credential_failures()
    {
        var (role, permission, _, _) = await ScopedAsync();
        await Status(await Post(Path, null, """{"roleId":"x","permissionId":"y"}"""), 400, "E10001", "Role Id must be a valid UUID, Permission Id must be a valid UUID.");
        await Status(await Send(null, Body(role, permission)), 401, "E10002", Invalid);
        await Status(await Send((await CallerAsync(null)).Jwt, Body(role, permission)), 401, "E10002", Invalid);
        var (callerId, jwt) = await AdminAsync();
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.t_user_access_token SET row_status = 'INACTIVE' WHERE user_id = @u", ("u", callerId));
        await Status(await Send(jwt, Body(role, permission)), 401, "E10002", Invalid);
    }

    [Fact]
    public async Task TC_RPERM_005_the_grant_id_its_fixed_keys_and_active_rows_to_give()
    {
        var (_, jwt) = await AdminAsync();
        var (role, permission, scope, definition) = await ScopedAsync();
        var grant = await GrantAsync(role, permission, "INACTIVE");
        await GrantScopeAsync(grant, definition);

        await Status(await Send(jwt, Body(role, permission, Guid.NewGuid())), 404, "E10004", "Role permission not found.");
        await Status(await Send(jwt, Body(await RoleAsync(NewName()), permission, grant)), 400, "E10001", "Role Id and Permission Id cannot be changed.");

        // Giving needs an ACTIVE role and permission; reopening counts as giving; taking away does not.
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE author.m_role SET row_status = 'LOCKED' WHERE id = @r", ("r", role));
        await Status(await Send(jwt, Body(role, permission, grant)), 404, "E10005", "Role or permission not found or not ACTIVE.");
        await Status(await Send(jwt, Body(role, await PermissionAsync(NewPermissionName(), "INACTIVE"))), 404, "E10005", "Role or permission not found or not ACTIVE.");
        await Status(await Send(jwt, Body(await RoleAsync(NewName()), Guid.NewGuid())), 404, "E10005", "Role or permission not found or not ACTIVE.");
        await Status(await Send(jwt, Body(role, permission, grant, "ARCHIVED")), 200, "S10000", "Archive Role Permission Success");
    }

    [Fact]
    public async Task TC_RPERM_006_scope_items()
    {
        var (_, jwt) = await AdminAsync();
        var (role, permission, scope, definition) = await ScopedAsync();
        var grant = await GrantAsync(role, permission);
        var link = await GrantScopeAsync(grant, definition);
        var other = await ScopeAsync();
        await PermissionScopeAsync(permission, other);

        await Status(await Send(jwt, Body(role, permission, grant, scopes: [Scope(scope, Guid.NewGuid())])), 404, "E10004", "scopes[0]: Role permission scope not found.");
        await Status(await Send(jwt, Body(role, permission, grant, scopes: [Scope(other, link)])), 400, "E10001", "scopes[0]: Scope Id cannot be changed.");
        await Status(await Send(jwt, Body(role, permission, grant, scopes: [Scope(await ScopeAsync())])), 400, "E10001", "scopes[0]: Scope is not a scope of the permission.");
        await Status(await Send(jwt, Body(await RoleAsync(NewName()), permission, scopes: [Scope(scope, link)])), 404, "E10004", "scopes[0]: Role permission scope not found.");

        // A new link needs an ACTIVE definition and master; keeping or removing an existing one does not.
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE author.m_scope SET row_status = 'LOCKED' WHERE id = @s", ("s", other));
        await Status(await Send(jwt, Body(role, permission, grant, scopes: [Scope(other)])), 404, "E10005", "scopes[0]: Scope not found or not ACTIVE.");
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE author.t_permission_scope SET row_status = 'INACTIVE' WHERE id = @d", ("d", definition));
        await Status(await Send(jwt, Body(role, permission, grant, scopes: [Scope(scope, link)])), 200, "S10000", "Update Role Permission Success");
    }

    [Fact]
    public async Task TC_RPERM_007_only_a_platform_administrator_before_the_duplicate_check()
    {
        var (role, permission, scope, _) = await ScopedAsync();
        await GrantAsync(role, permission);
        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
        {
            var (_, jwt) = await CallerAsync(roles);
            await Status(await Send(jwt, Body(role, permission, scopes: [Scope(scope)])), 403, "E10009", AssignForbidden);
        }
    }

    [Fact]
    public async Task TC_RPERM_008_duplicate_grant_and_scope()
    {
        var (_, jwt) = await AdminAsync();
        var (role, permission, scope, _) = await ScopedAsync();
        await GrantAsync(role, permission, "ARCHIVED");
        await Status(await Send(jwt, Body(role, permission, scopes: [Scope(scope)])), 409, "E10003", "The role already has this permission.");

        var (role2, permission2, scope2, definition2) = await ScopedAsync();
        var grant = await GrantAsync(role2, permission2);
        var link = await GrantScopeAsync(grant, definition2);
        await Status(await Send(jwt, Body(role2, permission2, grant, scopes: [Scope(scope2)])), 409, "E10003", "scopes[0]: The role already has this scope for the permission.");
        // Removing the link and adding the scope again in one request is allowed.
        await Status(await Send(jwt, Body(role2, permission2, grant, scopes: [Scope(scope2, link, "DELETED"), Scope(scope2)])), 200, "S10000", "Update Role Permission Success");
        Assert.Equal(("DELETED", 1L), (await LinkStatusAsync(link), await ActiveScopesAsync(grant)));
    }

    [Fact]
    public async Task TC_RPERM_009_an_active_grant_of_a_scoped_permission_keeps_a_scope()
    {
        var (_, jwt) = await AdminAsync();
        var (role, permission, scope, definition) = await ScopedAsync();
        const string NeedsScope = "An ACTIVE grant of a scoped permission needs at least one ACTIVE scope.";

        await Status(await Send(jwt, Body(role, permission)), 400, "E10001", NeedsScope);
        var grant = await GrantAsync(role, permission);
        var link = await GrantScopeAsync(grant, definition);
        await Status(await Send(jwt, Body(role, permission, grant, scopes: [Scope(scope, link, "DELETED")])), 400, "E10001", NeedsScope);
        Assert.Equal("ACTIVE", await LinkStatusAsync(link));

        // An INACTIVE grant may be left without scopes; a permission without scopes needs none.
        await Status(await Send(jwt, Body(role, permission, grant, "INACTIVE", Scope(scope, link, "DELETED"))), 200, "S10000", "Update Role Permission Success");
        await Status(await Send(jwt, Body(role, await PermissionAsync(NewPermissionName()))), 201, "S10000", "Insert Role Permission Success");
    }

    [Fact]
    public async Task TC_RPERM_010_concurrent_grants_of_one_pair()
    {
        var jwts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AdminAsync()));
        var (role, permission) = (await RoleAsync(NewName()), await PermissionAsync(NewPermissionName()));
        var responses = await Task.WhenAll(jwts.Select(a => Send(a.Jwt, Body(role, permission))));

        Assert.Equal(1, responses.Count(r => (int)r.StatusCode == 201));
        foreach (var conflict in responses.Where(r => (int)r.StatusCode != 201))
            await Status(conflict, 409, "E10003", "The role already has this permission.");
    }

    [Fact]
    public async Task TC_RPERM_011_role_and_permission_locks()
    {
        var (role, permission) = (await RoleAsync(NewName()), await PermissionAsync(NewPermissionName()));
        var (_, customer) = await CallerAsync(new[] { "CUSTOMER" });
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        foreach (var (sql, target) in new[] { ("SELECT 1 FROM author.m_role WHERE id = @id FOR SHARE", role), ("SELECT 1 FROM author.m_permission WHERE id = @id FOR SHARE", permission) })
        {
            var (connection, transaction) = await HoldAsync(sql, target);
            await Status(await Send(customer, Body(role, permission), client), 408, "E90001", null);
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }

        await Status(await Send(customer, Body(role, permission), client), 403, "E10009", AssignForbidden);
    }

    private sealed class FailingScopeInsert(IRolePermissionRepository inner) : IRolePermissionRepository
    {
        public Task<RolePermissionRow?> LockGrantAsync(Guid id, CancellationToken ct) => inner.LockGrantAsync(id, ct);
        public Task<IReadOnlyList<PermissionScopeDefinition>> DefinitionsAsync(Guid permissionId, CancellationToken ct) => inner.DefinitionsAsync(permissionId, ct);
        public Task<IReadOnlyList<GrantScopeRow>> LockScopesAsync(Guid grantId, CancellationToken ct) => inner.LockScopesAsync(grantId, ct);
        public Task<bool> TakenAsync(Guid roleId, Guid permissionId, Guid? excludeId, CancellationToken ct) => inner.TakenAsync(roleId, permissionId, excludeId, ct);
        public Task<Guid> InsertAsync(Guid roleId, Guid permissionId, string rowStatus, string by, DateTime now, CancellationToken ct) => inner.InsertAsync(roleId, permissionId, rowStatus, by, now, ct);
        public Task<bool> UpdateAsync(Guid id, string rowStatus, string by, DateTime now, CancellationToken ct) => inner.UpdateAsync(id, rowStatus, by, now, ct);
        public Task<bool> UpdateScopeAsync(Guid id, Guid grantId, string rowStatus, string by, DateTime now, CancellationToken ct) => inner.UpdateScopeAsync(id, grantId, rowStatus, by, now, ct);
        public Task DeleteScopesAsync(Guid grantId, string by, DateTime now, CancellationToken ct) => inner.DeleteScopesAsync(grantId, by, now, ct);
        public Task<long> ActiveScopesAsync(Guid grantId, CancellationToken ct) => inner.ActiveScopesAsync(grantId, ct);

        public async Task<Guid> InsertScopeAsync(Guid grantId, Guid permissionScopeId, string rowStatus, string by, DateTime now, CancellationToken ct)
        {
            await inner.InsertScopeAsync(grantId, permissionScopeId, rowStatus, by, now, ct);
            throw new InvalidOperationException("injected after the scope insert");
        }
    }

    [Fact]
    public async Task TC_RPERM_012_an_unexpected_failure_rolls_back_the_grant_and_its_scopes()
    {
        var (_, jwt) = await AdminAsync();
        var (role, permission, scope, _) = await ScopedAsync();
        var client = Factory.WithWebHostBuilder(w => w.ConfigureTestServices(s =>
        {
            s.AddScoped<RolePermissionRepository>();
            s.AddScoped<IRolePermissionRepository>(p => new FailingScopeInsert(p.GetRequiredService<RolePermissionRepository>()));
        })).CreateClient();

        await Status(await Send(jwt, Body(role, permission, scopes: [Scope(scope)]), client), 500, null, "An unexpected error occurred.");
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM author.t_role_permission WHERE role_id = @id", role));
    }
}
