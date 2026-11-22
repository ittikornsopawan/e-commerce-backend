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

// e-commerce/doc/api/author/15-upsert-user-permission/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class UpsertUserPermissionApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/user/permission/upsert";

    private static JsonObject Body(Guid userId, Guid permissionId, Guid? scopeId = null, Guid? id = null, string? rowStatus = null)
    {
        var body = new JsonObject { ["userId"] = userId.ToString(), ["permissionId"] = permissionId.ToString() };
        if (scopeId is not null) body["scopeId"] = scopeId.ToString();
        if (id is not null) body["id"] = id.ToString();
        if (rowStatus is not null) body["rowStatus"] = rowStatus;
        return body;
    }

    private Task<HttpResponseMessage> Send(string? jwt, JsonNode body, HttpClient? client = null) => Post(Path, jwt, body.ToJsonString(), client);

    private static Guid GrantId(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["userPermissionId"], data.EnumerateObject().Select(p => p.Name));
        return data.GetProperty("userPermissionId").GetGuid();
    }

    private Task<string> ColumnAsync(Guid id, string column) => ScalarAsync<string>($"SELECT {column}::text FROM author.t_user_permission WHERE id = @id", id);

    [Fact]
    public async Task TC_UPERM_001_insert_scoped_and_unscoped()
    {
        var (adminId, jwt) = await AdminAsync();
        var user = await UserAsync();
        var (scoped, scope) = (await PermissionAsync(NewPermissionName()), await ScopeAsync());
        var definition = await PermissionScopeAsync(scoped, scope);

        var id = GrantId(await Status(await Send(jwt, Body(user, scoped, scope)), 201, "S10000", "Insert User Permission Success"));
        Assert.Equal((definition.ToString(), "ACTIVE", adminId.ToString()), (await ColumnAsync(id, "permission_scope_id"), await ColumnAsync(id, "row_status"), await ColumnAsync(id, "created_by")));

        var plain = GrantId(await Status(await Send(jwt, Body(user, await PermissionAsync(NewPermissionName()))), 201, "S10000", "Insert User Permission Success"));
        Assert.Null(await ScalarAsync<object>("SELECT permission_scope_id FROM author.t_user_permission WHERE id = @id", plain) as Guid?);
    }

    private const string AssignForbidden = "You do not have permission to assign permissions.";
    private const string NotActive = "User, permission or scope not found or not ACTIVE.";

    [Fact]
    public async Task TC_UPERM_002_update_archive_reopen_and_delete()
    {
        var (adminId, jwt) = await AdminAsync();
        var (user, permission) = (await UserAsync(), await PermissionAsync(NewPermissionName()));
        var id = await UserPermissionAsync(user, permission);

        Assert.Equal(id, GrantId(await Status(await Send(jwt, Body(user, permission, id: id, rowStatus: "LOCKED")), 200, "S10000", "Update User Permission Success")));
        Assert.Equal(("LOCKED", adminId.ToString()), (await ColumnAsync(id, "row_status"), await ColumnAsync(id, "updated_by")));
        await Status(await Send(jwt, Body(user, permission, id: id, rowStatus: "ARCHIVED")), 200, "S10000", "Archive User Permission Success");
        await Status(await Send(jwt, Body(user, permission, id: id)), 200, "S10000", "Update User Permission Success");
        await Status(await Send(jwt, Body(user, permission, id: id, rowStatus: "DELETED")), 204, null, null);
        await Status(await Send(jwt, Body(user, permission, id: id)), 404, "E10004", "User permission not found.");
    }

    public static TheoryData<string, string> InvalidBodies => new()
    {
        { "[]", "Request Body must be an object." },
        { """{"userId":"x","permissionId":"y","extra":1}""", "Extra is not a known field." },
        { """{"userId":null}""", "User Id and Permission Id are required fields." },
        { """{"userId":"6f1c0e1e-0000-4000-8000-000000000001","permissionId":"6f1c0e1e-0000-4000-8000-000000000002","scopeId":null}""", "Scope Id must be a valid UUID." },
        { """{"userId":"6f1c0e1e-0000-4000-8000-000000000001","permissionId":"y","scopeId":"z"}""", "Permission Id must be a valid UUID, Scope Id must be a valid UUID." },
        { """{"userId":"6f1c0e1e-0000-4000-8000-000000000001","permissionId":"6f1c0e1e-0000-4000-8000-000000000002","rowStatus":"DELETED"}""", "Row status must not be DELETED when inserting a user permission." }
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task TC_UPERM_003_input_validation(string body, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Post(Path, jwt, body), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_UPERM_004_validation_before_the_credential_credentials_and_the_target_user()
    {
        var (user, permission) = (await UserAsync(), await PermissionAsync(NewPermissionName()));
        await Status(await Post(Path, null, """{"userId":"x","permissionId":"y"}"""), 400, "E10001", "User Id must be a valid UUID, Permission Id must be a valid UUID.");
        await Status(await Send(null, Body(user, permission)), 401, "E10002", Invalid);
        await Status(await Send((await CallerAsync(null)).Jwt, Body(user, permission)), 401, "E10002", Invalid);

        var (_, jwt) = await AdminAsync();
        await Status(await Send(jwt, Body(Guid.NewGuid(), permission)), 404, "E10005", "User not found.");
        var (_, customer) = await CallerAsync(new[] { "CUSTOMER" });
        await Status(await Send(customer, Body(Guid.NewGuid(), permission, id: Guid.NewGuid(), rowStatus: "DELETED")), 404, "E10005", "User not found.");
        await Status(await Send(jwt, Body(user, permission, id: Guid.NewGuid())), 404, "E10004", "User permission not found.");
    }

    [Fact]
    public async Task TC_UPERM_005_the_scope_rule_and_fixed_keys()
    {
        var (_, jwt) = await AdminAsync();
        var user = await UserAsync();
        var (scoped, scope) = (await PermissionAsync(NewPermissionName()), await ScopeAsync());
        var definition = await PermissionScopeAsync(scoped, scope);
        var plain = await PermissionAsync(NewPermissionName());

        await Status(await Send(jwt, Body(user, scoped)), 400, "E10001", "Scope Id is required for a permission that has scopes.");
        await Status(await Send(jwt, Body(user, scoped, await ScopeAsync())), 400, "E10001", "Scope is not a scope of the permission.");
        await Status(await Send(jwt, Body(user, plain, scope)), 400, "E10001", "Scope is not a scope of the permission.");

        var other = await ScopeAsync();
        await PermissionScopeAsync(scoped, other);
        var id = await UserPermissionAsync(user, scoped, definition);
        await Status(await Send(jwt, Body(user, scoped, other, id)), 400, "E10001", "User Id, Permission Id and Scope Id cannot be changed.");
        await Status(await Send(jwt, Body(await UserAsync(), scoped, scope, id)), 400, "E10001", "User Id, Permission Id and Scope Id cannot be changed.");
    }

    [Fact]
    public async Task TC_UPERM_006_active_rows_to_give()
    {
        var (_, jwt) = await AdminAsync();
        var (scoped, scope) = (await PermissionAsync(NewPermissionName()), await ScopeAsync());
        var definition = await PermissionScopeAsync(scoped, scope);

        await Status(await Send(jwt, Body(await UserAsync("LOCKED"), scoped, scope)), 404, "E10005", NotActive);
        await Status(await Send(jwt, Body(await UserAsync(), await PermissionAsync(NewPermissionName(), "INACTIVE"))), 404, "E10005", NotActive);
        await Status(await Send(jwt, Body(await UserAsync(), Guid.NewGuid())), 404, "E10005", NotActive);
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE author.m_scope SET row_status = 'LOCKED' WHERE id = @s", ("s", scope));
        await Status(await Send(jwt, Body(await UserAsync(), scoped, scope)), 404, "E10005", NotActive);

        // Reopening gives; taking away needs no ACTIVE row.
        var user = await UserAsync();
        var held = await UserPermissionAsync(user, scoped, definition, "INACTIVE");
        await Status(await Send(jwt, Body(user, scoped, scope, held)), 404, "E10005", NotActive);
        await Status(await Send(jwt, Body(user, scoped, scope, held, "DELETED")), 204, null, null);
    }

    [Fact]
    public async Task TC_UPERM_007_only_a_platform_administrator_before_the_duplicate_check()
    {
        var (user, permission) = (await UserAsync(), await PermissionAsync(NewPermissionName()));
        await UserPermissionAsync(user, permission);
        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
            await Status(await Send((await CallerAsync(roles)).Jwt, Body(user, permission)), 403, "E10009", AssignForbidden);
    }

    [Fact]
    public async Task TC_UPERM_008_duplicates_per_scope()
    {
        var (_, jwt) = await AdminAsync();
        var user = await UserAsync();
        var plain = await PermissionAsync(NewPermissionName());
        await UserPermissionAsync(user, plain, rowStatus: "ARCHIVED");
        await Status(await Send(jwt, Body(user, plain)), 409, "E10003", "The user already has this permission in this scope.");

        // One grant per scope: a second scope of the same permission is a second grant.
        var (scoped, s1, s2) = (await PermissionAsync(NewPermissionName()), await ScopeAsync(), await ScopeAsync());
        var d1 = await PermissionScopeAsync(scoped, s1);
        await PermissionScopeAsync(scoped, s2);
        await UserPermissionAsync(user, scoped, d1);
        await Status(await Send(jwt, Body(user, scoped, s1)), 409, "E10003", "The user already has this permission in this scope.");
        await Status(await Send(jwt, Body(user, scoped, s2)), 201, "S10000", "Insert User Permission Success");
    }

    [Fact]
    public async Task TC_UPERM_009_concurrent_grants_of_one_scope()
    {
        var jwts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AdminAsync()));
        var (user, permission) = (await UserAsync(), await PermissionAsync(NewPermissionName()));
        var responses = await Task.WhenAll(jwts.Select(a => Send(a.Jwt, Body(user, permission))));
        Assert.Equal(1, responses.Count(r => (int)r.StatusCode == 201));
        foreach (var conflict in responses.Where(r => (int)r.StatusCode != 201))
            await Status(conflict, 409, "E10003", "The user already has this permission in this scope.");
    }

    [Fact]
    public async Task TC_UPERM_010_user_and_permission_locks()
    {
        var (user, permission) = (await UserAsync(), await PermissionAsync(NewPermissionName()));
        var (_, customer) = await CallerAsync(new[] { "CUSTOMER" });
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        foreach (var (sql, target) in new[] { ("SELECT 1 FROM authen.m_user WHERE id = @id FOR SHARE", user), ("SELECT 1 FROM author.m_permission WHERE id = @id FOR SHARE", permission) })
        {
            var (connection, transaction) = await HoldAsync(sql, target);
            await Status(await Send(customer, Body(user, permission), client), 408, "E90001", null);
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }

        await Status(await Send(customer, Body(user, permission), client), 403, "E10009", AssignForbidden);
    }

    private sealed class FailingInsert(IUserPermissionRepository inner) : IUserPermissionRepository
    {
        public Task<UserPermissionRow?> LockGrantAsync(Guid id, CancellationToken ct) => inner.LockGrantAsync(id, ct);
        public Task<bool> TakenAsync(Guid userId, Guid permissionId, Guid? permissionScopeId, Guid? excludeId, CancellationToken ct) =>
            inner.TakenAsync(userId, permissionId, permissionScopeId, excludeId, ct);
        public Task<bool> UpdateAsync(Guid id, string rowStatus, string by, DateTime now, CancellationToken ct) => inner.UpdateAsync(id, rowStatus, by, now, ct);

        public async Task<Guid> InsertAsync(Guid userId, Guid permissionId, Guid? permissionScopeId, string rowStatus, string by, DateTime now, CancellationToken ct)
        {
            await inner.InsertAsync(userId, permissionId, permissionScopeId, rowStatus, by, now, ct);
            throw new InvalidOperationException("injected after the insert");
        }
    }

    [Fact]
    public async Task TC_UPERM_011_an_unexpected_failure_rolls_back()
    {
        var (_, jwt) = await AdminAsync();
        var (user, permission) = (await UserAsync(), await PermissionAsync(NewPermissionName()));
        var client = Factory.WithWebHostBuilder(w => w.ConfigureTestServices(sv =>
        {
            sv.AddScoped<UserPermissionRepository>();
            sv.AddScoped<IUserPermissionRepository>(p => new FailingInsert(p.GetRequiredService<UserPermissionRepository>()));
        })).CreateClient();

        await Status(await Send(jwt, Body(user, permission), client), 500, null, "An unexpected error occurred.");
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM author.t_user_permission WHERE user_id = @id", user));
    }
}
