using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/authen-and-author/06-logout/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class LogoutApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/auth/logout";
    private const string Key = "unit-test-signing-key-0123456789-abcdef";
    private const string Invalid = "Invalid or expired access token.";
    private readonly PostgresFixture _postgres;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FieldProtector _protector = new(FieldKey);

    public LogoutApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        _factory = factory.WithWebHostBuilder(b => b
            .UseSetting("Database:ConnectionString", postgres.ConnectionString)
            .UseSetting("Jwt:SigningKey", Key)
            .UseSetting("Security:FieldEncryptionKey", Convert.ToBase64String(FieldKey)));
    }

    private static string Jwt(string sub, string key = Key, int expiresInMinutes = 15) =>
        new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "ecommerce", Audience = "ecommerce-api", IssuedAt = DateTime.UtcNow.AddMinutes(-30),
            Expires = DateTime.UtcNow.AddMinutes(expiresInMinutes),
            Claims = new Dictionary<string, object> { ["sub"] = sub, ["jti"] = Guid.NewGuid().ToString() },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)
        });

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private sealed record Fixture(Guid UserId, string A2, Guid S1, Guid S2, Guid OtherUser);

    private async Task<Fixture> SessionsAsync()
    {
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var repository = new RegistrationRepository(db, _protector);
        var userId = await repository.AddAsync(Registration(), CancellationToken.None);
        var otherId = await repository.AddAsync(Registration(), CancellationToken.None);
        var (s1, s2) = (Guid.NewGuid(), Guid.NewGuid());
        var a2 = Jwt(userId.ToString());
        await Row("authen.t_user_access_token", userId, s1, RandomNumberGenerator.GetBytes(32), "ACTIVE", -5); // A1, expired but ACTIVE
        await Row("authen.t_user_access_token", userId, s1, Hash(a2), "ACTIVE", 15);                          // A2, presented
        await Row("authen.t_user_refresh_token", userId, s1, RandomNumberGenerator.GetBytes(32), "INACTIVE", 60); // R1, rotated
        await Row("authen.t_user_refresh_token", userId, s1, RandomNumberGenerator.GetBytes(32), "ACTIVE", 60);   // R2
        await Row("authen.t_user_access_token", userId, s2, RandomNumberGenerator.GetBytes(32), "ACTIVE", 15);
        await Row("authen.t_user_refresh_token", userId, s2, RandomNumberGenerator.GetBytes(32), "ACTIVE", 60);
        await Row("authen.t_user_access_token", otherId, s1, RandomNumberGenerator.GetBytes(32), "ACTIVE", 15);   // legacy reuse of S1
        await Row("authen.t_user_refresh_token", otherId, s1, RandomNumberGenerator.GetBytes(32), "ACTIVE", 60);
        return new Fixture(userId, a2, s1, s2, otherId);
    }

    private Task Row(string table, Guid userId, Guid sessionId, byte[] hash, string status, int minutes) =>
        ExecuteAsync(_postgres.ConnectionString,
            $"INSERT INTO {table} (created_by, row_status, user_id, session_id, token, expired_at) VALUES ('test', @s, @u, @sid, @t, @x)",
            ("s", status), ("u", userId), ("sid", sessionId), ("t", hash), ("x", DateTime.UtcNow.AddMinutes(minutes)));

    private Task<HttpResponseMessage> Send(string? jwt, string? timestamp = "now", string? body = null, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (jwt is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        if (timestamp is not null) request.Headers.Add("timestamp", timestamp == "now" ? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") : timestamp);
        return (client ?? _factory.CreateClient()).SendAsync(request);
    }

    private static async Task Status(HttpResponseMessage response, int code, string? bizCode, string? bizMessage)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, (int)response.StatusCode);
        var status = json.RootElement.GetProperty("status");
        if (bizCode is null) Assert.False(status.TryGetProperty("bizCode", out _));
        else Assert.Equal(bizCode, status.GetProperty("bizCode").GetString());
        if (bizMessage is not null) Assert.Equal(bizMessage, status.GetProperty("bizMessage").GetString());
        Assert.False(json.RootElement.TryGetProperty("data", out _));
    }

    private async Task<int> Active(Guid userId, Guid sessionId)
    {
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        return await db.AccessTokens.CountAsync(t => t.UserId == userId && t.SessionId == sessionId && t.RowStatus == "ACTIVE")
               + await db.RefreshTokens.CountAsync(t => t.UserId == userId && t.SessionId == sessionId && t.RowStatus == "ACTIVE");
    }

    [Fact]
    public async Task TC_LOGOUT_001_the_session_is_closed()
    {
        var f = await SessionsAsync();

        await Status(await Send(f.A2), 200, "S10000", "Logout Success");

        Assert.Equal(0, await Active(f.UserId, f.S1));
        Assert.Equal(2, await Active(f.UserId, f.S2));
        Assert.Equal(2, await Active(f.OtherUser, f.S1));
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        Assert.Equal(3, await db.AccessTokens.CountAsync(t => t.UserId == f.UserId));
        Assert.All(await db.AccessTokens.Where(t => t.UserId == f.UserId && t.SessionId == f.S1).ToListAsync(), t => Assert.Equal(f.UserId.ToString(), t.UpdatedBy));
    }

    [Fact]
    public async Task TC_LOGOUT_002_validation_comes_first_even_without_a_token()
    {
        var f = await SessionsAsync();
        foreach (var jwt in new[] { f.A2, null })
        {
            await Status(await Send(jwt, timestamp: null), 400, "E10001", "Timestamp is a required header.");
            await Status(await Send(jwt, timestamp: "yesterday"), 400, "E10001", "Timestamp must be a valid UTC timestamp.");
            await Status(await Send(jwt, body: "{}"), 400, "E10001", "Timestamp must be a valid UTC timestamp and the request body must be empty.");
        }

        Assert.Equal(3, await Active(f.UserId, f.S1)); // A1, A2 and R2 still ACTIVE (R1 was already INACTIVE)
    }

    [Fact]
    public async Task TC_LOGOUT_003_a_bad_jwt_is_401()
    {
        var f = await SessionsAsync();
        foreach (var jwt in new[] { null, "not-a-jwt", Jwt(f.UserId.ToString(), "another-signing-key-0123456789-abcdef"), Jwt(f.UserId.ToString(), expiresInMinutes: -5), Jwt("abc") })
        {
            await Status(await Send(jwt), 401, "E10002", Invalid);
        }

        Assert.Equal(3, await Active(f.UserId, f.S1));
    }

    [Fact]
    public async Task TC_LOGOUT_004_a_revoked_unknown_or_foreign_access_token_is_401()
    {
        var f = await SessionsAsync();
        await Status(await Send(Jwt(f.UserId.ToString())), 401, "E10002", Invalid);       // no stored row
        await Status(await Send(Jwt(f.OtherUser.ToString())), 401, "E10002", Invalid);    // another user, no row of theirs
        await Status(await Send(f.A2), 200, "S10000", null);
        await Status(await Send(f.A2), 401, "E10002", Invalid);                           // logout again
        Assert.Equal(2, await Active(f.UserId, f.S2));
    }

    [Fact]
    public async Task TC_LOGOUT_005_a_user_that_is_not_active_is_401_and_nothing_changes()
    {
        var f = await SessionsAsync();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'LOCKED' WHERE id = @id", ("id", f.UserId));
        await Status(await Send(f.A2), 401, "E10002", Invalid);
        Assert.Equal(3, await Active(f.UserId, f.S1));
    }

    [Fact]
    public async Task TC_LOGOUT_006_concurrent_logouts_give_one_200_and_one_401()
    {
        var f = await SessionsAsync();
        var responses = await Task.WhenAll(Send(f.A2), Send(f.A2));
        Assert.Equal([200, 401], responses.Select(r => (int)r.StatusCode).Order());
        Assert.Equal(0, await Active(f.UserId, f.S1));
    }

    [Fact]
    public async Task TC_LOGOUT_007_the_user_row_lock_makes_a_slow_request_time_out_without_changes()
    {
        var f = await SessionsAsync();
        await using (var blocker = new NpgsqlConnection(_postgres.ConnectionString))
        {
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            await using (var lockRow = new NpgsqlCommand("SELECT id FROM authen.m_user WHERE id = @id FOR UPDATE", blocker, transaction))
            {
                lockRow.Parameters.AddWithValue("id", f.UserId);
                await lockRow.ExecuteScalarAsync();
            }

            var client = _factory.WithWebHostBuilder(b => b.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();
            await Status(await Send(f.A2, client: client), 408, "E90001", "The server timed out waiting for the request.");
            Assert.Equal(3, await Active(f.UserId, f.S1));
            await transaction.RollbackAsync();
        }

        await Status(await Send(f.A2), 200, "S10000", null);
    }

    private sealed class FailingLogouts(ILogoutRepository inner, LogoutFailure failure) : ILogoutRepository
    {
        public Task<bool> LockActiveUserAsync(Guid u, CancellationToken ct) => inner.LockActiveUserAsync(u, ct);
        public Task<Guid?> GetSessionAsync(byte[] h, Guid u, DateTime n, CancellationToken ct) => inner.GetSessionAsync(h, u, n, ct);
        public async Task<int> CloseSessionAsync(Guid s, Guid u, DateTime n, CancellationToken ct)
        {
            await failure.Db.AccessTokens.Where(t => t.SessionId == s && t.UserId == u && t.RowStatus == "ACTIVE")
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.RowStatus, "INACTIVE"), ct); // the access update ran
            throw new InvalidOperationException("injected after the access-token update");
        }
    }

    private sealed class LogoutFailure(EcommerceDbContext db) { public EcommerceDbContext Db { get; } = db; }

    [Fact]
    public async Task TC_LOGOUT_008_an_unexpected_failure_is_500_with_no_partial_revocation()
    {
        var f = await SessionsAsync();
        var client = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddScoped<LogoutRepository>();
            s.AddScoped(p => new LogoutFailure(p.GetRequiredService<EcommerceDbContext>()));
            s.AddScoped<ILogoutRepository>(p => new FailingLogouts(p.GetRequiredService<LogoutRepository>(), p.GetRequiredService<LogoutFailure>()));
        })).CreateClient();

        await Status(await Send(f.A2, client: client), 500, null, "An unexpected error occurred.");

        Assert.Equal(3, await Active(f.UserId, f.S1));
    }
}
