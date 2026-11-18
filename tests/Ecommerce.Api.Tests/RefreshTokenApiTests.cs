using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/authen-and-author/05-refresh-token/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class RefreshTokenApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/auth/refresh-token";
    private const string Invalid = "Invalid or expired refresh token.";
    private readonly PostgresFixture _postgres;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FieldProtector _protector = new(FieldKey);

    public RefreshTokenApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        _factory = factory.WithWebHostBuilder(b => b
            .UseSetting("Database:ConnectionString", postgres.ConnectionString)
            .UseSetting("Jwt:SigningKey", "unit-test-signing-key-0123456789-abcdef")
            .UseSetting("Security:FieldEncryptionKey", Convert.ToBase64String(FieldKey))
            .UseSetting("RateLimit:PerMinute", "10000"));
    }

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private sealed record Fixture(Guid UserId, string Username, string R1, Guid S1, Guid S2);

    /// <summary>U_OK with session S1 (access A1, refresh R1) and session S2 (one access, one refresh).</summary>
    private async Task<Fixture> SessionAsync()
    {
        var registration = Registration();
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var userId = await new RegistrationRepository(db, _protector).AddAsync(registration, CancellationToken.None);
        var r1 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var (s1, s2) = (Guid.NewGuid(), Guid.NewGuid());
        await TokenAsync("authen.t_user_refresh_token", userId, s1, Hash(r1), 60 * 24);
        await TokenAsync("authen.t_user_access_token", userId, s1, RandomNumberGenerator.GetBytes(32), -1);
        await TokenAsync("authen.t_user_refresh_token", userId, s2, RandomNumberGenerator.GetBytes(32), 60 * 24);
        await TokenAsync("authen.t_user_access_token", userId, s2, RandomNumberGenerator.GetBytes(32), 15);
        return new Fixture(userId, registration.Username, r1, s1, s2);
    }

    private Task TokenAsync(string table, Guid userId, Guid sessionId, byte[] hash, int expiresInMinutes) =>
        ExecuteAsync(_postgres.ConnectionString,
            $"INSERT INTO {table} (created_by, user_id, session_id, token, expired_at) VALUES ('test', @u, @s, @t, @x)",
            ("u", userId), ("s", sessionId), ("t", hash), ("x", DateTime.UtcNow.AddMinutes(expiresInMinutes)));

    private Task<HttpResponseMessage> Send(object body, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        request.Headers.Add("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        return (client ?? _factory.CreateClient()).SendAsync(request);
    }

    private static async Task<JsonElement> Status(HttpResponseMessage response, int code, string? bizCode, string? bizMessage)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement.Clone();
        Assert.Equal(code, (int)response.StatusCode);
        var status = root.GetProperty("status");
        if (bizCode is null) Assert.False(status.TryGetProperty("bizCode", out _));
        else Assert.Equal(bizCode, status.GetProperty("bizCode").GetString());
        if (bizMessage is not null) Assert.Equal(bizMessage, status.GetProperty("bizMessage").GetString());
        if (code != 200) Assert.False(root.TryGetProperty("data", out _));
        return root;
    }

    private async Task<string> Rotate(string token) =>
        (await Status(await Send(new { refreshAccessToken = token }), 200, "S10000", "Refresh Token Success"))
            .GetProperty("data").GetProperty("refreshAccessToken").GetString()!;

    private async Task<(int Active, int Total)> Count(DbSet<RefreshTokenRecord> set, Guid sessionId) =>
        (await set.CountAsync(t => t.SessionId == sessionId && t.RowStatus == "ACTIVE"), await set.CountAsync(t => t.SessionId == sessionId));

    private async Task<(int Active, int Total)> CountAccess(Guid sessionId)
    {
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        return (await db.AccessTokens.CountAsync(t => t.SessionId == sessionId && t.RowStatus == "ACTIVE"), await db.AccessTokens.CountAsync(t => t.SessionId == sessionId));
    }

    private async Task<(int Active, int Total)> CountRefresh(Guid sessionId)
    {
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        return await Count(db.RefreshTokens, sessionId);
    }

    private static JsonElement Payload(string jwt)
    {
        var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        return JsonDocument.Parse(Convert.FromBase64String(part.PadRight(part.Length + (4 - part.Length % 4) % 4, '='))).RootElement.Clone();
    }

    [Fact]
    public async Task TC_REFRESH_001_rotation_in_the_same_session_without_an_access_token()
    {
        var f = await SessionAsync();

        var root = await Status(await Send(new { refreshAccessToken = f.R1 }), 200, "S10000", "Refresh Token Success");

        Assert.Equal("Ok", root.GetProperty("status").GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(["tokenType", "accessToken", "refreshAccessToken", "expiresIn"], data.EnumerateObject().Select(p => p.Name));
        Assert.Equal(("Bearer", 900), (data.GetProperty("tokenType").GetString(), data.GetProperty("expiresIn").GetInt32()));
        var r2 = data.GetProperty("refreshAccessToken").GetString()!;
        Assert.NotEqual(f.R1, r2);
        Assert.Equal(f.UserId.ToString(), Payload(data.GetProperty("accessToken").GetString()!).GetProperty("sub").GetString());
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var old = await db.RefreshTokens.SingleAsync(t => t.Token == Hash(f.R1));
        Assert.Equal(("INACTIVE", f.UserId.ToString()), (old.RowStatus, old.UpdatedBy));
        var fresh = await db.RefreshTokens.SingleAsync(t => t.Token == Hash(r2));
        Assert.Equal(("ACTIVE", f.S1), (fresh.RowStatus, fresh.SessionId));
        Assert.Equal((2, 2), await CountAccess(f.S1)); // A1 stays ACTIVE
        Assert.Equal((1, 2), await CountRefresh(f.S1));
        Assert.Equal((1, 1), await CountRefresh(f.S2));
    }

    [Fact]
    public async Task TC_REFRESH_002_claims_are_resolved_again()
    {
        var f = await SessionAsync();
        var s = _postgres.ConnectionString;
        var tag = Guid.NewGuid().ToString("N")[..6];
        var roleA = await AuthorAsync(s, "author.m_role", "name", "R_A_" + tag);
        var roleB = await AuthorAsync(s, "author.m_role", "name", "R_B_" + tag);
        var catalog = await AuthorAsync(s, "author.m_permission", "name", $"catalog{tag}.manage");
        var order = await AuthorAsync(s, "author.m_permission", "name", $"order{tag}.view");
        await LinkAsync(s, "author.t_user_role", ("user_id", f.UserId), ("role_id", roleA), "INACTIVE");
        await LinkAsync(s, "author.t_role_permission", ("role_id", roleA), ("permission_id", catalog));
        await LinkAsync(s, "author.t_user_role", ("user_id", f.UserId), ("role_id", roleB));
        await LinkAsync(s, "author.t_role_permission", ("role_id", roleB), ("permission_id", order));

        var root = await Status(await Send(new { refreshAccessToken = f.R1 }), 200, "S10000", null);

        var payload = Payload(root.GetProperty("data").GetProperty("accessToken").GetString()!);
        Assert.Equal(["R_B_" + tag], payload.GetProperty("roles").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal([$"order{tag}:view"], payload.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(f.Username, payload.GetProperty("user").GetProperty("username").GetString());
    }

    [Fact]
    public async Task TC_REFRESH_003_a_missing_token_is_400()
    {
        await Status(await Send(new { }), 400, "E10001", "Refresh Access Token is a required field.");
        await Status(await Send(new { refreshAccessToken = "" }), 400, "E10001", "Refresh Access Token is a required field.");
    }

    [Fact]
    public async Task TC_REFRESH_004_an_unknown_or_expired_token_is_401_and_changes_nothing()
    {
        var f = await SessionAsync();
        await Status(await Send(new { refreshAccessToken = "unknown" }), 401, "E10002", Invalid);
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.t_user_refresh_token SET expired_at = @x WHERE token = @t",
            ("x", DateTime.UtcNow.AddSeconds(-1)), ("t", Hash(f.R1)));
        await Status(await Send(new { refreshAccessToken = f.R1 }), 401, "E10002", Invalid);
        Assert.Equal((1, 1), await CountRefresh(f.S1));
    }

    [Fact]
    public async Task TC_REFRESH_005_a_user_that_is_not_active_is_401_and_the_token_is_not_consumed()
    {
        var f = await SessionAsync();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'LOCKED' WHERE id = @id", ("id", f.UserId));
        await Status(await Send(new { refreshAccessToken = f.R1 }), 401, "E10002", Invalid);
        Assert.Equal((1, 1), await CountRefresh(f.S1));
    }

    [Fact]
    public async Task TC_REFRESH_006_a_replay_inside_the_grace_window_keeps_the_rotated_session()
    {
        var f = await SessionAsync();
        var r2 = await Rotate(f.R1);

        await Status(await Send(new { refreshAccessToken = f.R1 }), 401, "E10002", Invalid);

        Assert.Equal((1, 2), await CountRefresh(f.S1));
        Assert.Equal((2, 2), await CountAccess(f.S1));
        await Rotate(r2);
    }

    [Fact]
    public async Task TC_REFRESH_007_a_replay_after_the_grace_window_closes_the_session()
    {
        var f = await SessionAsync();
        var r2 = await Rotate(f.R1);
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.t_user_refresh_token SET updated_at = updated_at - INTERVAL '10 seconds' WHERE token = @t", ("t", Hash(f.R1)));

        await Status(await Send(new { refreshAccessToken = f.R1 }), 401, "E10002", Invalid);

        Assert.Equal(0, (await CountRefresh(f.S1)).Active);
        Assert.Equal(0, (await CountAccess(f.S1)).Active);
        Assert.Equal((1, 1), await CountRefresh(f.S2));
        Assert.Equal((1, 1), await CountAccess(f.S2));
        await Status(await Send(new { refreshAccessToken = r2 }), 401, "E10002", Invalid);
    }

    [Fact]
    public async Task TC_REFRESH_007_an_inactive_token_without_updated_at_closes_the_session()
    {
        var f = await SessionAsync();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.t_user_refresh_token SET row_status = 'INACTIVE' WHERE token = @t", ("t", Hash(f.R1)));

        await Status(await Send(new { refreshAccessToken = f.R1 }), 401, "E10002", Invalid);

        Assert.Equal(0, (await CountAccess(f.S1)).Active);
        Assert.Equal((1, 1), await CountAccess(f.S2));
    }

    [Fact]
    public async Task TC_REFRESH_008_concurrent_refreshes_give_one_200_and_keep_the_rotated_session()
    {
        var f = await SessionAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Send(new { refreshAccessToken = f.R1 })));

        Assert.Equal([200, 401, 401], responses.Select(r => (int)r.StatusCode).Order());
        Assert.Equal((1, 2), await CountRefresh(f.S1));
    }

    [Fact]
    public async Task TC_REFRESH_009_the_user_row_lock_makes_a_slow_request_time_out_without_changes()
    {
        var f = await SessionAsync();
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
            await Status(await Send(new { refreshAccessToken = f.R1 }, client), 408, "E90001", "The server timed out waiting for the request.");
            Assert.Equal((1, 1), await CountRefresh(f.S1));
            await transaction.RollbackAsync();
        }

        await Rotate(f.R1);
    }

    private sealed class FailingLogins(ILoginRepository inner) : ILoginRepository
    {
        public Task LockIdentifierAsync(string i, CancellationToken ct) => inner.LockIdentifierAsync(i, ct);
        public Task<int> CountRecentFailuresAsync(string i, DateTime w, CancellationToken ct) => inner.CountRecentFailuresAsync(i, w, ct);
        public Task<IReadOnlyList<LoginUser>> LockActiveUsersAsync(string i, CancellationToken ct) => inner.LockActiveUsersAsync(i, ct);
        public Task<PasswordCredential?> GetCurrentPasswordAsync(Guid u, CancellationToken ct) => inner.GetCurrentPasswordAsync(u, ct);
        public Task<AuthorizationFacts> GetAuthorizationFactsAsync(Guid u, CancellationToken ct) => inner.GetAuthorizationFactsAsync(u, ct);
        public Task<IReadOnlyList<ScopeFact>> GetActiveScopesAsync(IReadOnlyList<Guid> ids, CancellationToken ct) => inner.GetActiveScopesAsync(ids, ct);
        public async Task AddSessionTokensAsync(LoginSession s, CancellationToken ct)
        {
            await inner.AddSessionTokensAsync(s, ct);
            throw new InvalidOperationException("injected after the token inserts");
        }
        public Task RecordAttemptAsync(string i, Guid? u, LoginResult r, DateTime n, CancellationToken ct) => inner.RecordAttemptAsync(i, u, r, n, ct);
    }

    [Fact]
    public async Task TC_REFRESH_010_an_unexpected_failure_is_500_and_rolls_back()
    {
        var f = await SessionAsync();
        var client = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddScoped<LoginRepository>();
            s.AddScoped<ILoginRepository>(p => new FailingLogins(p.GetRequiredService<LoginRepository>()));
        })).CreateClient();

        await Status(await Send(new { refreshAccessToken = f.R1 }, client), 500, null, "An unexpected error occurred.");

        Assert.Equal((1, 1), await CountRefresh(f.S1));
        Assert.Equal((1, 1), await CountAccess(f.S1));
    }

    [Fact]
    public async Task TC_REFRESH_011_the_sixty_first_request_in_a_minute_is_429()
    {
        var client = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimit:PerMinute", "60")).CreateClient();
        for (var i = 0; i < 60; i++) await Send(new { refreshAccessToken = "x" }, client);

        var response = await Send(new { refreshAccessToken = "x" }, client);

        await Status(response, 429, "E90002", "Too many requests. Try again later.");
        Assert.InRange(int.Parse(response.Headers.GetValues("Retry-After").Single()), 1, 60);
    }
}
