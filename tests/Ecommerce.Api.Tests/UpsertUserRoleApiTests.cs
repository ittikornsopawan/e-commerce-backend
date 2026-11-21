using System.Text.Json;
using System.Text.Json.Nodes;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Author;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/author/10-upsert-user-role/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class UpsertUserRoleApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/user/role/upsert";

    private static JsonObject Body(Guid userId, Guid roleId, Guid? id = null, string? rowStatus = null)
    {
        var body = new JsonObject { ["userId"] = userId.ToString(), ["roleId"] = roleId.ToString() };
        if (id is not null) body["id"] = id.ToString();
        if (rowStatus is not null) body["rowStatus"] = rowStatus;
        return body;
    }

    private Task<HttpResponseMessage> Send(string? jwt, JsonNode body, HttpClient? client = null) => Post(Path, jwt, body.ToJsonString(), client);

    private static Guid UserRoleId(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["userRoleId"], data.EnumerateObject().Select(p => p.Name));
        return data.GetProperty("userRoleId").GetGuid();
    }

    private Task<string> ColumnAsync(Guid id, string column) => ScalarAsync<string>($"SELECT {column}::text FROM author.t_user_role WHERE id = @id", id);

    [Fact]
    public async Task TC_UROLE_001_insert()
    {
        await AnchorAdministratorAsync();
        var (adminId, jwt) = await AdminAsync();
        var (user, role) = (await UserAsync(), await RoleAsync(NewName()));

        var id = UserRoleId(await Status(await Send(jwt, Body(user, role)), 201, "S10000", "Insert User Role Success"));
        Assert.Equal((user.ToString(), role.ToString(), "ACTIVE", adminId.ToString()),
            (await ColumnAsync(id, "user_id"), await ColumnAsync(id, "role_id"), await ColumnAsync(id, "row_status"), await ColumnAsync(id, "created_by")));
    }

    private const string AssignForbidden = "You do not have permission to assign roles.";

    private async Task<long> AssignmentCountAsync(Guid userId, Guid roleId)
    {
        await using var connection = new NpgsqlConnection(Postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM author.t_user_role WHERE user_id = @u AND role_id = @r AND row_status <> 'DELETED'", connection);
        command.Parameters.AddWithValue("u", userId);
        command.Parameters.AddWithValue("r", roleId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task TC_UROLE_002_update_archive_reopen_and_delete()
    {
        await AnchorAdministratorAsync();
        var (adminId, jwt) = await AdminAsync();
        var (user, role) = (await UserAsync(), await RoleAsync(NewName()));
        var id = await UserRoleAsync(user, role);

        Assert.Equal(id, UserRoleId(await Status(await Send(jwt, Body(user, role, id, "LOCKED")), 200, "S10000", "Update User Role Success")));
        Assert.Equal(("LOCKED", adminId.ToString(), "seed"), (await ColumnAsync(id, "row_status"), await ColumnAsync(id, "updated_by"), await ColumnAsync(id, "created_by")));
        await Status(await Send(jwt, Body(user, role, id, "ARCHIVED")), 200, "S10000", "Archive User Role Success");
        await Status(await Send(jwt, Body(user, role, id)), 200, "S10000", "Update User Role Success");
        Assert.Equal("ACTIVE", await ColumnAsync(id, "row_status"));

        await Status(await Send(jwt, Body(user, role, id, "DELETED")), 204, null, null);
        await Status(await Send(jwt, Body(user, role, id)), 404, "E10004", "User role not found.");
        // After a delete the role can be given again, as a new row.
        Assert.NotEqual(id, UserRoleId(await Status(await Send(jwt, Body(user, role)), 201, "S10000", "Insert User Role Success")));
    }

    public static TheoryData<string, string> InvalidBodies => new()
    {
        { "[]", "Request Body must be an object." },
        { """{"userId":"x","roleId":"y","extra":1}""", "Extra is not a known field." },
        { """{"userId":"6f1c0e1e-0000-4000-8000-000000000001","userId":"6f1c0e1e-0000-4000-8000-000000000002","roleId":"6f1c0e1e-0000-4000-8000-000000000003"}""", "User Id must not be duplicated." },
        { """{"userId":null,"roleId":""}""", "User Id and Role Id are required fields." },
        { """{"userId":"x","roleId":"6f1c0e1e-0000-4000-8000-000000000003"}""", "User Id must be a valid UUID." },
        { """{"id":"x","userId":"6f1c0e1e-0000-4000-8000-000000000001","roleId":"y"}""", "Id must be a valid UUID, Role Id must be a valid UUID." },
        { """{"userId":"6f1c0e1e-0000-4000-8000-000000000001","roleId":"6f1c0e1e-0000-4000-8000-000000000003","rowStatus":"DELETED"}""", "Row status must not be DELETED when inserting a user role." },
        { """{"userId":"6f1c0e1e-0000-4000-8000-000000000001","roleId":"6f1c0e1e-0000-4000-8000-000000000003","rowStatus":"GONE"}""", "Row Status must be ACTIVE, INACTIVE, DELETED, LOCKED or ARCHIVED." }
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task TC_UROLE_003_input_validation(string body, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Post(Path, jwt, body), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_UROLE_004_validation_before_the_credential_and_credential_failures()
    {
        await AnchorAdministratorAsync();
        var (user, role) = (await UserAsync(), await RoleAsync(NewName()));
        await Status(await Post(Path, null, """{"userId":"x","roleId":"y"}"""), 400, "E10001", "User Id must be a valid UUID, Role Id must be a valid UUID.");
        await Status(await Send(null, Body(user, role)), 401, "E10002", Invalid);
        await Status(await Send((await CallerAsync(null)).Jwt, Body(user, role)), 401, "E10002", Invalid);
        var (callerId, jwt) = await AdminAsync();
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.t_user_access_token SET row_status = 'INACTIVE' WHERE user_id = @u", ("u", callerId));
        await Status(await Send(jwt, Body(user, role)), 401, "E10002", Invalid);
        Assert.Equal(0L, await AssignmentCountAsync(user, role));
    }

    [Fact]
    public async Task TC_UROLE_005_the_target_user_must_exist_and_the_request_must_give_to_active_rows()
    {
        await AnchorAdministratorAsync();
        var (_, jwt) = await AdminAsync();
        var role = await RoleAsync(NewName());

        // A missing target user is 404 E10005, also when the request takes the role away, and before 403.
        await Status(await Send(jwt, Body(Guid.NewGuid(), role)), 404, "E10005", "User not found.");
        var (_, customer) = await CallerAsync(new[] { "CUSTOMER" });
        await Status(await Send(customer, Body(Guid.NewGuid(), role, Guid.NewGuid(), "DELETED")), 404, "E10005", "User not found.");

        // Giving the role needs an ACTIVE user and an ACTIVE role.
        var (locked, active) = (await UserAsync("LOCKED"), await UserAsync());
        await Status(await Send(jwt, Body(locked, role)), 404, "E10005", "User or role not found or not ACTIVE.");
        await Status(await Send(jwt, Body(active, await RoleAsync(NewName(), "INACTIVE"))), 404, "E10005", "User or role not found or not ACTIVE.");
        await Status(await Send(jwt, Body(active, Guid.NewGuid())), 404, "E10005", "User or role not found or not ACTIVE.");

        // Reopening a row gives the role; taking it away, or keeping an ACTIVE row ACTIVE, does not need ACTIVE rows.
        var held = await UserRoleAsync(locked, role, "INACTIVE");
        await Status(await Send(jwt, Body(locked, role, held)), 404, "E10005", "User or role not found or not ACTIVE.");
        await Status(await Send(jwt, Body(locked, role, held, "DELETED")), 204, null, null);
        var closedRole = await RoleAsync(NewName());
        var kept = await UserRoleAsync(active, closedRole);
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE author.m_role SET row_status = 'LOCKED' WHERE id = @r", ("r", closedRole));
        await Status(await Send(jwt, Body(active, closedRole, kept)), 200, "S10000", "Update User Role Success");
    }

    [Fact]
    public async Task TC_UROLE_006_the_assignment_id_and_its_fixed_user_and_role()
    {
        await AnchorAdministratorAsync();
        var (_, jwt) = await AdminAsync();
        var (user, other, role) = (await UserAsync(), await UserAsync(), await RoleAsync(NewName()));
        var id = await UserRoleAsync(user, role);
        var deleted = await UserRoleAsync(user, await RoleAsync(NewName()), "DELETED");

        await Status(await Send(jwt, Body(user, role, Guid.NewGuid())), 404, "E10004", "User role not found.");
        await Status(await Send(jwt, Body(user, await ScalarAsync<Guid>("SELECT role_id FROM author.t_user_role WHERE id = @id", deleted), deleted)), 404, "E10004", "User role not found.");
        await Status(await Send(jwt, Body(other, role, id)), 400, "E10001", "User Id and Role Id cannot be changed.");
        await Status(await Send(jwt, Body(user, await RoleAsync(NewName()), id)), 400, "E10001", "User Id and Role Id cannot be changed.");
    }

    [Fact]
    public async Task TC_UROLE_007_only_a_platform_administrator_before_the_duplicate_check()
    {
        await AnchorAdministratorAsync();
        var (user, role) = (await UserAsync(), await RoleAsync(NewName()));
        await UserRoleAsync(user, role);
        var system = await PlatformAdminRoleAsync();
        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
        {
            var (_, jwt) = await CallerAsync(roles);
            await Status(await Send(jwt, Body(user, role)), 403, "E10009", AssignForbidden);
            await Status(await Send(jwt, Body(await UserAsync(), system)), 403, "E10009", AssignForbidden);
        }
    }

    [Fact]
    public async Task TC_UROLE_008_duplicate_assignment()
    {
        await AnchorAdministratorAsync();
        var (_, jwt) = await AdminAsync();
        var (user, role) = (await UserAsync(), await RoleAsync(NewName()));
        await UserRoleAsync(user, role, "ARCHIVED");
        await Status(await Send(jwt, Body(user, role)), 409, "E10003", "The user already has this role.");
        Assert.Equal(1L, await AssignmentCountAsync(user, role));
    }

    [Fact]
    public async Task TC_UROLE_009_the_platform_keeps_at_least_one_administrator()
    {
        var anchor = await AnchorAdministratorAsync();
        var system = await PlatformAdminRoleAsync();
        var (_, jwt) = await AdminAsync();

        // Leave the anchor as the only ACTIVE administrator for this case, then put the others back.
        var others = new List<Guid>();
        await using (var connection = new NpgsqlConnection(Postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("UPDATE author.t_user_role SET row_status = 'INACTIVE' WHERE role_id = @r AND row_status = 'ACTIVE' AND id <> @a RETURNING id", connection);
            command.Parameters.AddWithValue("r", system);
            command.Parameters.AddWithValue("a", anchor);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) others.Add(reader.GetGuid(0));
        }

        try
        {
            // An ACTIVE assignment of a user who is not ACTIVE is not an administrator.
            await UserRoleAsync(await UserAsync("LOCKED"), system);
            var anchorUser = await ScalarAsync<Guid>("SELECT user_id FROM author.t_user_role WHERE id = @id", anchor);
            foreach (var status in new[] { "INACTIVE", "LOCKED", "ARCHIVED", "DELETED" })
                await Status(await Send(jwt, Body(anchorUser, system, anchor, status)), 409, "E10006", "The platform must keep at least one administrator.");
            Assert.Equal("ACTIVE", await ColumnAsync(anchor, "row_status"));

            // With a second administrator the first may go.
            var second = await UserAsync();
            var secondId = UserRoleId(await Status(await Send(jwt, Body(second, system)), 201, "S10000", "Insert User Role Success"));
            await Status(await Send(jwt, Body(second, system, secondId, "DELETED")), 204, null, null);
        }
        finally
        {
            foreach (var id in others)
                await ExecuteAsync(Postgres.ConnectionString, "UPDATE author.t_user_role SET row_status = 'ACTIVE' WHERE id = @id", ("id", id));
        }
    }

    [Fact]
    public async Task TC_UROLE_010_concurrent_assignments_of_one_role()
    {
        await AnchorAdministratorAsync();
        var jwts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AdminAsync()));
        var (user, role) = (await UserAsync(), await RoleAsync(NewName()));
        var responses = await Task.WhenAll(jwts.Select(a => Send(a.Jwt, Body(user, role))));

        Assert.Equal(1, responses.Count(r => (int)r.StatusCode == 201));
        foreach (var conflict in responses.Where(r => (int)r.StatusCode != 201))
            await Status(conflict, 409, "E10003", "The user already has this role.");
        Assert.Equal(1L, await AssignmentCountAsync(user, role));
    }

    [Fact]
    public async Task TC_UROLE_011_user_and_role_locks()
    {
        await AnchorAdministratorAsync();
        var (user, role) = (await UserAsync(), await RoleAsync(NewName()));
        var (_, customer) = await CallerAsync(new[] { "CUSTOMER" });
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        // FOR SHARE conflicts only with this API's FOR UPDATE; a non-administrator stops at 403 before any write when not blocked.
        foreach (var (sql, target) in new[] { ("SELECT 1 FROM authen.m_user WHERE id = @id FOR SHARE", user), ("SELECT 1 FROM author.m_role WHERE id = @id FOR SHARE", role) })
        {
            var (connection, transaction) = await HoldAsync(sql, target);
            await Status(await Send(customer, Body(user, role), client), 408, "E90001", null);
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }

        await Status(await Send(customer, Body(user, role), client), 403, "E10009", AssignForbidden);
    }

    private sealed class FailingInsert(IUserRoleRepository inner) : IUserRoleRepository
    {
        public Task<IReadOnlyDictionary<Guid, string>> LockUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) => inner.LockUsersAsync(userIds, ct);
        public Task<UserRoleRow?> LockAssignmentAsync(Guid id, CancellationToken ct) => inner.LockAssignmentAsync(id, ct);
        public Task<bool> TakenAsync(Guid userId, Guid roleId, Guid? excludeId, CancellationToken ct) => inner.TakenAsync(userId, roleId, excludeId, ct);
        public Task<bool> UpdateAsync(Guid id, string rowStatus, string by, DateTime now, CancellationToken ct) => inner.UpdateAsync(id, rowStatus, by, now, ct);
        public Task<long> AdministratorsAsync(CancellationToken ct) => inner.AdministratorsAsync(ct);

        public async Task<Guid> InsertAsync(Guid userId, Guid roleId, string rowStatus, string by, DateTime now, CancellationToken ct)
        {
            await inner.InsertAsync(userId, roleId, rowStatus, by, now, ct);
            throw new InvalidOperationException("injected after the insert");
        }
    }

    [Fact]
    public async Task TC_UROLE_012_an_unexpected_failure_rolls_back()
    {
        await AnchorAdministratorAsync();
        var (_, jwt) = await AdminAsync();
        var (user, role) = (await UserAsync(), await RoleAsync(NewName()));
        var client = Factory.WithWebHostBuilder(w => w.ConfigureTestServices(s =>
        {
            s.AddScoped<UserRoleRepository>();
            s.AddScoped<IUserRoleRepository>(p => new FailingInsert(p.GetRequiredService<UserRoleRepository>()));
        })).CreateClient();

        await Status(await Send(jwt, Body(user, role), client), 500, null, "An unexpected error occurred.");
        Assert.Equal(0L, await AssignmentCountAsync(user, role));
    }
}
