using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

// e-commerce/doc/api/authen-and-author/02-login/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class LoginApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/auth/login";
    private const string Password = "Passw0rd-Example";
    private const string SigningKey = "unit-test-signing-key-0123456789-abcdef";
    private readonly PostgresFixture _postgres;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FieldProtector _protector = new(FieldKey);

    public LoginApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        _factory = factory.WithWebHostBuilder(b => b
            .UseSetting("Database:ConnectionString", postgres.ConnectionString)
            .UseSetting("Jwt:SigningKey", SigningKey)
            .UseSetting("Security:FieldEncryptionKey", Convert.ToBase64String(FieldKey))
            .UseFieldTransportKey()
            .UseSetting("RateLimit:StrictPerMinute", "10000"));
    }

    private async Task<(Guid Id, NewRegistration Registration)> UserAsync(string? username = null, string? email = null, string? displayName = null)
    {
        var registration = Registration() with { Password = new Pbkdf2PasswordHasher().Hash(Password) };
        if (username is not null) registration = registration with { Username = username };
        if (email is not null) registration = registration with { EmailAddress = email };
        registration = registration with { DisplayName = displayName };
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        return (await new RegistrationRepository(db, _protector).AddAsync(registration, CancellationToken.None), registration);
    }

    private Task<HttpResponseMessage> Send(object body, HttpClient? client = null, string requestId = "req-test")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path);
        request.Content = new StringContent(FieldTransportClient.Seal(request, JsonSerializer.Serialize(body), "username", "password"), Encoding.UTF8, "application/json");
        request.Headers.Add("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add("requestId", requestId);
        return (client ?? _factory.CreateClient()).SendAsync(request);
    }

    private static async Task<(JsonElement Root, string Body)> Status(HttpResponseMessage response, int code, string? bizCode, string? bizMessage = null)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement.Clone();
        Assert.Equal(code, (int)response.StatusCode);
        var status = root.GetProperty("status");
        if (bizCode is null) Assert.False(status.TryGetProperty("bizCode", out _));
        else Assert.Equal(bizCode, status.GetProperty("bizCode").GetString());
        if (bizMessage is not null) Assert.Equal(bizMessage, status.GetProperty("bizMessage").GetString());
        return (root, body);
    }

    private static Task<(JsonElement Root, string Body)> Unauthorized(HttpResponseMessage response) =>
        Status(response, 401, "E10002", "Invalid username or password.");

    private static JsonElement Payload(string jwt)
    {
        var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(part)).RootElement.Clone();
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private async Task<List<LoginAttemptRecord>> Attempts(string identifier)
    {
        var hash = Hash(identifier);
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        return await db.LoginAttempts.Where(a => a.IdentifierHash == hash).OrderBy(a => a.CreatedAt).ToListAsync();
    }

    private async Task<int> TokenRows(Guid userId)
    {
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        return await db.AccessTokens.CountAsync(t => t.UserId == userId) + await db.RefreshTokens.CountAsync(t => t.UserId == userId);
    }

    private async Task<string[]> GrantAsync(Guid userId, string role, string permission)
    {
        var roleId = await AuthorAsync(_postgres.ConnectionString, "author.m_role", "name", role);
        var permissionId = await AuthorAsync(_postgres.ConnectionString, "author.m_permission", "name", permission);
        await LinkAsync(_postgres.ConnectionString, "author.t_user_role", ("user_id", userId), ("role_id", roleId));
        await LinkAsync(_postgres.ConnectionString, "author.t_role_permission", ("role_id", roleId), ("permission_id", permissionId));
        return [role, permission];
    }

    [Fact]
    public async Task TC_LOGIN_001_successful_login()
    {
        var (userId, r) = await UserAsync(displayName: "John Doe");
        var role = "CATALOG_MANAGER_" + Guid.NewGuid().ToString("N")[..6];
        var permission = "catalog" + Guid.NewGuid().ToString("N")[..6] + ".manage";
        await GrantAsync(userId, role, permission);
        var before = DateTime.UtcNow;

        var (root, _) = await Status(await Send(new { username = r.Username, password = Password }, requestId: "req-TC-LOGIN-001"), 200, "S10000", "Logged-in Success");

        var status = root.GetProperty("status");
        Assert.Equal(("Ok", "SUCCESS", "req-TC-LOGIN-001"),
            (status.GetProperty("message").GetString(), status.GetProperty("status").GetString(), status.GetProperty("requestId").GetString()));
        var data = root.GetProperty("data");
        Assert.Equal(["tokenType", "accessToken", "refreshAccessToken", "expiresIn"], data.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Bearer", data.GetProperty("tokenType").GetString());
        Assert.Equal(900, data.GetProperty("expiresIn").GetInt32());
        var access = data.GetProperty("accessToken").GetString()!;
        var refresh = data.GetProperty("refreshAccessToken").GetString()!;

        var validation = await new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().ValidateTokenAsync(access, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidIssuer = "ecommerce",
            ValidAudience = "ecommerce-api",
            ValidAlgorithms = ["HS256"],
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey))
        });
        Assert.True(validation.IsValid);
        var payload = Payload(access);
        Assert.Equal(userId.ToString(), payload.GetProperty("sub").GetString());
        Assert.True(Guid.TryParse(payload.GetProperty("jti").GetString(), out _));
        Assert.Equal(900, payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64());
        var user = payload.GetProperty("user");
        Assert.Equal((userId.ToString(), r.Username, "John Doe", r.EmailAddress),
            (user.GetProperty("id").GetString(), user.GetProperty("username").GetString(), user.GetProperty("displayName").GetString(), user.GetProperty("email").GetString()));
        Assert.Equal([role], payload.GetProperty("roles").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal([Ecommerce.Domain.Authen.Domain.AuthorizationClaims.ClaimName(permission)], payload.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()));
        Assert.Empty(payload.GetProperty("scopes").EnumerateArray());
        Assert.False(payload.TryGetProperty("password", out _));

        Assert.Equal(32, Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(refresh).Length);
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var accessRow = await db.AccessTokens.SingleAsync(t => t.UserId == userId);
        var refreshRow = await db.RefreshTokens.SingleAsync(t => t.UserId == userId);
        Assert.Equal(accessRow.SessionId, refreshRow.SessionId);
        Assert.Equal(Hash(access), accessRow.Token);
        Assert.Equal(Hash(refresh), refreshRow.Token);
        Assert.Equal(userId.ToString(), accessRow.CreatedBy);
        Assert.InRange(accessRow.ExpiredAt, before.AddMinutes(15).AddSeconds(-5), DateTime.UtcNow.AddMinutes(15).AddSeconds(5));
        Assert.InRange(refreshRow.ExpiredAt, before.AddDays(14).AddSeconds(-5), DateTime.UtcNow.AddDays(14).AddSeconds(5));
        var attempt = Assert.Single(await Attempts(r.Username));
        Assert.Equal((userId, "SUCCESS", "system"), (attempt.UserId!.Value, attempt.Result, attempt.CreatedBy));
    }

    [Fact]
    public async Task TC_LOGIN_002_missing_fields_are_400_and_not_attempts()
    {
        var username = "missing." + Guid.NewGuid();
        await Status(await Send(new { password = Password }), 400, "E10001", "Username is a required field.");
        await Status(await Send(new { username }), 400, "E10001", "Password is a required field.");
        await Status(await Send(new { }), 400, "E10001", "Username and Password are required fields.");
        await Status(await Send(new { username = "", password = Password }), 400, "E10001", "Username is a required field.");
        Assert.Empty(await Attempts(username));
    }

    [Fact]
    public async Task TC_LOGIN_003_length_limits()
    {
        var tooLong = new string('u', 255);
        await Status(await Send(new { username = tooLong, password = Password }), 400, "E10001", "Username must be at most 254 characters.");
        await Status(await Send(new { username = "x." + Guid.NewGuid(), password = new string('p', 129) }), 400, "E10001", "Password must be at most 128 characters.");
        Assert.Empty(await Attempts(tooLong));
        var max = Guid.NewGuid().ToString("N") + new string('u', 254 - 32);
        await Unauthorized(await Send(new { username = max, password = Password }));
        Assert.Single(await Attempts(max));
    }

    [Fact]
    public async Task TC_LOGIN_004_no_password_complexity_at_login()
    {
        var (_, r) = await UserAsync();
        await Unauthorized(await Send(new { username = r.Username, password = "abc" }));
        Assert.Equal("FAILED", Assert.Single(await Attempts(r.Username)).Result);
    }

    [Fact]
    public async Task TC_LOGIN_005_unknown_username()
    {
        var username = "ghost." + Guid.NewGuid();
        var (root, _) = await Unauthorized(await Send(new { username, password = Password }));
        Assert.False(root.TryGetProperty("data", out _));
        var attempt = Assert.Single(await Attempts(username));
        Assert.Equal(("FAILED", (Guid?)null), (attempt.Result, attempt.UserId));
    }

    [Fact]
    public async Task TC_LOGIN_006_wrong_password_commits_FAILED_with_the_user_and_no_token()
    {
        var (userId, r) = await UserAsync();
        await Unauthorized(await Send(new { username = r.Username, password = "Wrong-Passw0rd" }));
        var attempt = Assert.Single(await Attempts(r.Username));
        Assert.Equal(("FAILED", (Guid?)userId), (attempt.Result, attempt.UserId));
        Assert.Equal(0, await TokenRows(userId));
    }

    [Theory]
    [InlineData("INACTIVE")]
    [InlineData("LOCKED")]
    [InlineData("DELETED")]
    public async Task TC_LOGIN_007_a_user_that_is_not_ACTIVE_cannot_log_in(string rowStatus)
    {
        var (userId, r) = await UserAsync();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user SET row_status = @s WHERE id = @id", ("s", rowStatus), ("id", userId));
        await Unauthorized(await Send(new { username = r.Username, password = Password }));
        Assert.Null(Assert.Single(await Attempts(r.Username)).UserId);
        Assert.Equal(0, await TokenRows(userId));
    }

    [Fact]
    public async Task TC_LOGIN_008_every_failure_has_the_same_body()
    {
        var (_, wrong) = await UserAsync();
        var (noPasswordId, noPassword) = await UserAsync();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user_password SET row_status = 'INACTIVE' WHERE user_id = @id", ("id", noPasswordId));
        var bodies = new List<string>();
        foreach (var body in new object[]
        {
            new { username = "ghost." + Guid.NewGuid(), password = Password },
            new { username = wrong.Username, password = "Wrong-Passw0rd" },
            new { username = noPassword.Username, password = Password }
        })
        {
            var (_, raw) = await Unauthorized(await Send(body, requestId: "req-same"));
            bodies.Add(System.Text.RegularExpressions.Regex.Replace(raw, "\"timestamp\":\"[^\"]*\"", ""));
        }

        Assert.Single(bodies.Distinct());
    }

    [Fact]
    public async Task TC_LOGIN_009_no_active_PASSWORD_row()
    {
        var (userId, r) = await UserAsync();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user_password SET row_status = 'INACTIVE' WHERE user_id = @id", ("id", userId));
        await Unauthorized(await Send(new { username = r.Username, password = Password }));
        Assert.Equal(userId, Assert.Single(await Attempts(r.Username)).UserId);
    }

    [Fact]
    public async Task TC_LOGIN_010_an_ambiguous_identifier_chooses_no_account()
    {
        var shared = $"x.{Guid.NewGuid():N}@example.test";
        var (a, _) = await UserAsync(username: shared);
        var (b, _) = await UserAsync(email: shared);

        await Unauthorized(await Send(new { username = shared, password = Password }));

        Assert.Null(Assert.Single(await Attempts(shared)).UserId);
        Assert.Equal(0, await TokenRows(a) + await TokenRows(b));
    }

    [Fact]
    public async Task TC_LOGIN_011_the_identifier_is_compared_exactly()
    {
        var (_, r) = await UserAsync();
        await Unauthorized(await Send(new { username = r.Username.ToUpperInvariant(), password = Password }));
        await Unauthorized(await Send(new { username = r.Username + " ", password = Password }));
    }

    [Fact]
    public async Task TC_LOGIN_012_login_by_email_address()
    {
        var (_, r) = await UserAsync();
        await Status(await Send(new { username = r.EmailAddress, password = Password }), 200, "S10000");
        Assert.Equal("SUCCESS", Assert.Single(await Attempts(r.EmailAddress)).Result);
    }

    [Fact]
    public async Task TC_LOGIN_013_five_failures_refuse_the_sixth_even_with_the_right_password()
    {
        var (userId, r) = await UserAsync();
        for (var i = 0; i < 5; i++) await Unauthorized(await Send(new { username = r.Username, password = "Wrong-Passw0rd" }));

        await Unauthorized(await Send(new { username = r.Username, password = Password }));
        await Unauthorized(await Send(new { username = r.Username, password = Password }));

        var attempts = await Attempts(r.Username);
        Assert.Equal(5, attempts.Count(a => a.Result == "FAILED"));
        Assert.Equal(2, attempts.Count(a => a.Result == "REFUSED" && a.UserId == null));
        Assert.Equal(0, await TokenRows(userId));
    }

    private Task FailedAttemptsAsync(string identifier, int count, int minutesAgo) =>
        Task.WhenAll(Enumerable.Range(0, count).Select(_ => ExecuteAsync(_postgres.ConnectionString,
            "INSERT INTO authen.l_user_login_attempt (created_by, created_at, identifier_hash, result) VALUES ('test', @at, @hash, 'FAILED')",
            ("at", DateTime.UtcNow.AddMinutes(-minutesAgo)), ("hash", Hash(identifier)))));

    [Fact]
    public async Task TC_LOGIN_014_failures_older_than_the_window_do_not_count()
    {
        var (_, old) = await UserAsync();
        await FailedAttemptsAsync(old.Username, 5, 16);
        await Status(await Send(new { username = old.Username, password = Password }), 200, "S10000");

        var (_, recent) = await UserAsync();
        await FailedAttemptsAsync(recent.Username, 4, 14);
        await Status(await Send(new { username = recent.Username, password = Password }), 200, "S10000");
    }

    [Fact]
    public async Task TC_LOGIN_015_the_counter_is_per_supplied_identifier()
    {
        var (_, r) = await UserAsync();
        await FailedAttemptsAsync(r.Username, 5, 1);

        await Unauthorized(await Send(new { username = r.Username, password = Password }));
        await Status(await Send(new { username = r.EmailAddress, password = Password }), 200, "S10000");
    }

    [Fact]
    public async Task TC_LOGIN_016_concurrent_attempts_cannot_pass_a_stale_count()
    {
        var (_, r) = await UserAsync();
        await FailedAttemptsAsync(r.Username, 4, 1);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Send(new { username = r.Username, password = "Wrong-Passw0rd" })));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        var attempts = await Attempts(r.Username);
        Assert.Equal(5, attempts.Count(a => a.Result == "FAILED"));
        Assert.Equal(4, attempts.Count(a => a.Result == "REFUSED"));
    }

    [Fact]
    public async Task TC_LOGIN_017_authorization_claims_in_the_token()
    {
        var s = _postgres.ConnectionString;
        var (userId, r) = await UserAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        string Name(string prefix) => prefix + tag;
        var roleA = await AuthorAsync(s, "author.m_role", "name", Name("A_ROLE_"));
        var roleB = await AuthorAsync(s, "author.m_role", "name", Name("B_ROLE_"));
        var roleX = await AuthorAsync(s, "author.m_role", "name", Name("X_ROLE_"));
        var roleY = await AuthorAsync(s, "author.m_role", "name", Name("Y_ROLE_"), "INACTIVE");
        await LinkAsync(s, "author.t_user_role", ("user_id", userId), ("role_id", roleA));
        await LinkAsync(s, "author.t_user_role", ("user_id", userId), ("role_id", roleB));
        await LinkAsync(s, "author.t_user_role", ("user_id", userId), ("role_id", roleX), "INACTIVE");
        await LinkAsync(s, "author.t_user_role", ("user_id", userId), ("role_id", roleY));
        var catalog = await AuthorAsync(s, "author.m_permission", "name", $"catalog{tag}.manage");
        var shop = await AuthorAsync(s, "author.m_permission", "name", $"shop{tag}.product.manage");
        var order = await AuthorAsync(s, "author.m_permission", "name", $"order{tag}.view");
        var report = await AuthorAsync(s, "author.m_permission", "name", $"report{tag}.view");
        var audit = await AuthorAsync(s, "author.m_permission", "name", $"audit{tag}.view", "INACTIVE");
        var hidden = await AuthorAsync(s, "author.m_permission", "name", $"hidden{tag}.view");
        var own = await ScopeAsync(s, "OWN" + tag);
        var all = await ScopeAsync(s, "ALL" + tag);
        var shopOwn = await LinkAsync(s, "author.t_permission_scope", ("permission_id", shop), ("scope_id", own));
        await LinkAsync(s, "author.t_permission_scope", ("permission_id", shop), ("scope_id", all));
        await LinkAsync(s, "author.t_permission_scope", ("permission_id", report), ("scope_id", all));
        await LinkAsync(s, "author.t_role_permission", ("role_id", roleA), ("permission_id", catalog));
        await LinkAsync(s, "author.t_role_permission", ("role_id", roleA), ("permission_id", report));
        var bShop = await LinkAsync(s, "author.t_role_permission", ("role_id", roleB), ("permission_id", shop));
        await LinkAsync(s, "author.t_role_permission_scope", ("role_permission_id", bShop), ("permission_scope_id", shopOwn));
        await LinkAsync(s, "author.t_role_permission", ("role_id", roleX), ("permission_id", hidden));
        await LinkAsync(s, "author.t_role_permission", ("role_id", roleY), ("permission_id", hidden));
        await LinkAsync(s, "author.t_user_permission", ("user_id", userId), ("permission_id", order));
        await LinkAsync(s, "author.t_user_permission", ("user_id", userId), ("permission_id", audit));
        await LinkAsync(s, "author.t_user_permission", ("user_id", userId), ("permission_id", catalog));

        var (root, _) = await Status(await Send(new { username = r.Username, password = Password }), 200, "S10000");

        var payload = Payload(root.GetProperty("data").GetProperty("accessToken").GetString()!);
        Assert.Equal([Name("A_ROLE_"), Name("B_ROLE_")], payload.GetProperty("roles").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal([$"catalog{tag}:manage", $"order{tag}:view", $"shop{tag}.product:manage"], payload.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal([$"own{tag}:*"], payload.GetProperty("scopes").EnumerateArray().Select(e => e.GetString()));

        var (_, plain) = await UserAsync();
        var empty = Payload((await Status(await Send(new { username = plain.Username, password = Password }), 200, "S10000")).Root.GetProperty("data").GetProperty("accessToken").GetString()!);
        Assert.Empty(empty.GetProperty("roles").EnumerateArray());
        Assert.Empty(empty.GetProperty("permissions").EnumerateArray());
        Assert.Empty(empty.GetProperty("scopes").EnumerateArray());
    }

    [Fact]
    public async Task TC_LOGIN_018_each_login_is_a_new_session()
    {
        var (userId, r) = await UserAsync();
        var first = Payload((await Status(await Send(new { username = r.Username, password = Password }), 200, "S10000")).Root.GetProperty("data").GetProperty("accessToken").GetString()!);
        var second = Payload((await Status(await Send(new { username = r.Username, password = Password }), 200, "S10000")).Root.GetProperty("data").GetProperty("accessToken").GetString()!);

        Assert.NotEqual(first.GetProperty("jti").GetString(), second.GetProperty("jti").GetString());
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        Assert.Equal(2, await db.AccessTokens.Where(t => t.UserId == userId).Select(t => t.SessionId).Distinct().CountAsync());
        Assert.Equal(2, await db.RefreshTokens.Where(t => t.UserId == userId).Select(t => t.SessionId).Distinct().CountAsync());
    }

    [Fact]
    public async Task TC_LOGIN_019_login_waits_for_the_user_row_lock_and_times_out_without_writes()
    {
        var (userId, r) = await UserAsync();
        await using (var blocker = new NpgsqlConnection(_postgres.ConnectionString))
        {
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            await using (var lockRow = new NpgsqlCommand("SELECT id FROM authen.m_user WHERE id = @id FOR UPDATE", blocker, transaction))
            {
                lockRow.Parameters.AddWithValue("id", userId);
                await lockRow.ExecuteScalarAsync();
            }

            var client = _factory.WithWebHostBuilder(b => b.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();
            await Status(await Send(new { username = r.Username, password = Password }, client), 408, "E90001", "The server timed out waiting for the request.");
            Assert.Equal(0, await TokenRows(userId));
            Assert.Empty(await Attempts(r.Username));
            await transaction.RollbackAsync();
        }

        await Status(await Send(new { username = r.Username, password = Password }), 200, "S10000");
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
    public async Task TC_LOGIN_020_an_unexpected_failure_is_500_and_rolls_back()
    {
        var (userId, r) = await UserAsync();
        var client = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddScoped<LoginRepository>();
            services.AddScoped<ILoginRepository>(p => new FailingLogins(p.GetRequiredService<LoginRepository>()));
        })).CreateClient();

        var (root, _) = await Status(await Send(new { username = r.Username, password = Password }, client), 500, null, "An unexpected error occurred.");

        Assert.False(root.TryGetProperty("data", out _));
        Assert.Equal(0, await TokenRows(userId));
        Assert.Empty(await Attempts(r.Username));
    }

    [Fact]
    public async Task TC_LOGIN_021_the_eleventh_request_in_a_minute_is_429_and_not_an_attempt()
    {
        var username = "limit." + Guid.NewGuid();
        var client = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimit:StrictPerMinute", "10")).CreateClient();
        for (var i = 0; i < 10; i++) await Send(new { username, password = "Wrong-Passw0rd" }, client);

        var response = await Send(new { username, password = "Wrong-Passw0rd" }, client);

        await Status(response, 429, "E90002", "Too many requests. Try again later.");
        Assert.InRange(int.Parse(response.Headers.GetValues("Retry-After").Single()), 1, 60);
        Assert.Equal(10, (await Attempts(username)).Count);
    }
}
