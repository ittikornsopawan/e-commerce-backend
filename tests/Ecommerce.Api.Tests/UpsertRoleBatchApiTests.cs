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

// e-commerce/doc/api/author/02-upsert-role-batch/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class UpsertRoleBatchApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/role/upsert/batch";
    private const string Success = "Batch Upsert Role Success";

    private static JsonObject Item(string name, string description = "A role", Guid? id = null, string? rowStatus = null)
    {
        var item = new JsonObject { ["name"] = name, ["description"] = description };
        if (id is not null) item["id"] = id.ToString();
        if (rowStatus is not null) item["rowStatus"] = rowStatus;
        return item;
    }

    private static string Batch(params JsonNode[] items) => new JsonObject { ["roles"] = new JsonArray(items) }.ToJsonString();

    private Task<HttpResponseMessage> Send(string? jwt, string body, HttpClient? client = null) => Post(Path, jwt, body, client);

    /// <summary>data.roles as (index, roleId, action), checking that data has only roles and each result only those keys.</summary>
    private static List<(int Index, Guid RoleId, string Action)> Results(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["roles"], data.EnumerateObject().Select(p => p.Name));
        return data.GetProperty("roles").EnumerateArray().Select(r =>
        {
            Assert.Equal(["index", "roleId", "action"], r.EnumerateObject().Select(p => p.Name));
            return (r.GetProperty("index").GetInt32(), r.GetProperty("roleId").GetGuid(), r.GetProperty("action").GetString()!);
        }).ToList();
    }

    [Fact]
    public async Task TC_RBATCH_001_insert_update_delete_and_archive_in_one_batch()
    {
        var (adminId, jwt) = await AdminAsync();
        var (inserted, updated, deleted, archived) = (NewName(), await RoleAsync(NewName()), await RoleAsync(NewName()), await RoleAsync(NewName()));
        var renamed = NewName();

        var root = await Status(await Send(jwt, Batch(
            Item(inserted, "New role"),
            Item(renamed, "Renamed", updated, "INACTIVE"),
            Item((await RowAsync(deleted)).Name, "Gone", deleted, "DELETED"),
            Item((await RowAsync(archived)).Name, "Kept", archived, "ARCHIVED"))), 200, "S10000", Success);

        var results = Results(root);
        Assert.Equal([0, 1, 2, 3], results.Select(r => r.Index));
        Assert.Equal(["INSERT", "UPDATE", "DELETE", "ARCHIVE"], results.Select(r => r.Action));
        Assert.Equal([updated, deleted, archived], results.Skip(1).Select(r => r.RoleId));
        Assert.Equal((inserted, "New role", "ACTIVE", adminId.ToString(), (string?)null), await RowAsync(results[0].RoleId));
        Assert.Equal((renamed, "Renamed", "INACTIVE", "seed", adminId.ToString()), await RowAsync(updated));
        Assert.Equal("DELETED", (await RowAsync(deleted)).RowStatus);
        Assert.Equal("ARCHIVED", (await RowAsync(archived)).RowStatus);
    }

    [Fact]
    public async Task TC_RBATCH_002_insert_archived_is_an_insert_and_status_changes_are_updates()
    {
        var (_, jwt) = await AdminAsync();
        var locked = await RoleAsync(NewName(), "INACTIVE");
        var name = (await RowAsync(locked)).Name;

        var results = Results(await Status(await Send(jwt, Batch(Item(NewName(), rowStatus: "ARCHIVED"), Item(name, id: locked, rowStatus: "LOCKED"))), 200, "S10000", Success));
        Assert.Equal(["INSERT", "UPDATE"], results.Select(r => r.Action));
        Assert.Equal("ARCHIVED", (await RowAsync(results[0].RoleId)).RowStatus);
        Assert.Equal("LOCKED", (await RowAsync(locked)).RowStatus);

        // Without rowStatus the role is ACTIVE again.
        results = Results(await Status(await Send(jwt, Batch(Item(name, id: locked))), 200, "S10000", Success));
        Assert.Equal(("UPDATE", "ACTIVE"), (results[0].Action, (await RowAsync(locked)).RowStatus));
    }

    public static TheoryData<string, string> RequestShapes => new()
    {
        { "[]", "Request Body must be an object." },
        { """{"roles":[{"name":"A","description":"B"}],"extra":1}""", "Extra is not a known field." },
        { """{"roles":[{"name":"A","description":"B"}],"roles":[]}""", "Roles must not be duplicated." },
        { "{}", "Roles is a required field." },
        { """{"roles":null}""", "Roles is a required field." },
        { """{"roles":"A"}""", "Roles must be an array." },
        { """{"roles":[]}""", "Roles must contain 1 to 100 items." }
    };

    [Theory]
    [MemberData(nameof(RequestShapes))]
    public async Task TC_RBATCH_003_request_level_validation(string body, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Send(jwt, body), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_RBATCH_003_item_count_and_query_keys()
    {
        var (_, jwt) = await AdminAsync();
        var prefix = NewName();
        await Status(await Send(jwt, Batch(Enumerable.Range(0, 101).Select(i => (JsonNode)Item($"{prefix}_{i}")).ToArray())), 400, "E10001", "Roles must contain 1 to 100 items.");
        Assert.Equal(0L, await NameCountAsync($"{prefix}_0"));

        var hundred = Results(await Status(await Send(jwt, Batch(Enumerable.Range(0, 100).Select(i => (JsonNode)Item($"{prefix}_{i}")).ToArray())), 200, "S10000", Success));
        Assert.Equal(Enumerable.Range(0, 100), hundred.Select(r => r.Index));

        await Status(await Post(Path + "?dryRun=true", jwt, Batch(Item(NewName()))), 400, "E10001", "Dry Run is not a known field.");
    }

    public static TheoryData<string, string> ItemShapes => new()
    {
        { """{"roles":[null]}""", "roles[0]: Role must be an object." },
        { """{"roles":[{"name":"A","description":"B","code":1}]}""", "roles[0]: Code is not a known field." },
        { """{"roles":[{"name":"A","description":"B","name":"C"}]}""", "roles[0]: Name must not be duplicated." },
        { """{"roles":[{"name":"A","description":"B"},{"name":1,"description":"B"}]}""", "roles[1]: Name must be a string." },
        { """{"roles":[{"name":"A","description":"B"},{"description":"B"}]}""", "roles[1]: Name is a required field." },
        { """{"roles":[{"name":" ","description":""}]}""", "roles[0]: Name and Description are required fields." },
        { """{"roles":[{"name":"A","description":"B"},{"name":"catalog","description":"B"}]}""", "roles[1]: Role name must be capital letters, digits and _, starting with a letter, at most 64 characters." },
        { """{"roles":[{"name":"A","description":"B","rowStatus":"DELETED"}]}""", "roles[0]: Row status must not be DELETED when inserting a role." },
        { """{"roles":[{"id":null,"name":"A","description":"B"}]}""", "roles[0]: Id must be a valid UUID." },
        { """{"roles":[{"name":"A","description":"B","rowStatus":"GONE"}]}""", "roles[0]: Row Status must be ACTIVE, INACTIVE, DELETED, LOCKED or ARCHIVED." },
        { """{"roles":[{"name":"a b","description":""},{"name":"A"}]}""", "roles[0]: Description is a required field, Role name must be capital letters, digits and _, starting with a letter, at most 64 characters, roles[1]: Description is a required field." }
    };

    [Theory]
    [MemberData(nameof(ItemShapes))]
    public async Task TC_RBATCH_004_item_validation(string body, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Send(jwt, body), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_RBATCH_004_description_length_and_delete_still_needs_fields()
    {
        var (_, jwt) = await AdminAsync();
        var id = await RoleAsync(NewName());
        await Status(await Send(jwt, Batch(Item(NewName(), new string('d', 257)))), 400, "E10001", "roles[0]: Description must be at most 256 characters.");
        await Status(await Send(jwt, Batch(new JsonObject { ["id"] = id.ToString(), ["rowStatus"] = "DELETED" })), 400, "E10001", "roles[0]: Name and Description are required fields.");
        Assert.Equal("ACTIVE", (await RowAsync(id)).RowStatus);

        var exact = NewName();
        Assert.Equal(exact, (await RowAsync(Results(await Status(await Send(jwt, Batch(Item(exact, new string('d', 256)))), 200, "S10000", Success))[0].RoleId)).Name);
    }

    [Fact]
    public async Task TC_RBATCH_005_duplicated_ids_and_names_in_the_request()
    {
        var (_, jwt) = await AdminAsync();
        var (a, b) = (await RoleAsync(NewName()), await RoleAsync(NewName()));
        var (nameA, nameB, same) = ((await RowAsync(a)).Name, (await RowAsync(b)).Name, NewName());

        // Ids are compared as UUIDs: upper case is the same id.
        await Status(await Send(jwt, Batch(Item(NewName()), Item(nameA, id: a), new JsonObject { ["id"] = a.ToString().ToUpperInvariant(), ["name"] = nameB, ["description"] = "x" })),
            400, "E10001", "roles[2]: Id is duplicated with roles[1].");
        await Status(await Send(jwt, Batch(Item(same), Item(nameB, id: b), Item(same))), 400, "E10001", "roles[2]: Name is duplicated with roles[0].");

        // A DELETED item does not hold its name: the role is deleted and its name is used again by the insert after it.
        var results = Results(await Status(await Send(jwt, Batch(Item(nameA, id: a, rowStatus: "DELETED"), Item(nameA))), 200, "S10000", Success));
        Assert.Equal(["DELETE", "INSERT"], results.Select(r => r.Action));
        Assert.Equal(nameA, (await RowAsync(results[1].RoleId)).Name);
    }

    [Fact]
    public async Task TC_RBATCH_006_validation_before_the_credential()
    {
        await Status(await Send(null, Batch(Item("bad name"))), 400, "E10001", "roles[0]: Role name must be capital letters, digits and _, starting with a letter, at most 64 characters.");
        await Status(await Send(null, """{"roles":[]}"""), 400, "E10001", "Roles must contain 1 to 100 items.");
    }

    [Fact]
    public async Task TC_RBATCH_007_credential_failures()
    {
        var name = NewName();
        await Status(await Send(null, Batch(Item(name))), 401, "E10002", Invalid);
        await Status(await Send("not-a-jwt", Batch(Item(name))), 401, "E10002", Invalid);
        await Status(await Send((await CallerAsync(null)).Jwt, Batch(Item(name))), 401, "E10002", Invalid);
        await Status(await Send((await CallerAsync("PLATFORM_ADMIN")).Jwt, Batch(Item(name))), 401, "E10002", Invalid);

        var (userId, jwt) = await AdminAsync();
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.t_user_access_token SET row_status = 'INACTIVE' WHERE user_id = @u", ("u", userId));
        await Status(await Send(jwt, Batch(Item(name))), 401, "E10002", Invalid);
        Assert.Equal(0L, await NameCountAsync(name));
    }

    [Fact]
    public async Task TC_RBATCH_008_first_missing_target_in_request_order()
    {
        var (_, jwt) = await AdminAsync();
        var deleted = await RoleAsync(NewName(), "DELETED");
        var kept = await RoleAsync(NewName());
        var insert = NewName();

        // Index 1 is the first missing id in request order, although index 3 sorts first.
        var low = Guid.Parse("00000000-0000-4000-8000-000000000001");
        await Status(await Send(jwt, Batch(Item(insert), Item(NewName(), id: Guid.NewGuid()), Item((await RowAsync(kept)).Name, "Changed", kept), Item(NewName(), id: low))),
            404, "E10004", "roles[1]: Role not found.");
        await Status(await Send(jwt, Batch(Item(insert), Item(NewName(), id: deleted))), 404, "E10004", "roles[1]: Role not found.");

        // 404 comes before 403 for a caller who is not an administrator.
        var (_, customer) = await CallerAsync(new[] { "CUSTOMER" });
        await Status(await Send(customer, Batch(Item(NewName(), id: deleted))), 404, "E10004", "roles[0]: Role not found.");

        Assert.Equal(0L, await NameCountAsync(insert));
        Assert.Equal("seeded", (await RowAsync(kept)).Description);
    }

    [Fact]
    public async Task TC_RBATCH_009_only_a_platform_administrator()
    {
        var target = await RoleAsync(NewName());
        var system = await PlatformAdminRoleAsync();
        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
        {
            var (_, jwt) = await CallerAsync(roles);
            // One 403 with no position, before the system-role conflict of index 1.
            await Status(await Send(jwt, Batch(Item(NewName()), Item("PLATFORM_ADMIN", id: system), Item((await RowAsync(target)).Name, id: target))),
                403, "E10009", Forbidden);
        }

        Assert.Equal("seeded", (await RowAsync(target)).Description);
    }

    [Fact]
    public async Task TC_RBATCH_010_the_system_role_rolls_back_the_whole_batch()
    {
        var (_, jwt) = await AdminAsync();
        var system = await PlatformAdminRoleAsync();
        var first = NewName();

        foreach (var status in new string?[] { null, "INACTIVE", "DELETED", "ARCHIVED" })
            await Status(await Send(jwt, Batch(Item(first), Item("PLATFORM_ADMIN", "Renamed", system, status))), 409, "E10006", "roles[1]: System role cannot be changed.");

        await Status(await Send(jwt, Batch(Item(first), Item("PLATFORM_ADMIN"))), 409, "E10003", "roles[1]: Role name already exists.");
        Assert.Equal(0L, await NameCountAsync(first));
        Assert.Equal("ACTIVE", (await RowAsync(system)).RowStatus);
    }

    [Fact]
    public async Task TC_RBATCH_011_duplicate_names_follow_the_request_order()
    {
        var (_, jwt) = await AdminAsync();
        var (a, b) = (await RoleAsync(NewName()), await RoleAsync(NewName(), "LOCKED"));
        var (nameA, nameB) = ((await RowAsync(a)).Name, (await RowAsync(b)).Name);

        // The insert at index 0 meets the name still held by b; the delete at index 1 comes too late.
        await Status(await Send(jwt, Batch(Item(nameB), Item(nameB, id: b, rowStatus: "DELETED"))), 409, "E10003", "roles[0]: Role name already exists.");
        // Renaming a to b's name fails even though index 1 would rename b: no final-state plan.
        await Status(await Send(jwt, Batch(Item(nameB, id: a), Item(NewName(), id: b))), 409, "E10003", "roles[0]: Role name already exists.");
        Assert.Equal((nameA, "LOCKED"), ((await RowAsync(a)).Name, (await RowAsync(b)).RowStatus));

        // A role keeps its own name.
        await Status(await Send(jwt, Batch(Item(nameA, "Own name", a))), 200, "S10000", Success);
    }

    [Fact]
    public async Task TC_RBATCH_012_in_use_blocks_archive_and_delete()
    {
        var (userId, jwt) = await AdminAsync();
        var withUser = await RoleAsync(NewName());
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.t_user_role (created_by, row_status, user_id, role_id) VALUES ('seed', 'LOCKED', @u, @r)", ("u", userId), ("r", withUser));
        var withPermission = await RoleAsync(NewName());
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.t_role_permission (created_by, role_id, permission_id) VALUES ('seed', @r, @p)", ("r", withPermission), ("p", await PermissionAsync()));
        var first = NewName();

        foreach (var role in new[] { withUser, withPermission })
        {
            var name = (await RowAsync(role)).Name;
            foreach (var status in new[] { "ARCHIVED", "DELETED" })
                await Status(await Send(jwt, Batch(Item(first), Item(name, id: role, rowStatus: status))), 409, "E10006", "roles[1]: Role is assigned to users or has permissions.");
            await Status(await Send(jwt, Batch(Item(name, id: role, rowStatus: "INACTIVE"))), 200, "S10000", Success);
        }

        Assert.Equal(0L, await NameCountAsync(first));
    }

    [Fact]
    public async Task TC_RBATCH_013_a_later_failure_keeps_no_earlier_item()
    {
        var (_, jwt) = await AdminAsync();
        var (first, taken) = (NewName(), NewName());
        var updated = await RoleAsync(NewName());
        await RoleAsync(taken);

        await Status(await Send(jwt, Batch(Item(first), Item((await RowAsync(updated)).Name, "Changed", updated), Item(taken))), 409, "E10003", "roles[2]: Role name already exists.");
        Assert.Equal(0L, await NameCountAsync(first));
        Assert.Equal(("seeded", (string?)null), ((await RowAsync(updated)).Description, (await RowAsync(updated)).UpdatedBy));
    }

    [Fact]
    public async Task TC_RBATCH_014_concurrent_batches_with_one_name()
    {
        // Separate administrators, so the caller lock does not queue them: the later name check or uq_m_role_name decides.
        var jwts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AdminAsync()));
        var name = NewName();
        var responses = await Task.WhenAll(jwts.Select(a => Send(a.Jwt, Batch(Item(NewName()), Item(name)))));

        Assert.Equal(1, responses.Count(r => (int)r.StatusCode == 200));
        foreach (var conflict in responses.Where(r => (int)r.StatusCode != 200))
            await Status(conflict, 409, "E10003", "roles[1]: Role name already exists.");
        Assert.Equal(1L, await NameCountAsync(name));
    }

    [Fact]
    public async Task TC_RBATCH_015_locks_and_their_order()
    {
        var (adminId, jwt) = await AdminAsync();
        var (a, b) = (await RoleAsync(NewName()), await RoleAsync(NewName()));
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        foreach (var (sql, id) in new[] { ("SELECT 1 FROM authen.m_user WHERE id = @id FOR UPDATE", adminId), ("SELECT 1 FROM author.m_role WHERE id = @id FOR UPDATE", a) })
        {
            var (connection, transaction) = await HoldAsync(sql, id);
            await Status(await Send(jwt, Batch(Item(NewName(), id: a)), client), 408, "E90001", "The server timed out waiting for the request.");
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }

        // Targets are locked in id order whatever the request order: crossed batches finish without a deadlock.
        var (nameA, nameB) = ((await RowAsync(a)).Name, (await RowAsync(b)).Name);
        var (_, other) = await AdminAsync();
        for (var round = 0; round < 5; round++)
        {
            var responses = await Task.WhenAll(
                Send(jwt, Batch(Item(nameA, "ab", a), Item(nameB, "ab", b))),
                Send(other, Batch(Item(nameB, "ba", b), Item(nameA, "ba", a))));
            foreach (var response in responses) await Status(response, 200, "S10000", Success);
        }
    }

    private sealed class FailingSecondInsert(IRoleRepository inner) : IRoleRepository
    {
        private int _inserts;
        public Task<string?> LockNameAsync(Guid id, CancellationToken ct) => inner.LockNameAsync(id, ct);
        public Task<LockedRole?> LockAnyAsync(Guid id, CancellationToken ct) => inner.LockAnyAsync(id, ct);
        public Task<IReadOnlyDictionary<Guid, string>> LockNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => inner.LockNamesAsync(ids, ct);
        public Task<bool> NameTakenAsync(string name, Guid? excludeId, CancellationToken ct) => inner.NameTakenAsync(name, excludeId, ct);
        public Task<bool> InUseAsync(Guid id, CancellationToken ct) => inner.InUseAsync(id, ct);
        public Task<bool> UpdateAsync(Guid id, RoleValues values, string by, DateTime now, CancellationToken ct) => inner.UpdateAsync(id, values, by, now, ct);

        public async Task<Guid> InsertAsync(RoleValues values, string by, DateTime now, CancellationToken ct)
        {
            var id = await inner.InsertAsync(values, by, now, ct);
            return ++_inserts == 2 ? throw new InvalidOperationException("injected after the second insert") : id;
        }
    }

    [Fact]
    public async Task TC_RBATCH_016_an_unexpected_failure_rolls_back_every_item()
    {
        var (_, jwt) = await AdminAsync();
        var (first, second) = (NewName(), NewName());
        var client = Factory.WithWebHostBuilder(w => w.ConfigureTestServices(s =>
        {
            s.AddScoped<RoleRepository>();
            s.AddScoped<IRoleRepository>(p => new FailingSecondInsert(p.GetRequiredService<RoleRepository>()));
        })).CreateClient();

        await Status(await Send(jwt, Batch(Item(first), Item(second)), client), 500, null, "An unexpected error occurred.");
        Assert.Equal((0L, 0L), (await NameCountAsync(first), await NameCountAsync(second)));
    }
}
