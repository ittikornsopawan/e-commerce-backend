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

// e-commerce/doc/api/author/04-upsert-permission/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class UpsertPermissionApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/permission/upsert";

    private static JsonObject Body(string name, string description = "A permission", Guid? id = null, string? rowStatus = null, params JsonNode[] scopes)
    {
        var body = new JsonObject { ["name"] = name, ["description"] = description };
        if (id is not null) body["id"] = id.ToString();
        if (rowStatus is not null) body["rowStatus"] = rowStatus;
        if (scopes.Length > 0) body["scopes"] = new JsonArray(scopes);
        return body;
    }

    private static JsonObject Scope(Guid scopeId, Guid? id = null, string? rowStatus = null)
    {
        var scope = new JsonObject { ["scopeId"] = scopeId.ToString() };
        if (id is not null) scope["id"] = id.ToString();
        if (rowStatus is not null) scope["rowStatus"] = rowStatus;
        return scope;
    }

    private Task<HttpResponseMessage> Send(string? jwt, JsonNode body, HttpClient? client = null) => Post(Path, jwt, body.ToJsonString(), client);

    private static Guid PermissionId(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["permissionId"], data.EnumerateObject().Select(p => p.Name));
        return data.GetProperty("permissionId").GetGuid();
    }

    private Task<long> LinkCountAsync(Guid permissionId) =>
        ScalarAsync<long>("SELECT COUNT(*) FROM author.t_permission_scope WHERE permission_id = @id AND row_status <> 'DELETED'", permissionId);

    [Fact]
    public async Task TC_PERM_001_insert_with_scopes()
    {
        var (adminId, jwt) = await AdminAsync();
        var (s1, s2) = (await ScopeAsync(), await ScopeAsync());
        var name = NewPermissionName();

        var id = PermissionId(await Status(await Send(jwt, Body(name, "Reads reports", null, null, Scope(s1), Scope(s2))), 201, "S10000", "Insert Permission Success"));

        Assert.Equal((name, "ACTIVE", adminId.ToString()), (
            await ScalarAsync<string>("SELECT name FROM author.m_permission WHERE id = @id", id),
            await ScalarAsync<string>("SELECT row_status FROM author.m_permission WHERE id = @id", id),
            await ScalarAsync<string>("SELECT created_by FROM author.m_permission WHERE id = @id", id)));
        Assert.Equal(2L, await LinkCountAsync(id));
    }

    private const string ManageForbidden = "You do not have permission to manage permissions.";

    private async Task<long> PermissionNameCountAsync(string name)
    {
        await using var connection = new Npgsql.NpgsqlConnection(Postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT COUNT(*) FROM author.m_permission WHERE name = @n", connection);
        command.Parameters.AddWithValue("n", name);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private Task<string> StatusOfAsync(Guid permissionId) =>
        ScalarAsync<string>("SELECT row_status FROM author.m_permission WHERE id = @id", permissionId);

    private Task<string> LinkStatusAsync(Guid linkId) =>
        ScalarAsync<string>("SELECT row_status FROM author.t_permission_scope WHERE id = @id", linkId);

    /// <summary>A role grant of the permission, optionally scoped to one of its links.</summary>
    private async Task<Guid> RoleGrantAsync(Guid permissionId, Guid? linkId = null, string rowStatus = "ACTIVE")
    {
        var grant = Guid.NewGuid();
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.t_role_permission (id, created_by, row_status, role_id, permission_id) VALUES (@id, 'seed', @r, @role, @p)",
            ("id", grant), ("r", rowStatus), ("role", await RoleAsync(NewName())), ("p", permissionId));
        if (linkId is { } link)
            await ExecuteAsync(Postgres.ConnectionString,
                "INSERT INTO author.t_role_permission_scope (created_by, role_permission_id, permission_scope_id) VALUES ('seed', @g, @l)", ("g", grant), ("l", link));
        return grant;
    }

    /// <summary>A direct-user grant of the permission, optionally scoped to one of its links.</summary>
    private async Task UserGrantAsync(Guid userId, Guid permissionId, Guid? linkId = null) =>
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.t_user_permission (created_by, row_status, user_id, permission_id, permission_scope_id) VALUES ('seed', 'INACTIVE', @u, @p, @l)",
            ("u", userId), ("p", permissionId), ("l", (object?)linkId ?? DBNull.Value));

    [Fact]
    public async Task TC_PERM_002_update_archive_reopen_and_delete()
    {
        var (adminId, jwt) = await AdminAsync();
        var name = NewPermissionName();
        var id = await PermissionAsync(name);
        var renamed = NewPermissionName();

        Assert.Equal(id, PermissionId(await Status(await Send(jwt, Body(renamed, "Changed", id, "LOCKED")), 200, "S10000", "Update Permission Success")));
        Assert.Equal((renamed, "LOCKED", adminId.ToString()), (
            await ScalarAsync<string>("SELECT name FROM author.m_permission WHERE id = @id", id), await StatusOfAsync(id),
            await ScalarAsync<string>("SELECT updated_by FROM author.m_permission WHERE id = @id", id)));

        Assert.Equal(id, PermissionId(await Status(await Send(jwt, Body(renamed, "Changed", id, "ARCHIVED")), 200, "S10000", "Archive Permission Success")));
        await Status(await Send(jwt, Body(renamed, "Changed", id)), 200, "S10000", "Update Permission Success");
        Assert.Equal("ACTIVE", await StatusOfAsync(id));

        await Status(await Send(jwt, Body(renamed, "Changed", id, "DELETED")), 204, null, null);
        // DELETED is final: the id is not found and the name is free again.
        await Status(await Send(jwt, Body(renamed, "Back", id)), 404, "E10004", "Permission not found.");
        await Status(await Send(jwt, Body(renamed)), 201, "S10000", "Insert Permission Success");

        // A new permission requested ARCHIVED is an insert.
        var archived = PermissionId(await Status(await Send(jwt, Body(NewPermissionName(), rowStatus: "ARCHIVED")), 201, "S10000", "Insert Permission Success"));
        Assert.Equal("ARCHIVED", await StatusOfAsync(archived));
    }

    public static TheoryData<string, string> InvalidBodies => new()
    {
        { "[]", "Request Body must be an object." },
        { """{"name":"a.b","description":"d","code":1}""", "Code is not a known field." },
        { """{"name":"a.b","description":"d","name":"c.d"}""", "Name must not be duplicated." },
        { """{"name":" ","description":""}""", "Name and Description are required fields." },
        { """{"name":"catalog","description":"d"}""", "Permission name must be lower-case letters, digits, _ and ., with at least two parts separated by a dot, at most 64 characters." },
        { """{"name":"Catalog.manage","description":"d"}""", "Permission name must be lower-case letters, digits, _ and ., with at least two parts separated by a dot, at most 64 characters." },
        { """{"name":"a.b","description":"d","rowStatus":"DELETED"}""", "Row status must not be DELETED when inserting a permission." },
        { """{"id":null,"name":"a.b","description":"d"}""", "Id must be a valid UUID." },
        { """{"name":"a.b","description":"d","scopes":null}""", "Scopes must be an array." },
        { """{"name":"a.b","description":"d","scopes":[null]}""", "scopes[0]: Scope must be an object." },
        { """{"name":"a.b","description":"d","scopes":[{"scopeId":"x","extra":1}]}""", "scopes[0]: Extra is not a known field." },
        { """{"name":"a.b","description":"d","scopes":[{}]}""", "scopes[0]: Scope Id is a required field." },
        { """{"name":"a.b","description":"d","scopes":[{"scopeId":"x"}]}""", "scopes[0]: Scope Id must be a valid UUID." },
        { """{"name":"a.b","description":"d","scopes":[{"scopeId":"6f1c0e1e-0000-4000-8000-000000000001","rowStatus":"INACTIVE"}]}""", "scopes[0]: Row Status must be ACTIVE or DELETED." },
        { """{"name":"a.b","description":"d","scopes":[{"scopeId":"6f1c0e1e-0000-4000-8000-000000000001","rowStatus":"DELETED"}]}""", "scopes[0]: Row status must not be DELETED when inserting a scope." },
        { """{"name":"a.b","description":"d","scopes":[{"scopeId":"6f1c0e1e-0000-4000-8000-000000000001"},{"scopeId":"6F1C0E1E-0000-4000-8000-000000000001"}]}""", "scopes[1]: Scope Id is duplicated with scopes[0]." },
        { """{"name":"a.b","description":"d","scopes":[{"id":"6f1c0e1e-0000-4000-8000-000000000009","scopeId":"6f1c0e1e-0000-4000-8000-000000000001"},{"id":"6f1c0e1e-0000-4000-8000-000000000009","scopeId":"6f1c0e1e-0000-4000-8000-000000000002"}]}""", "scopes[1]: Id is duplicated with scopes[0]." },
        { """{"name":"x","description":"","scopes":[{"scopeId":"x"}]}""", "Description is a required field, Permission name must be lower-case letters, digits, _ and ., with at least two parts separated by a dot, at most 64 characters, scopes[0]: Scope Id must be a valid UUID." }
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task TC_PERM_003_input_validation(string body, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Post(Path, jwt, body), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_PERM_003_lengths_query_and_deleted_links_do_not_reserve()
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Send(jwt, Body("a." + new string('b', 63))), 400, "E10001",
            "Permission name must be lower-case letters, digits, _ and ., with at least two parts separated by a dot, at most 64 characters.");
        await Status(await Send(jwt, Body(NewPermissionName(), new string('d', 257))), 400, "E10001", "Description must be at most 256 characters.");
        await Status(await Post(Path + "?x=1", jwt, Body(NewPermissionName()).ToJsonString()), 400, "E10001", "X is not a known field.");

        // A DELETED link and an ACTIVE one may name the same scope: the removal does not reserve it (it fails later on the data).
        var scope = await ScopeAsync();
        var body = Body(NewPermissionName(), id: Guid.NewGuid(), scopes: [Scope(scope, Guid.NewGuid(), "DELETED"), Scope(scope)]);
        await Status(await Send(jwt, body), 404, "E10004", "Permission not found.");

        var exact = "a." + new string('b', 62);
        await Status(await Send(jwt, Body(exact, new string('d', 256))), 201, "S10000", "Insert Permission Success");
    }

    [Fact]
    public async Task TC_PERM_004_validation_before_the_credential_and_credential_failures()
    {
        await Status(await Send(null, Body("bad")), 400, "E10001",
            "Permission name must be lower-case letters, digits, _ and ., with at least two parts separated by a dot, at most 64 characters.");

        var name = NewPermissionName();
        await Status(await Send(null, Body(name)), 401, "E10002", Invalid);
        await Status(await Send("not-a-jwt", Body(name)), 401, "E10002", Invalid);
        await Status(await Send((await CallerAsync(null)).Jwt, Body(name)), 401, "E10002", Invalid);
        var (userId, jwt) = await AdminAsync();
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.t_user_access_token SET row_status = 'INACTIVE' WHERE user_id = @u", ("u", userId));
        await Status(await Send(jwt, Body(name)), 401, "E10002", Invalid);
        Assert.Equal(0L, await PermissionNameCountAsync(name));
    }

    [Fact]
    public async Task TC_PERM_005_only_a_platform_administrator_after_the_permission_lookup()
    {
        var id = await PermissionAsync(NewPermissionName());
        var deleted = await PermissionAsync(NewPermissionName(), "DELETED");
        var inactiveScope = await ScopeAsync("INACTIVE");
        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
        {
            var (_, jwt) = await CallerAsync(roles);
            await Status(await Send(jwt, Body(NewPermissionName())), 403, "E10009", ManageForbidden);
            // 404 for the permission comes first; 403 comes before the scope check.
            await Status(await Send(jwt, Body(NewPermissionName(), id: deleted)), 404, "E10004", "Permission not found.");
            await Status(await Send(jwt, Body(NewPermissionName(), id: id, scopes: [Scope(inactiveScope)])), 403, "E10009", ManageForbidden);
        }
    }

    [Fact]
    public async Task TC_PERM_006_scope_masters_and_q23()
    {
        var (_, jwt) = await AdminAsync();
        var name = NewPermissionName();
        foreach (var status in new[] { "INACTIVE", "DELETED" })
            await Status(await Send(jwt, Body(name, scopes: [Scope(await ScopeAsync(status))])), 404, "E10005", "Scope not found.");
        await Status(await Send(jwt, Body(name, scopes: [Scope(Guid.NewGuid())])), 404, "E10005", "Scope not found.");
        Assert.Equal(0L, await PermissionNameCountAsync(name));

        // Q23: an existing link to a scope that has since closed can still be removed.
        var id = await PermissionAsync(NewPermissionName());
        var closing = await ScopeAsync();
        var keep = await PermissionScopeAsync(id, await ScopeAsync());
        var link = await PermissionScopeAsync(id, closing);
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE author.m_scope SET row_status = 'LOCKED' WHERE id = @s", ("s", closing));
        var permissionName = await ScalarAsync<string>("SELECT name FROM author.m_permission WHERE id = @id", id);

        await Status(await Send(jwt, Body(permissionName, id: id, scopes: [Scope(closing, link)])), 404, "E10005", "Scope not found.");
        await Status(await Send(jwt, Body(permissionName, id: id, scopes: [Scope(closing, link, "DELETED")])), 200, "S10000", "Update Permission Success");
        Assert.Equal(("DELETED", "ACTIVE"), (await LinkStatusAsync(link), await LinkStatusAsync(keep)));
    }

    [Fact]
    public async Task TC_PERM_007_link_ids_belong_to_the_permission_and_keep_their_scope()
    {
        var (_, jwt) = await AdminAsync();
        var (a, b) = (await PermissionAsync(NewPermissionName()), await PermissionAsync(NewPermissionName()));
        var (s1, s2) = (await ScopeAsync(), await ScopeAsync());
        var linkOfB = await PermissionScopeAsync(b, s1);
        var linkOfA = await PermissionScopeAsync(a, s1);
        var nameA = await ScalarAsync<string>("SELECT name FROM author.m_permission WHERE id = @id", a);

        await Status(await Send(jwt, Body(nameA, id: a, scopes: [Scope(s1, linkOfB)])), 404, "E10004", "Permission scope not found.");
        await Status(await Send(jwt, Body(nameA, id: a, scopes: [Scope(s1, Guid.NewGuid())])), 404, "E10004", "Permission scope not found.");
        await Status(await Send(jwt, Body(NewPermissionName(), scopes: [Scope(s1, linkOfA)])), 404, "E10004", "Permission scope not found.");
        await Status(await Send(jwt, Body(nameA, id: a, scopes: [Scope(s2, linkOfA)])), 400, "E10001", "Scope Id cannot be changed.");

        var deletedLink = await PermissionScopeAsync(a, s2, "DELETED");
        await Status(await Send(jwt, Body(nameA, id: a, scopes: [Scope(s2, deletedLink, "DELETED")])), 404, "E10004", "Permission scope not found.");
    }

    [Fact]
    public async Task TC_PERM_008_duplicate_name()
    {
        var (_, jwt) = await AdminAsync();
        var taken = NewPermissionName();
        await PermissionAsync(taken, "ARCHIVED");
        await Status(await Send(jwt, Body(taken)), 409, "E10003", "Permission name already exists.");

        var other = await PermissionAsync(NewPermissionName());
        await Status(await Send(jwt, Body(taken, id: other)), 409, "E10003", "Permission name already exists.");
        var own = await ScalarAsync<string>("SELECT name FROM author.m_permission WHERE id = @id", other);
        await Status(await Send(jwt, Body(own, "Own name", other)), 200, "S10000", "Update Permission Success");

        var deletedName = NewPermissionName();
        await PermissionAsync(deletedName, "DELETED");
        await Status(await Send(jwt, Body(deletedName)), 201, "S10000", "Insert Permission Success");
    }

    [Fact]
    public async Task TC_PERM_009_grants_block_delete_and_archive()
    {
        var (userId, jwt) = await AdminAsync();
        var byRole = await PermissionAsync(NewPermissionName());
        await RoleGrantAsync(byRole, rowStatus: "LOCKED");
        var byUser = await PermissionAsync(NewPermissionName());
        await UserGrantAsync(userId, byUser);

        foreach (var id in new[] { byRole, byUser })
        {
            var name = await ScalarAsync<string>("SELECT name FROM author.m_permission WHERE id = @id", id);
            foreach (var status in new[] { "ARCHIVED", "DELETED" })
                await Status(await Send(jwt, Body(name, id: id, rowStatus: status)), 409, "E10006", "Permission is used by roles or users.");
            await Status(await Send(jwt, Body(name, id: id, rowStatus: "INACTIVE")), 200, "S10000", "Update Permission Success");
        }
    }

    [Fact]
    public async Task TC_PERM_010_duplicate_link()
    {
        var (_, jwt) = await AdminAsync();
        var id = await PermissionAsync(NewPermissionName());
        var scope = await ScopeAsync();
        var link = await PermissionScopeAsync(id, scope);
        var name = await ScalarAsync<string>("SELECT name FROM author.m_permission WHERE id = @id", id);

        await Status(await Send(jwt, Body(name, id: id, scopes: [Scope(scope)])), 409, "E10003", "Scope already exists in the permission.");
        // Removing and adding the same scope in one request: the old link still exists at the precheck.
        await Status(await Send(jwt, Body(name, id: id, scopes: [Scope(scope, link, "DELETED"), Scope(scope)])), 409, "E10003", "Scope already exists in the permission.");
        Assert.Equal("ACTIVE", await LinkStatusAsync(link));
        // The link itself may be sent again unchanged.
        await Status(await Send(jwt, Body(name, id: id, scopes: [Scope(scope, link)])), 200, "S10000", "Update Permission Success");
    }

    [Fact]
    public async Task TC_PERM_011_a_used_link_cannot_be_removed()
    {
        var (userId, jwt) = await AdminAsync();
        var id = await PermissionAsync(NewPermissionName());
        var (roleLink, userLink, spare) = (await PermissionScopeAsync(id, await ScopeAsync()), await PermissionScopeAsync(id, await ScopeAsync()), await PermissionScopeAsync(id, await ScopeAsync()));
        await RoleGrantAsync(id, roleLink);
        await UserGrantAsync(userId, id, userLink);
        var name = await ScalarAsync<string>("SELECT name FROM author.m_permission WHERE id = @id", id);
        var scopeOf = async (Guid link) => await ScalarAsync<Guid>("SELECT scope_id FROM author.t_permission_scope WHERE id = @id", link);

        foreach (var link in new[] { roleLink, userLink })
            await Status(await Send(jwt, Body(name, id: id, scopes: [Scope(await scopeOf(link), link, "DELETED")])), 409, "E10006", "Scope is used by roles or users.");
        await Status(await Send(jwt, Body(name, id: id, scopes: [Scope(await scopeOf(spare), spare, "DELETED")])), 200, "S10000", "Update Permission Success");
        Assert.Equal(("ACTIVE", "ACTIVE", "DELETED"), (await LinkStatusAsync(roleLink), await LinkStatusAsync(userLink), await LinkStatusAsync(spare)));
    }

    [Fact]
    public async Task TC_PERM_012_q24_delete_needs_zero_links()
    {
        var (_, jwt) = await AdminAsync();
        var id = await PermissionAsync(NewPermissionName());
        var scope = await ScopeAsync();
        var link = await PermissionScopeAsync(id, scope);
        var name = await ScalarAsync<string>("SELECT name FROM author.m_permission WHERE id = @id", id);

        await Status(await Send(jwt, Body(name, id: id, rowStatus: "DELETED")), 409, "E10006", "Permission cannot be deleted while scope definitions remain.");
        Assert.Equal("ACTIVE", await StatusOfAsync(id));
        // ARCHIVED may keep its links.
        await Status(await Send(jwt, Body(name, id: id, rowStatus: "ARCHIVED")), 200, "S10000", "Archive Permission Success");
        // Removing every link and deleting in one request works.
        await Status(await Send(jwt, Body(name, id: id, rowStatus: "DELETED", scopes: [Scope(scope, link, "DELETED")])), 204, null, null);
        Assert.Equal(("DELETED", "DELETED"), (await StatusOfAsync(id), await LinkStatusAsync(link)));
    }

    [Fact]
    public async Task TC_PERM_013_q22_a_granted_permission_keeps_a_link()
    {
        var (_, jwt) = await AdminAsync();
        var id = await PermissionAsync(NewPermissionName());
        var scope = await ScopeAsync();
        var link = await PermissionScopeAsync(id, scope);
        await RoleGrantAsync(id);
        var name = await ScalarAsync<string>("SELECT name FROM author.m_permission WHERE id = @id", id);

        await Status(await Send(jwt, Body(name, "Changed", id, scopes: [Scope(scope, link, "DELETED")])), 409, "E10006", "Permission cannot become unscoped while assigned to roles or users.");
        Assert.Equal(("ACTIVE", "seeded"), (await LinkStatusAsync(link), await ScalarAsync<string>("SELECT description FROM author.m_permission WHERE id = @id", id)));

        // Replacing the last link with another keeps it scoped.
        var other = await ScopeAsync();
        await Status(await Send(jwt, Body(name, id: id, scopes: [Scope(scope, link, "DELETED"), Scope(other)])), 200, "S10000", "Update Permission Success");

        // A permission already without links may keep its grants.
        var unscoped = await PermissionAsync(NewPermissionName());
        await RoleGrantAsync(unscoped);
        var unscopedName = await ScalarAsync<string>("SELECT name FROM author.m_permission WHERE id = @id", unscoped);
        await Status(await Send(jwt, Body(unscopedName, "Still fine", unscoped)), 200, "S10000", "Update Permission Success");
    }

    [Fact]
    public async Task TC_PERM_014_concurrent_inserts_of_one_name()
    {
        var jwts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AdminAsync()));
        var name = NewPermissionName();
        var responses = await Task.WhenAll(jwts.Select(a => Send(a.Jwt, Body(name))));

        Assert.Equal(1, responses.Count(r => (int)r.StatusCode == 201));
        foreach (var conflict in responses.Where(r => (int)r.StatusCode != 201))
            await Status(conflict, 409, "E10003", "Permission name already exists.");
        Assert.Equal(1L, await PermissionNameCountAsync(name));
    }

    [Fact]
    public async Task TC_PERM_015_concurrent_links_of_one_scope()
    {
        var jwts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AdminAsync()));
        var id = await PermissionAsync(NewPermissionName());
        var scope = await ScopeAsync();
        var name = await ScalarAsync<string>("SELECT name FROM author.m_permission WHERE id = @id", id);
        var responses = await Task.WhenAll(jwts.Select(a => Send(a.Jwt, Body(name, id: id, scopes: [Scope(scope)]))));

        // The permission row lock queues them: one adds the link, the others meet it at the precheck.
        Assert.Equal(1, responses.Count(r => (int)r.StatusCode == 200));
        foreach (var conflict in responses.Where(r => (int)r.StatusCode != 200))
            await Status(conflict, 409, "E10003", "Scope already exists in the permission.");
        Assert.Equal(1L, await LinkCountAsync(id));
    }

    [Fact]
    public async Task TC_PERM_016_locks_time_out()
    {
        var (adminId, jwt) = await AdminAsync();
        var id = await PermissionAsync(NewPermissionName());
        var scope = await ScopeAsync();
        var name = await ScalarAsync<string>("SELECT name FROM author.m_permission WHERE id = @id", id);
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        foreach (var (sql, target) in new[]
                 {
                     ("SELECT 1 FROM authen.m_user WHERE id = @id FOR UPDATE", adminId),
                     ("SELECT 1 FROM author.m_permission WHERE id = @id FOR UPDATE", id),
                     ("SELECT 1 FROM author.m_scope WHERE id = @id FOR UPDATE", scope)
                 })
        {
            var (connection, transaction) = await HoldAsync(sql, target);
            await Status(await Send(jwt, Body(name, "Changed", id, scopes: [Scope(scope)]), client), 408, "E90001", "The server timed out waiting for the request.");
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }

        Assert.Equal(0L, await LinkCountAsync(id));

        // FOR SHARE conflicts only with this API's FOR UPDATE, not with the later UPDATE or foreign-key checks, and each
        // request stops before any write: without the lock it would answer at once (403, 409) instead of waiting.
        var (_, customer) = await CallerAsync(new[] { "CUSTOMER" });
        var (shared, sharedTransaction) = await HoldAsync("SELECT 1 FROM author.m_permission WHERE id = @id FOR SHARE", id);
        await Status(await Send(customer, Body(name, id: id), client), 408, "E90001", null);
        await sharedTransaction.RollbackAsync();
        await shared.DisposeAsync();
        await Status(await Send(customer, Body(name, id: id), client), 403, "E10009", ManageForbidden);

        var taken = NewPermissionName();
        await PermissionAsync(taken);
        (shared, sharedTransaction) = await HoldAsync("SELECT 1 FROM author.m_scope WHERE id = @id FOR SHARE", scope);
        await Status(await Send(jwt, Body(taken, scopes: [Scope(scope)]), client), 408, "E90001", null);
        await sharedTransaction.RollbackAsync();
        await shared.DisposeAsync();
        await Status(await Send(jwt, Body(taken, scopes: [Scope(scope)]), client), 409, "E10003", "Permission name already exists.");
    }

    private sealed class FailingLinkInsert(IPermissionRepository inner) : IPermissionRepository
    {
        public Task<bool> LockAsync(Guid id, CancellationToken ct) => inner.LockAsync(id, ct);
        public Task<IReadOnlySet<Guid>> LockActiveScopesAsync(IReadOnlyCollection<Guid> scopeIds, CancellationToken ct) => inner.LockActiveScopesAsync(scopeIds, ct);
        public Task<IReadOnlySet<Guid>> LockManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => inner.LockManyAsync(ids, ct);
        public Task<IReadOnlyDictionary<Guid, LinkRow>> LockLinksAsync(IReadOnlyCollection<Guid> permissionIds, IReadOnlyCollection<Guid> linkIds, CancellationToken ct) =>
            inner.LockLinksAsync(permissionIds, linkIds, ct);
        public Task<long> CountLinksAsync(Guid permissionId, CancellationToken ct) => inner.CountLinksAsync(permissionId, ct);
        public Task<bool> NameTakenAsync(string name, Guid? excludeId, CancellationToken ct) => inner.NameTakenAsync(name, excludeId, ct);
        public Task<bool> GrantedAsync(Guid permissionId, CancellationToken ct) => inner.GrantedAsync(permissionId, ct);
        public Task<bool> LinkTakenAsync(Guid permissionId, Guid scopeId, Guid? excludeLinkId, CancellationToken ct) => inner.LinkTakenAsync(permissionId, scopeId, excludeLinkId, ct);
        public Task<bool> LinkInUseAsync(Guid linkId, CancellationToken ct) => inner.LinkInUseAsync(linkId, ct);
        public Task<Guid> InsertAsync(PermissionValues values, string by, DateTime now, CancellationToken ct) => inner.InsertAsync(values, by, now, ct);
        public Task<bool> UpdateAsync(Guid id, PermissionValues values, string by, DateTime now, CancellationToken ct) => inner.UpdateAsync(id, values, by, now, ct);
        public Task<bool> UpdateLinkAsync(Guid linkId, Guid permissionId, Guid scopeId, string rowStatus, string by, DateTime now, CancellationToken ct) =>
            inner.UpdateLinkAsync(linkId, permissionId, scopeId, rowStatus, by, now, ct);

        public async Task<Guid> InsertLinkAsync(Guid permissionId, Guid scopeId, string rowStatus, string by, DateTime now, CancellationToken ct)
        {
            await inner.InsertLinkAsync(permissionId, scopeId, rowStatus, by, now, ct);
            throw new InvalidOperationException("injected after the link insert");
        }
    }

    [Fact]
    public async Task TC_PERM_017_an_unexpected_failure_rolls_back_the_parent_and_links()
    {
        var (_, jwt) = await AdminAsync();
        var name = NewPermissionName();
        var client = Factory.WithWebHostBuilder(w => w.ConfigureTestServices(s =>
        {
            s.AddScoped<PermissionRepository>();
            s.AddScoped<IPermissionRepository>(p => new FailingLinkInsert(p.GetRequiredService<PermissionRepository>()));
        })).CreateClient();

        await Status(await Send(jwt, Body(name, scopes: [Scope(await ScopeAsync())]), client), 500, null, "An unexpected error occurred.");
        Assert.Equal(0L, await PermissionNameCountAsync(name));
    }
}
