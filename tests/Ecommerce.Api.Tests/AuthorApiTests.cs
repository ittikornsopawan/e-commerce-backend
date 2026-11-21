using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

/// <summary>What the author API tests share: callers with stored tokens, seeded roles and bindings, the request and the status envelope.</summary>
public abstract class AuthorApiTests
{
    protected const string Invalid = "Invalid or expired access token.";
    protected const string Forbidden = "You do not have permission to manage roles.";
    protected static readonly string[] Admin = ["PLATFORM_ADMIN"];
    protected readonly PostgresFixture Postgres;
    protected readonly WebApplicationFactory<Program> Factory;
    private readonly FieldProtector _protector = new(FieldKey);

    protected AuthorApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    {
        Postgres = postgres;
        Factory = factory.WithWebHostBuilder(b => b
            .UseSetting("Database:ConnectionString", postgres.ConnectionString)
            .UseSetting("Jwt:SigningKey", "unit-test-signing-key-0123456789-abcdef")
            .UseSetting("Security:FieldEncryptionKey", Convert.ToBase64String(FieldKey)));
    }

    /// <summary>A user with a stored ACTIVE access token whose JWT carries <paramref name="roles"/> (null omits the claim).</summary>
    protected async Task<(Guid UserId, string Jwt)> CallerAsync(object? roles)
    {
        await using var db = EcommerceDbContext.Create(Postgres.ConnectionString);
        var userId = await new RegistrationRepository(db, _protector).AddAsync(Registration(), CancellationToken.None);
        var jwt = UpsertUserProfileApiTests.Jwt(userId.ToString(), roles: roles);
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO authen.t_user_access_token (created_by, user_id, session_id, token, expired_at) VALUES ('test', @u, @s, @t, @x)",
            ("u", userId), ("s", Guid.NewGuid()), ("t", SHA256.HashData(Encoding.UTF8.GetBytes(jwt))), ("x", DateTime.UtcNow.AddMinutes(15)));
        return (userId, jwt);
    }

    protected Task<(Guid UserId, string Jwt)> AdminAsync() => CallerAsync(Admin);

    /// <summary>A role name no other test uses: capital letters and hex digits.</summary>
    protected static string NewName() => "R_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    protected async Task<Guid> RoleAsync(string name, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.m_role (id, created_by, row_status, name, description) VALUES (@id, 'seed', @r, @n, 'seeded')",
            ("id", id), ("r", rowStatus), ("n", name));
        return id;
    }

    /// <summary>The seeded system role, added once for the shared database.</summary>
    protected async Task<Guid> PlatformAdminRoleAsync()
    {
        await ExecuteAsync(Postgres.ConnectionString,
            """
            INSERT INTO author.m_role (created_by, name, description)
            SELECT 'seed', 'PLATFORM_ADMIN', 'Platform administrator'
            WHERE NOT EXISTS (SELECT 1 FROM author.m_role WHERE name = 'PLATFORM_ADMIN' AND row_status <> 'DELETED')
            """);
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM author.m_role WHERE name = 'PLATFORM_ADMIN' AND row_status <> 'DELETED'", connection);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    protected async Task<(string Name, string Description, string RowStatus, string CreatedBy, string? UpdatedBy)> RowAsync(Guid id)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("SELECT name, description, row_status, created_by, updated_by FROM author.m_role WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    protected async Task<Guid> PermissionAsync()
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.m_permission (id, created_by, name, description) VALUES (@id, 'seed', @n, 'seeded')",
            ("id", id), ("n", "p" + Guid.NewGuid().ToString("N") + ".manage"));
        return id;
    }

    /// <summary>A permission code no other test uses: lower-case, two parts.</summary>
    protected static string NewPermissionName() => "p" + Guid.NewGuid().ToString("N") + ".manage";

    protected async Task<Guid> PermissionAsync(string name, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.m_permission (id, created_by, row_status, name, description) VALUES (@id, 'seed', @r, @n, 'seeded')",
            ("id", id), ("r", rowStatus), ("n", name));
        return id;
    }

    protected Task<Guid> ScopeAsync(string rowStatus = "ACTIVE") => ScopeAsync("s_" + Guid.NewGuid().ToString("N"), rowStatus);

    /// <summary>A scope master with the given code; its name is the code with "Name " in front.</summary>
    protected async Task<Guid> ScopeAsync(string code, string rowStatus)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.m_scope (id, created_by, row_status, scope, name, description) VALUES (@id, 'seed', @r, @s, 'Name ' || @s, 'seeded')",
            ("id", id), ("r", rowStatus), ("s", code));
        return id;
    }

    protected async Task<Guid> PermissionScopeAsync(Guid permissionId, Guid scopeId, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.t_permission_scope (id, created_by, row_status, permission_id, scope_id) VALUES (@id, 'seed', @r, @p, @s)",
            ("id", id), ("r", rowStatus), ("p", permissionId), ("s", scopeId));
        return id;
    }

    /// <summary>A user (ACTIVE unless <paramref name="rowStatus"/> says otherwise) with no token; returns its id.</summary>
    protected async Task<Guid> UserAsync(string rowStatus = "ACTIVE")
    {
        await using var db = EcommerceDbContext.Create(Postgres.ConnectionString);
        var userId = await new RegistrationRepository(db, _protector).AddAsync(Registration(), CancellationToken.None);
        if (rowStatus != "ACTIVE")
            await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.m_user SET row_status = @r WHERE id = @u", ("r", rowStatus), ("u", userId));
        return userId;
    }

    protected async Task<Guid> UserRoleAsync(Guid userId, Guid roleId, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.t_user_role (id, created_by, row_status, user_id, role_id) VALUES (@id, 'seed', @r, @u, @role)",
            ("id", id), ("r", rowStatus), ("u", userId), ("role", roleId));
        return id;
    }

    /// <summary>
    /// The platform keeps at least one administrator (spec 10), and every user-role write checks it: this ACTIVE user with
    /// an ACTIVE PLATFORM_ADMIN assignment is added once for the shared database and left in place.
    /// </summary>
    protected async Task<Guid> AnchorAdministratorAsync()
    {
        var role = await PlatformAdminRoleAsync();
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT ur.id FROM author.t_user_role ur JOIN authen.m_user u ON u.id = ur.user_id WHERE ur.role_id = @r AND ur.row_status = 'ACTIVE' AND u.row_status = 'ACTIVE' ORDER BY ur.id LIMIT 1", connection);
        command.Parameters.AddWithValue("r", role);
        if (await command.ExecuteScalarAsync() is Guid existing) return existing;
        return await UserRoleAsync(await UserAsync(), role);
    }

    /// <summary>One value of <paramref name="sql"/> with <c>@id</c>, read on a UTC session.</summary>
    protected async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    protected async Task<long> NameCountAsync(string name)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM author.m_role WHERE name = @n", connection);
        command.Parameters.AddWithValue("n", name);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Another session holding a lock (<paramref name="sql"/> with <c>@id</c>) until the caller rolls it back.</summary>
    protected async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> HoldAsync(string sql, Guid id)
    {
        var connection = new NpgsqlConnection(Postgres.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
        return (connection, transaction);
    }

    protected Task<HttpResponseMessage> Post(string path, string? jwt, string body, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (jwt is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        request.Headers.Add("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        return (client ?? Factory.CreateClient()).SendAsync(request);
    }

    protected Task<HttpResponseMessage> Get(string pathAndQuery, string? jwt, string? body = null, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (jwt is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        request.Headers.Add("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        return (client ?? Factory.CreateClient()).SendAsync(request);
    }

    /// <summary>Checks the HTTP code and the status envelope; a 4xx/5xx has no data and a 204 has no body.</summary>
    protected static async Task<JsonElement> Status(HttpResponseMessage response, int code, string? bizCode, string? bizMessage)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(code == (int)response.StatusCode, $"{(int)response.StatusCode}: {text}");
        if (code == 204)
        {
            Assert.Equal("", text);
            return default;
        }

        using var json = JsonDocument.Parse(text);
        var root = json.RootElement.Clone();
        var status = root.GetProperty("status");
        if (bizCode is null) Assert.False(status.TryGetProperty("bizCode", out _));
        else Assert.Equal(bizCode, status.GetProperty("bizCode").GetString());
        if (bizMessage is not null) Assert.Equal(bizMessage, status.GetProperty("bizMessage").GetString());
        if (code >= 400) Assert.False(root.TryGetProperty("data", out _));
        return root;
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(EcommerceDbContext.UtcSession(Postgres.ConnectionString));
        await connection.OpenAsync();
        return connection;
    }
}
