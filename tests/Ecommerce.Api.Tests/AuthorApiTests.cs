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
