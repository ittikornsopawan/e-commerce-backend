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

// e-commerce/doc/api/author/01-upsert-role/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class UpsertRoleApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/role/upsert";

    private static JsonObject Body(string name, string description = "A role", Guid? id = null, string? rowStatus = null)
    {
        var body = new JsonObject { ["name"] = name, ["description"] = description };
        if (id is not null) body["id"] = id.ToString();
        if (rowStatus is not null) body["rowStatus"] = rowStatus;
        return body;
    }

    private Task<HttpResponseMessage> Send(string? jwt, string body, string query = "", HttpClient? client = null) =>
        Post(Path + query, jwt, body, client);

    private Task<HttpResponseMessage> Send(string? jwt, JsonNode body, HttpClient? client = null) => Send(jwt, body.ToJsonString(), client: client);

    private static Guid RoleId(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["roleId"], data.EnumerateObject().Select(p => p.Name));
        return data.GetProperty("roleId").GetGuid();
    }

    [Fact]
    public async Task TC_ROLE_001_insert()
    {
        var (adminId, jwt) = await AdminAsync();
        var name = NewName();

        var id = RoleId(await Status(await Send(jwt, Body(name, "Manages the catalogue")), 201, "S10000", "Insert Role Success"));

        Assert.Equal((name, "Manages the catalogue", "ACTIVE", adminId.ToString(), (string?)null), await RowAsync(id));
        var archived = RoleId(await Status(await Send(jwt, Body(NewName(), rowStatus: "ARCHIVED")), 201, "S10000", "Insert Role Success"));
        Assert.Equal("ARCHIVED", (await RowAsync(archived)).RowStatus);
    }

    [Fact]
    public async Task TC_ROLE_002_update_and_reopen()
    {
        var (adminId, jwt) = await AdminAsync();
        var id = await RoleAsync(NewName());
        var renamed = NewName();

        Assert.Equal(id, RoleId(await Status(await Send(jwt, Body(renamed, "New text", id, "LOCKED")), 200, "S10000", "Update Role Success")));
        Assert.Equal((renamed, "New text", "LOCKED", "seed", adminId.ToString()), await RowAsync(id));

        await Status(await Send(jwt, Body(renamed, "New text", id)), 200, "S10000", "Update Role Success");
        Assert.Equal("ACTIVE", (await RowAsync(id)).RowStatus);
    }

    [Fact]
    public async Task TC_ROLE_003_archive_delete_and_reuse_the_name()
    {
        var (_, jwt) = await AdminAsync();
        var name = NewName();
        var id = await RoleAsync(name);

        Assert.Equal(id, RoleId(await Status(await Send(jwt, Body(name, id: id, rowStatus: "ARCHIVED")), 200, "S10000", "Archive Role Success")));
        await Status(await Send(jwt, Body(name, id: id, rowStatus: "DELETED")), 204, null, null);
        Assert.Equal("DELETED", (await RowAsync(id)).RowStatus);

        await Status(await Send(jwt, Body(name, id: id)), 404, "E10004", "Role not found.");
        await Status(await Send(jwt, Body(name)), 201, "S10000", "Insert Role Success");
    }

    [Theory]
    [InlineData("""{"description":"x"}""", "Name is a required field.")]
    [InlineData("""{"name":" ","description":null}""", "Name and Description are required fields.")]
    [InlineData("""{}""", "Name and Description are required fields.")]
    [InlineData("""{"name":"catalog_manager","description":"x"}""", "Role name must be capital letters, digits and _, starting with a letter, at most 64 characters.")]
    [InlineData("""{"name":"1ROLE","description":"x"}""", "Role name must be capital letters, digits and _, starting with a letter, at most 64 characters.")]
    [InlineData("""{"name":" ROLE","description":"x"}""", "Role name must be capital letters, digits and _, starting with a letter, at most 64 characters.")]
    [InlineData("""{"name":"ROLE-A","description":"x"}""", "Role name must be capital letters, digits and _, starting with a letter, at most 64 characters.")]
    [InlineData("""{"name":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","description":"x"}""", "Role name must be capital letters, digits and _, starting with a letter, at most 64 characters.")]
    [InlineData("""{"name":"ROLE","description":"x","rowStatus":"DELETED"}""", "Row status must not be DELETED when inserting a role.")]
    [InlineData("""{"name":"ROLE","description":"x","rowStatus":"active"}""", "Row Status must be ACTIVE, INACTIVE, DELETED, LOCKED or ARCHIVED.")]
    [InlineData("""{"name":"ROLE","description":"x","id":"abc"}""", "Id must be a valid UUID.")]
    [InlineData("""{"name":"ROLE","description":"x","id":null}""", "Id must be a valid UUID.")]
    [InlineData("""{"name":1,"description":"x"}""", "Name must be a string.")]
    [InlineData("""{"name":"ROLE","description":"x","isAdmin":true}""", "Is Admin is not a known field.")]
    [InlineData("""{"name":"ROLE","name":"OTHER","description":"x"}""", "Name must not be duplicated.")]
    [InlineData("""[]""", "Request Body must be an object.")]
    [InlineData("""{"name":"bad","description":"x","id":"abc"}""", "Id must be a valid UUID, Role name must be capital letters, digits and _, starting with a letter, at most 64 characters.")]
    public async Task TC_ROLE_004_input_validation(string body, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Send(jwt, body), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_ROLE_004_description_length_and_query_keys()
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Send(jwt, Body(NewName(), new string('x', 257))), 400, "E10001", "Description must be at most 256 characters.");
        await Status(await Send(jwt, Body(NewName(), new string('x', 256))), 201, "S10000", null);
        await Status(await Send(jwt, Body(NewName()).ToJsonString(), "?userId=x"), 400, "E10001", "User Id is not a known field.");
    }

    [Fact]
    public async Task TC_ROLE_005_validation_before_the_credential()
    {
        await Status(await Send(null, "{}"), 400, "E10001", "Name and Description are required fields.");
        var (_, noRoles) = await CallerAsync(null);
        await Status(await Send(noRoles, """{"name":"bad","description":"x"}"""), 400, "E10001", null);
    }

    [Fact]
    public async Task TC_ROLE_006_credential_failures()
    {
        var body = Body(NewName());
        await Status(await Send(null, body), 401, "E10002", Invalid);

        var (userId, _) = await CallerAsync(Admin);
        await Status(await Send(UpsertUserProfileApiTests.Jwt(userId.ToString(), "another-signing-key-0123456789-abcdef", roles: Admin), body), 401, "E10002", Invalid);
        await Status(await Send(UpsertUserProfileApiTests.Jwt(userId.ToString(), roles: Admin), body), 401, "E10002", Invalid); // not stored

        foreach (var malformed in new object?[] { null, "PLATFORM_ADMIN", new[] { "PLATFORM_ADMIN", "PLATFORM_ADMIN" }, new[] { "" } })
        {
            var (_, jwt) = await CallerAsync(malformed);
            await Status(await Send(jwt, body), 401, "E10002", Invalid);
        }

        var (lockedId, lockedJwt) = await AdminAsync();
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'LOCKED' WHERE id = @id", ("id", lockedId));
        await Status(await Send(lockedJwt, body), 401, "E10002", Invalid);
        var (revokedId, revokedJwt) = await AdminAsync();
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.t_user_access_token SET row_status = 'INACTIVE' WHERE user_id = @u", ("u", revokedId));
        await Status(await Send(revokedJwt, body), 401, "E10002", Invalid);
    }

    [Fact]
    public async Task TC_ROLE_007_only_a_platform_administrator()
    {
        var existing = await RoleAsync(NewName());
        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
        {
            var (_, jwt) = await CallerAsync(roles);
            await Status(await Send(jwt, Body(NewName())), 403, "E10009", Forbidden);
            await Status(await Send(jwt, Body(NewName(), id: existing)), 403, "E10009", Forbidden);
            await Status(await Send(jwt, Body(NewName(), id: Guid.NewGuid())), 404, "E10004", "Role not found."); // existence first
        }
    }

    [Fact]
    public async Task TC_ROLE_008_unknown_or_deleted_id()
    {
        var (_, jwt) = await AdminAsync();
        var deleted = await RoleAsync(NewName(), "DELETED");
        await Status(await Send(jwt, Body(NewName(), id: Guid.NewGuid())), 404, "E10004", "Role not found.");
        await Status(await Send(jwt, Body(NewName(), id: deleted)), 404, "E10004", "Role not found.");
        Assert.Equal("DELETED", (await RowAsync(deleted)).RowStatus);
    }

    [Fact]
    public async Task TC_ROLE_009_the_system_role()
    {
        var (_, jwt) = await AdminAsync();
        var system = await PlatformAdminRoleAsync();
        var before = await RowAsync(system);

        foreach (var status in new[] { "ACTIVE", "INACTIVE", "LOCKED", "ARCHIVED", "DELETED" })
            await Status(await Send(jwt, Body("PLATFORM_ADMIN", before.Description, system, status)), 409, "E10006", "System role cannot be changed.");
        await Status(await Send(jwt, Body(NewName(), id: system)), 409, "E10006", "System role cannot be changed.");
        Assert.Equal(before, await RowAsync(system));

        await Status(await Send(jwt, Body("PLATFORM_ADMIN")), 409, "E10003", "Role name already exists.");
    }

    [Fact]
    public async Task TC_ROLE_010_duplicate_name()
    {
        var (_, jwt) = await AdminAsync();
        foreach (var status in new[] { "ACTIVE", "INACTIVE", "LOCKED", "ARCHIVED" })
        {
            var taken = NewName();
            await RoleAsync(taken, status);
            await Status(await Send(jwt, Body(taken)), 409, "E10003", "Role name already exists.");
            var other = await RoleAsync(NewName());
            await Status(await Send(jwt, Body(taken, id: other)), 409, "E10003", "Role name already exists.");
        }

        var own = NewName();
        var id = await RoleAsync(own);
        await Status(await Send(jwt, Body(own, "Kept name", id)), 200, "S10000", "Update Role Success");

        // The name check comes before the in-use check: an in-use role archived under a taken name answers the name 409
        // (without the check, uq_m_role_name would never be reached and the in-use 409 would answer).
        var (userId, _) = await AdminAsync();
        var inUse = await RoleAsync(NewName());
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.t_user_role (created_by, user_id, role_id) VALUES ('seed', @u, @r)", ("u", userId), ("r", inUse));
        var takenName = NewName();
        await RoleAsync(takenName);
        await Status(await Send(jwt, Body(takenName, id: inUse, rowStatus: "ARCHIVED")), 409, "E10003", "Role name already exists.");
    }


    [Fact]
    public async Task TC_ROLE_011_in_use_blocks_archive_and_delete()
    {
        var (userId, jwt) = await AdminAsync();
        var withUser = await RoleAsync(NewName());
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.t_user_role (created_by, row_status, user_id, role_id) VALUES ('seed', 'INACTIVE', @u, @r)", ("u", userId), ("r", withUser));
        var withPermission = await RoleAsync(NewName());
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.t_role_permission (created_by, role_id, permission_id) VALUES ('seed', @r, @p)", ("r", withPermission), ("p", await PermissionAsync()));

        foreach (var role in new[] { withUser, withPermission })
        {
            var name = (await RowAsync(role)).Name;
            foreach (var status in new[] { "ARCHIVED", "DELETED" })
                await Status(await Send(jwt, Body(name, id: role, rowStatus: status)), 409, "E10006", "Role is assigned to users or has permissions.");
            await Status(await Send(jwt, Body(name, id: role, rowStatus: "INACTIVE")), 200, "S10000", "Update Role Success");
            Assert.Equal("INACTIVE", (await RowAsync(role)).RowStatus);
        }

        await ExecuteAsync(Postgres.ConnectionString, "UPDATE author.t_user_role SET row_status = 'DELETED' WHERE role_id = @r", ("r", withUser));
        await Status(await Send(jwt, Body((await RowAsync(withUser)).Name, id: withUser, rowStatus: "DELETED")), 204, null, null);
    }

    [Fact]
    public async Task TC_ROLE_012_concurrent_inserts_of_one_name()
    {
        // Four administrators, so the caller lock does not queue them: only uq_m_role_name can stop the race.
        var jwts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AdminAsync()));
        var name = NewName();
        var responses = await Task.WhenAll(jwts.Select(a => Send(a.Jwt, Body(name))));

        Assert.Equal(1, responses.Count(r => (int)r.StatusCode == 201));
        foreach (var conflict in responses.Where(r => (int)r.StatusCode != 201))
            await Status(conflict, 409, "E10003", "Role name already exists.");
        Assert.Equal(1L, await NameCountAsync(name));
    }



    [Fact]
    public async Task TC_ROLE_013_the_caller_and_target_locks()
    {
        var (adminId, jwt) = await AdminAsync();
        var id = await RoleAsync(NewName());
        var client = Factory.WithWebHostBuilder(b => b.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        foreach (var sql in new[] { "SELECT 1 FROM author.m_role WHERE id = @id FOR UPDATE", "SELECT 1 FROM authen.m_user WHERE id = @id FOR UPDATE" })
        {
            var (connection, transaction) = await HoldAsync(sql, sql.Contains("m_role") ? id : adminId);
            await Status(await Send(jwt, Body(NewName(), id: id), client), 408, "E90001", "The server timed out waiting for the request.");
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }

        Assert.Equal("seeded", (await RowAsync(id)).Description);

        // The target is locked before the 403: a non-administrator waits on a FOR SHARE holder instead of answering 403.
        var (_, customer) = await CallerAsync(new[] { "CUSTOMER" });
        var (shared, sharedTransaction) = await HoldAsync("SELECT 1 FROM author.m_role WHERE id = @id FOR SHARE", id);
        await Status(await Send(customer, Body(NewName(), id: id), client), 408, "E90001", null);
        await sharedTransaction.RollbackAsync();
        await shared.DisposeAsync();
        await Status(await Send(customer, Body(NewName(), id: id), client), 403, "E10009", Forbidden);
    }

    private sealed class FailingWrite(IRoleRepository inner) : IRoleRepository
    {
        public Task<string?> LockNameAsync(Guid id, CancellationToken ct) => inner.LockNameAsync(id, ct);
        public Task<LockedRole?> LockAnyAsync(Guid id, CancellationToken ct) => inner.LockAnyAsync(id, ct);
        public Task<IReadOnlyDictionary<Guid, string>> LockNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => inner.LockNamesAsync(ids, ct);
        public Task<bool> NameTakenAsync(string name, Guid? excludeId, CancellationToken ct) => inner.NameTakenAsync(name, excludeId, ct);
        public Task<bool> InUseAsync(Guid id, CancellationToken ct) => inner.InUseAsync(id, ct);
        public async Task<Guid> InsertAsync(RoleValues values, string by, DateTime now, CancellationToken ct)
        {
            await inner.InsertAsync(values, by, now, ct);
            throw new InvalidOperationException("injected after the insert");
        }

        public Task<bool> UpdateAsync(Guid id, RoleValues values, string by, DateTime now, CancellationToken ct) => inner.UpdateAsync(id, values, by, now, ct);
    }

    [Fact]
    public async Task TC_ROLE_014_an_unexpected_failure_rolls_back()
    {
        var (_, jwt) = await AdminAsync();
        var name = NewName();
        var client = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddScoped<RoleRepository>();
            s.AddScoped<IRoleRepository>(p => new FailingWrite(p.GetRequiredService<RoleRepository>()));
        })).CreateClient();

        await Status(await Send(jwt, Body(name), client), 500, null, "An unexpected error occurred.");
        Assert.Equal(0L, await NameCountAsync(name));
    }
}
