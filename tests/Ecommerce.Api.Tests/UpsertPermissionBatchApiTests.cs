using System.Text.Json;
using System.Text.Json.Nodes;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Author;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static Ecommerce.Api.Tests.UpsertPermissionApiTests;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/author/05-upsert-permission-batch/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class UpsertPermissionBatchApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/permission/upsert/batch";
    private const string Success = "Batch Upsert Permission Success";

    private static string Batch(params JsonNode[] items) => new JsonObject { ["permissions"] = new JsonArray(items) }.ToJsonString();

    private Task<HttpResponseMessage> Send(string? jwt, string body, HttpClient? client = null) => Post(Path, jwt, body, client);

    /// <summary>data.permissions as (index, permissionId, action), checking that data has only permissions and each result only those keys.</summary>
    private static List<(int Index, Guid PermissionId, string Action)> Results(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["permissions"], data.EnumerateObject().Select(p => p.Name));
        return data.GetProperty("permissions").EnumerateArray().Select(r =>
        {
            Assert.Equal(["index", "permissionId", "action"], r.EnumerateObject().Select(p => p.Name));
            return (r.GetProperty("index").GetInt32(), r.GetProperty("permissionId").GetGuid(), r.GetProperty("action").GetString()!);
        }).ToList();
    }

    private Task<string> StatusOfAsync(Guid id) => ScalarAsync<string>("SELECT row_status FROM author.m_permission WHERE id = @id", id);

    private Task<string> NameOfAsync(Guid id) => ScalarAsync<string>("SELECT name FROM author.m_permission WHERE id = @id", id);

    private Task<long> LinkCountAsync(Guid id) =>
        ScalarAsync<long>("SELECT COUNT(*) FROM author.t_permission_scope WHERE permission_id = @id AND row_status <> 'DELETED'", id);

    [Fact]
    public async Task TC_PBATCH_001_insert_update_delete_and_archive_in_one_batch()
    {
        var (adminId, jwt) = await AdminAsync();
        var scope = await ScopeAsync();
        var (updated, deleted, archived) = (await PermissionAsync(NewPermissionName()), await PermissionAsync(NewPermissionName()), await PermissionAsync(NewPermissionName()));
        var (inserted, renamed) = (NewPermissionName(), NewPermissionName());

        var results = Results(await Status(await Send(jwt, Batch(
            Body(inserted, "New", null, null, Scope(scope)),
            Body(renamed, "Renamed", updated, "INACTIVE"),
            Body(await NameOfAsync(deleted), "Gone", deleted, "DELETED"),
            Body(await NameOfAsync(archived), "Kept", archived, "ARCHIVED"))), 200, "S10000", Success));

        Assert.Equal([0, 1, 2, 3], results.Select(r => r.Index));
        Assert.Equal(["INSERT", "UPDATE", "DELETE", "ARCHIVE"], results.Select(r => r.Action));
        Assert.Equal([updated, deleted, archived], results.Skip(1).Select(r => r.PermissionId));
        Assert.Equal((inserted, "ACTIVE", 1L), (await NameOfAsync(results[0].PermissionId), await StatusOfAsync(results[0].PermissionId), await LinkCountAsync(results[0].PermissionId)));
        Assert.Equal(adminId.ToString(), await ScalarAsync<string>("SELECT created_by FROM author.m_permission WHERE id = @id", results[0].PermissionId));
        Assert.Equal((renamed, "INACTIVE"), (await NameOfAsync(updated), await StatusOfAsync(updated)));
        Assert.Equal(("DELETED", "ARCHIVED"), (await StatusOfAsync(deleted), await StatusOfAsync(archived)));
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

    public static TheoryData<string, string> RequestShapes => new()
    {
        { "[]", "Request Body must be an object." },
        { """{"permissions":[{"name":"a.b","description":"d"}],"extra":1}""", "Extra is not a known field." },
        { """{"permissions":[],"permissions":[]}""", "Permissions must not be duplicated." },
        { "{}", "Permissions is a required field." },
        { """{"permissions":null}""", "Permissions is a required field." },
        { """{"permissions":"a"}""", "Permissions must be an array." },
        { """{"permissions":[]}""", "Permissions must contain 1 to 100 items." }
    };

    [Theory]
    [MemberData(nameof(RequestShapes))]
    public async Task TC_PBATCH_002_request_level_validation(string body, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Send(jwt, body), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_PBATCH_002_item_count_and_query_keys()
    {
        var (_, jwt) = await AdminAsync();
        var names = Enumerable.Range(0, 101).Select(_ => NewPermissionName()).ToList();
        await Status(await Send(jwt, Batch(names.Select(n => (JsonNode)Body(n)).ToArray())), 400, "E10001", "Permissions must contain 1 to 100 items.");
        Assert.Equal(0L, await PermissionNameCountAsync(names[0]));

        var hundred = Results(await Status(await Send(jwt, Batch(names.Take(100).Select(n => (JsonNode)Body(n)).ToArray())), 200, "S10000", Success));
        Assert.Equal(Enumerable.Range(0, 100), hundred.Select(r => r.Index));

        await Status(await Post(Path + "?dryRun=1", jwt, Batch(Body(NewPermissionName()))), 400, "E10001", "Dry Run is not a known field.");
    }

    public static TheoryData<string, string> ItemShapes => new()
    {
        { """{"permissions":[null]}""", "permissions[0]: Permission must be an object." },
        { """{"permissions":[{"name":"a.b","description":"d","code":1}]}""", "permissions[0]: Code is not a known field." },
        { """{"permissions":[{"name":"a.b","description":"d"},{"name":"a.c","description":"d","scopes":null}]}""", "permissions[1]: Scopes must be an array." },
        { """{"permissions":[{"name":"a.b","description":"d"},{"name":"a.c","description":"d","scopes":[{"scopeId":"x","extra":1}]}]}""", "permissions[1]: scopes[0]: Extra is not a known field." },
        { """{"permissions":[{"name":"a.b","description":"d"},{"name":"a.c","description":"d","scopes":[{"scopeId":"x"}]}]}""", "permissions[1]: scopes[0]: Scope Id must be a valid UUID." },
        { """{"permissions":[{"name":"x","description":""}]}""", "permissions[0]: Description is a required field, Permission name must be lower-case letters, digits, _ and ., with at least two parts separated by a dot, at most 64 characters." },
        { """{"permissions":[{"name":"a.b","description":"d"},{"name":"a.c","description":"d","rowStatus":"DELETED"}]}""", "permissions[1]: Row status must not be DELETED when inserting a permission." },
        { """{"permissions":[{"name":"a.b","description":"d","scopes":[{"scopeId":"6f1c0e1e-0000-4000-8000-000000000001","rowStatus":"DELETED"}]}]}""", "permissions[0]: scopes[0]: Row status must not be DELETED when inserting a scope." },
        { """{"permissions":[{"name":"","description":"d"},{"name":"a.c"}]}""", "permissions[0]: Name is a required field, permissions[1]: Description is a required field." }
    };

    [Theory]
    [MemberData(nameof(ItemShapes))]
    public async Task TC_PBATCH_003_item_validation(string body, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Send(jwt, body), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_PBATCH_003_duplicated_ids_and_names_in_the_request()
    {
        var (_, jwt) = await AdminAsync();
        var (a, b) = (await PermissionAsync(NewPermissionName()), await PermissionAsync(NewPermissionName()));
        var (nameA, nameB, same) = (await NameOfAsync(a), await NameOfAsync(b), NewPermissionName());

        var upper = Body(nameB, id: a);
        upper["id"] = a.ToString().ToUpperInvariant();
        await Status(await Send(jwt, Batch(Body(NewPermissionName()), Body(nameA, id: a), upper)), 400, "E10001", "permissions[2]: Id is duplicated with permissions[1].");
        await Status(await Send(jwt, Batch(Body(same), Body(nameB, id: b), Body(same))), 400, "E10001", "permissions[2]: Name is duplicated with permissions[0].");

        // A DELETED item does not hold its name: the permission is deleted and its name is used again by the insert after it.
        var results = Results(await Status(await Send(jwt, Batch(Body(nameA, id: a, rowStatus: "DELETED"), Body(nameA))), 200, "S10000", Success));
        Assert.Equal(["DELETE", "INSERT"], results.Select(r => r.Action));
        Assert.Equal(nameA, await NameOfAsync(results[1].PermissionId));
    }

    [Fact]
    public async Task TC_PBATCH_004_validation_before_the_credential_and_credential_failures()
    {
        await Status(await Send(null, Batch(Body("bad"))), 400, "E10001",
            "permissions[0]: Permission name must be lower-case letters, digits, _ and ., with at least two parts separated by a dot, at most 64 characters.");

        var name = NewPermissionName();
        await Status(await Send(null, Batch(Body(name))), 401, "E10002", Invalid);
        await Status(await Send("not-a-jwt", Batch(Body(name))), 401, "E10002", Invalid);
        await Status(await Send((await CallerAsync(null)).Jwt, Batch(Body(name))), 401, "E10002", Invalid);
        var (userId, jwt) = await AdminAsync();
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.t_user_access_token SET row_status = 'INACTIVE' WHERE user_id = @u", ("u", userId));
        await Status(await Send(jwt, Batch(Body(name))), 401, "E10002", Invalid);
        Assert.Equal(0L, await PermissionNameCountAsync(name));
    }

    [Fact]
    public async Task TC_PBATCH_005_first_missing_target_in_request_order()
    {
        var (_, jwt) = await AdminAsync();
        var deleted = await PermissionAsync(NewPermissionName(), "DELETED");
        var kept = await PermissionAsync(NewPermissionName());
        var insert = NewPermissionName();

        // Index 1 is the first missing id in request order, although index 3 sorts first.
        var low = Guid.Parse("00000000-0000-4000-8000-000000000001");
        await Status(await Send(jwt, Batch(Body(insert), Body(NewPermissionName(), id: Guid.NewGuid()), Body(await NameOfAsync(kept), "Changed", kept), Body(NewPermissionName(), id: low))),
            404, "E10004", "permissions[1]: Permission not found.");
        await Status(await Send(jwt, Batch(Body(insert), Body(NewPermissionName(), id: deleted))), 404, "E10004", "permissions[1]: Permission not found.");

        // 404 comes before 403.
        var (_, customer) = await CallerAsync(new[] { "CUSTOMER" });
        await Status(await Send(customer, Batch(Body(NewPermissionName(), id: deleted))), 404, "E10004", "permissions[0]: Permission not found.");

        Assert.Equal(0L, await PermissionNameCountAsync(insert));
        Assert.Equal("seeded", await ScalarAsync<string>("SELECT description FROM author.m_permission WHERE id = @id", kept));
    }

    [Fact]
    public async Task TC_PBATCH_006_only_a_platform_administrator_once()
    {
        var target = await PermissionAsync(NewPermissionName());
        var inactiveScope = await ScopeAsync("INACTIVE");
        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
        {
            var (_, jwt) = await CallerAsync(roles);
            // One 403 with no position, before the scope 404 of index 1.
            await Status(await Send(jwt, Batch(Body(NewPermissionName()), Body(await NameOfAsync(target), id: target, scopes: [Scope(inactiveScope)]))), 403, "E10009", ManageForbidden);
        }
    }

    [Fact]
    public async Task TC_PBATCH_007_every_item_error_names_its_position_and_rolls_back_the_batch()
    {
        var (userId, jwt) = await AdminAsync();
        var first = NewPermissionName();

        // Each case: a valid insert at index 0, the failing item at index 1; nothing of index 0 is kept.
        var withLink = await PermissionAsync(NewPermissionName());
        var scope = await ScopeAsync();
        var link = await PermissionScopeAsync(withLink, scope);
        var other = await PermissionAsync(NewPermissionName());
        var otherLink = await PermissionScopeAsync(other, await ScopeAsync());
        var granted = await PermissionAsync(NewPermissionName());
        await ExecuteAsync(Postgres.ConnectionString, "INSERT INTO author.t_user_permission (created_by, user_id, permission_id) VALUES ('seed', @u, @p)", ("u", userId), ("p", granted));
        var nameWith = await NameOfAsync(withLink);

        var cases = new (JsonNode Item, int Code, string BizCode, string Message)[]
        {
            (Body(NewPermissionName(), scopes: [Scope(await ScopeAsync("INACTIVE"))]), 404, "E10005", "permissions[1]: Scope not found."),
            (Body(nameWith, id: withLink, scopes: [Scope(scope, otherLink)]), 404, "E10004", "permissions[1]: Permission scope not found."),
            (Body(nameWith, id: withLink, scopes: [Scope(await ScopeAsync(), link)]), 400, "E10001", "permissions[1]: Scope Id cannot be changed."),
            (Body(nameWith), 409, "E10003", "permissions[1]: Permission name already exists."),
            (Body(nameWith, id: withLink, scopes: [Scope(scope)]), 409, "E10003", "permissions[1]: Scope already exists in the permission."),
            (Body(await NameOfAsync(granted), id: granted, rowStatus: "ARCHIVED"), 409, "E10006", "permissions[1]: Permission is used by roles or users."),
            (Body(nameWith, id: withLink, rowStatus: "DELETED"), 409, "E10006", "permissions[1]: Permission cannot be deleted while scope definitions remain.")
        };

        foreach (var (item, code, bizCode, message) in cases)
            await Status(await Send(jwt, Batch(Body(first), item)), code, bizCode, message);

        // A link of another permission of the same batch is locked too, but it is not this item's.
        var otherScope = await ScalarAsync<Guid>("SELECT scope_id FROM author.t_permission_scope WHERE id = @id", otherLink);
        await Status(await Send(jwt, Batch(Body(first), Body(await NameOfAsync(other), id: other), Body(nameWith, id: withLink, scopes: [Scope(otherScope, otherLink)]))),
            404, "E10004", "permissions[2]: Permission scope not found.");

        Assert.Equal(0L, await PermissionNameCountAsync(first));
        Assert.Equal(("ACTIVE", 1L), (await StatusOfAsync(withLink), await LinkCountAsync(withLink)));
    }

    [Fact]
    public async Task TC_PBATCH_008_link_errors_at_a_later_item_q22_and_link_in_use()
    {
        var (userId, jwt) = await AdminAsync();
        var first = NewPermissionName();
        var permission = await PermissionAsync(NewPermissionName());
        var (scope, usedScope) = (await ScopeAsync(), await ScopeAsync());
        var link = await PermissionScopeAsync(permission, scope);
        var used = await PermissionScopeAsync(permission, usedScope);
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.t_user_permission (created_by, user_id, permission_id, permission_scope_id) VALUES ('seed', @u, @p, @l)", ("u", userId), ("p", permission), ("l", used));
        var name = await NameOfAsync(permission);

        await Status(await Send(jwt, Batch(Body(first), Body(name, id: permission, scopes: [Scope(usedScope, used, "DELETED")]))), 409, "E10006", "permissions[1]: Scope is used by roles or users.");

        // Q22: the user grant above is on this permission; removing both links would leave it unscoped.
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE author.t_user_permission SET permission_scope_id = NULL WHERE permission_scope_id = @l", ("l", used));
        await Status(await Send(jwt, Batch(Body(first), Body(name, id: permission, scopes: [Scope(scope, link, "DELETED"), Scope(usedScope, used, "DELETED")]))),
            409, "E10006", "permissions[1]: Permission cannot become unscoped while assigned to roles or users.");

        Assert.Equal(0L, await PermissionNameCountAsync(first));
        Assert.Equal(2L, await LinkCountAsync(permission));
    }

    [Fact]
    public async Task TC_PBATCH_009_items_see_the_writes_before_them()
    {
        var (_, jwt) = await AdminAsync();
        var (a, b) = (await PermissionAsync(NewPermissionName()), await PermissionAsync(NewPermissionName(), "LOCKED"));
        var (nameA, nameB) = (await NameOfAsync(a), await NameOfAsync(b));

        // The insert at index 0 meets the name still held by b; the delete at index 1 comes too late.
        await Status(await Send(jwt, Batch(Body(nameB), Body(nameB, id: b, rowStatus: "DELETED"))), 409, "E10003", "permissions[0]: Permission name already exists.");
        // Renaming a to b's name fails even though index 1 would rename b: no final-state plan.
        await Status(await Send(jwt, Batch(Body(nameB, id: a), Body(NewPermissionName(), id: b))), 409, "E10003", "permissions[0]: Permission name already exists.");
        Assert.Equal((nameA, "LOCKED"), (await NameOfAsync(a), await StatusOfAsync(b)));

        // In the other order the delete frees the name for the insert after it.
        var results = Results(await Status(await Send(jwt, Batch(Body(nameB, id: b, rowStatus: "DELETED"), Body(nameB))), 200, "S10000", Success));
        Assert.Equal(["DELETE", "INSERT"], results.Select(r => r.Action));
    }

    [Fact]
    public async Task TC_PBATCH_010_concurrent_batches_with_one_name()
    {
        var jwts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AdminAsync()));
        var name = NewPermissionName();
        var responses = await Task.WhenAll(jwts.Select(a => Send(a.Jwt, Batch(Body(NewPermissionName()), Body(name)))));

        Assert.Equal(1, responses.Count(r => (int)r.StatusCode == 200));
        foreach (var conflict in responses.Where(r => (int)r.StatusCode != 200))
            await Status(conflict, 409, "E10003", "permissions[1]: Permission name already exists.");
        Assert.Equal(1L, await PermissionNameCountAsync(name));
    }

    [Fact]
    public async Task TC_PBATCH_011_locks_and_their_order()
    {
        var (adminId, jwt) = await AdminAsync();
        var (a, b) = (await PermissionAsync(NewPermissionName()), await PermissionAsync(NewPermissionName()));
        var scope = await ScopeAsync();
        var (nameA, nameB) = (await NameOfAsync(a), await NameOfAsync(b));
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        var (connection, transaction) = await HoldAsync("SELECT 1 FROM authen.m_user WHERE id = @id FOR UPDATE", adminId);
        await Status(await Send(jwt, Batch(Body(nameA, id: a)), client), 408, "E90001", "The server timed out waiting for the request.");
        await transaction.RollbackAsync();
        await connection.DisposeAsync();

        // FOR SHARE conflicts only with this API's FOR UPDATE; each request stops before any write when not blocked.
        var (_, customer) = await CallerAsync(new[] { "CUSTOMER" });
        (connection, transaction) = await HoldAsync("SELECT 1 FROM author.m_permission WHERE id = @id FOR SHARE", b);
        await Status(await Send(customer, Batch(Body(nameA, id: a), Body(nameB, id: b)), client), 408, "E90001", null);
        await transaction.RollbackAsync();
        await connection.DisposeAsync();
        await Status(await Send(customer, Batch(Body(nameA, id: a), Body(nameB, id: b)), client), 403, "E10009", ManageForbidden);

        (connection, transaction) = await HoldAsync("SELECT 1 FROM author.m_scope WHERE id = @id FOR SHARE", scope);
        await Status(await Send(jwt, Batch(Body(nameA), Body(NewPermissionName(), scopes: [Scope(scope)])), client), 408, "E90001", null);
        await transaction.RollbackAsync();
        await connection.DisposeAsync();
        await Status(await Send(jwt, Batch(Body(nameA), Body(NewPermissionName(), scopes: [Scope(scope)])), client), 409, "E10003", "permissions[0]: Permission name already exists.");

        // Targets are locked in id order whatever the request order: crossed batches finish without a deadlock.
        var (_, other) = await AdminAsync();
        for (var round = 0; round < 5; round++)
        {
            var responses = await Task.WhenAll(
                Send(jwt, Batch(Body(nameA, "ab", a), Body(nameB, "ab", b))),
                Send(other, Batch(Body(nameB, "ba", b), Body(nameA, "ba", a))));
            foreach (var response in responses) await Status(response, 200, "S10000", Success);
        }
    }

    private sealed class FailingSecondInsert(IPermissionRepository inner) : IPermissionRepository
    {
        private int _inserts;
        public Task<bool> LockAsync(Guid id, CancellationToken ct) => inner.LockAsync(id, ct);
        public Task<IReadOnlySet<Guid>> LockManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => inner.LockManyAsync(ids, ct);
        public Task<IReadOnlySet<Guid>> LockActiveScopesAsync(IReadOnlyCollection<Guid> scopeIds, CancellationToken ct) => inner.LockActiveScopesAsync(scopeIds, ct);
        public Task<IReadOnlyDictionary<Guid, LinkRow>> LockLinksAsync(IReadOnlyCollection<Guid> permissionIds, IReadOnlyCollection<Guid> linkIds, CancellationToken ct) =>
            inner.LockLinksAsync(permissionIds, linkIds, ct);
        public Task<long> CountLinksAsync(Guid permissionId, CancellationToken ct) => inner.CountLinksAsync(permissionId, ct);
        public Task<bool> NameTakenAsync(string name, Guid? excludeId, CancellationToken ct) => inner.NameTakenAsync(name, excludeId, ct);
        public Task<bool> GrantedAsync(Guid permissionId, CancellationToken ct) => inner.GrantedAsync(permissionId, ct);
        public Task<bool> LinkTakenAsync(Guid permissionId, Guid scopeId, Guid? excludeLinkId, CancellationToken ct) => inner.LinkTakenAsync(permissionId, scopeId, excludeLinkId, ct);
        public Task<bool> LinkInUseAsync(Guid linkId, CancellationToken ct) => inner.LinkInUseAsync(linkId, ct);
        public Task<bool> UpdateAsync(Guid id, PermissionValues values, string by, DateTime now, CancellationToken ct) => inner.UpdateAsync(id, values, by, now, ct);
        public Task<Guid> InsertLinkAsync(Guid permissionId, Guid scopeId, string rowStatus, string by, DateTime now, CancellationToken ct) =>
            inner.InsertLinkAsync(permissionId, scopeId, rowStatus, by, now, ct);
        public Task<bool> UpdateLinkAsync(Guid linkId, Guid permissionId, Guid scopeId, string rowStatus, string by, DateTime now, CancellationToken ct) =>
            inner.UpdateLinkAsync(linkId, permissionId, scopeId, rowStatus, by, now, ct);

        public async Task<Guid> InsertAsync(PermissionValues values, string by, DateTime now, CancellationToken ct)
        {
            var id = await inner.InsertAsync(values, by, now, ct);
            return ++_inserts == 2 ? throw new InvalidOperationException("injected after the second insert") : id;
        }
    }

    [Fact]
    public async Task TC_PBATCH_012_an_unexpected_failure_rolls_back_every_item()
    {
        var (_, jwt) = await AdminAsync();
        var (first, second) = (NewPermissionName(), NewPermissionName());
        var client = Factory.WithWebHostBuilder(w => w.ConfigureTestServices(s =>
        {
            s.AddScoped<PermissionRepository>();
            s.AddScoped<IPermissionRepository>(p => new FailingSecondInsert(p.GetRequiredService<PermissionRepository>()));
        })).CreateClient();

        await Status(await Send(jwt, Batch(Body(first, scopes: [Scope(await ScopeAsync())]), Body(second)), client), 500, null, "An unexpected error occurred.");
        Assert.Equal((0L, 0L), (await PermissionNameCountAsync(first), await PermissionNameCountAsync(second)));
    }
}
