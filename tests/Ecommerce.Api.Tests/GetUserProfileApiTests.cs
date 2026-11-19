using System.Globalization;
using System.Net.Http.Headers;
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
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/authen-and-author/08-inquiry-user-profile/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class GetUserProfileApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/user/profile";
    private const string Invalid = "Invalid or expired access token.";
    private const string RequestShape = "Timestamp must be valid UTC and this endpoint accepts no query parameters or body.";
    private readonly PostgresFixture _postgres;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FieldProtector _protector = new(FieldKey);

    public GetUserProfileApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        _factory = factory.WithWebHostBuilder(b => b
            .UseSetting("Database:ConnectionString", postgres.ConnectionString)
            .UseSetting("Jwt:SigningKey", "unit-test-signing-key-0123456789-abcdef")
            .UseSetting("Security:FieldEncryptionKey", Convert.ToBase64String(FieldKey)));
    }

    private async Task<(Guid UserId, string Email, string Jwt)> CallerAsync(bool full)
    {
        var registration = Registration(full: full);
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var userId = await new RegistrationRepository(db, _protector).AddAsync(registration, CancellationToken.None);
        if (!full) await ExecuteAsync(_postgres.ConnectionString, "DELETE FROM authen.m_user_information WHERE user_id = @u", ("u", userId));
        var jwt = UpsertUserProfileApiTests.Jwt(userId.ToString());
        await ExecuteAsync(_postgres.ConnectionString,
            "INSERT INTO authen.t_user_access_token (created_by, user_id, session_id, token, expired_at) VALUES ('test', @u, @s, @t, @x)",
            ("u", userId), ("s", Guid.NewGuid()), ("t", SHA256.HashData(Encoding.UTF8.GetBytes(jwt))), ("x", DateTime.UtcNow.AddMinutes(15)));
        return (userId, registration.EmailAddress, jwt);
    }

    private Task<HttpResponseMessage> Send(string? jwt, string query = "", string? body = null, bool timestamp = true, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Path + query);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (jwt is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        if (timestamp) request.Headers.Add("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        return (client ?? _factory.CreateClient()).SendAsync(request);
    }

    private static async Task<JsonElement> Status(HttpResponseMessage response, int code, string? bizCode, string? bizMessage)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement.Clone();
        Assert.True(code == (int)response.StatusCode, $"{(int)response.StatusCode}: {root}");
        var status = root.GetProperty("status");
        if (bizCode is null) Assert.False(status.TryGetProperty("bizCode", out _));
        else Assert.Equal(bizCode, status.GetProperty("bizCode").GetString());
        if (bizMessage is not null) Assert.Equal(bizMessage, status.GetProperty("bizMessage").GetString());
        if (code != 200) Assert.False(root.TryGetProperty("data", out _));
        return root;
    }

    private static readonly string[] DataFields =
        ["userId", "emailAddress", "firstName", "lastName", "displayName", "profilePicturePath", "phoneNumber", "address", "dateOfBirth", "gender"];

    [Fact]
    public async Task TC_PROFILEQ_001_the_full_profile()
    {
        var (userId, email, jwt) = await CallerAsync(full: true);

        var data = (await Status(await Send(jwt), 200, "S10000", "Inquiry User Profile Success")).GetProperty("data");

        Assert.Equal(DataFields, data.EnumerateObject().Select(p => p.Name));
        Assert.Equal(userId, data.GetProperty("userId").GetGuid());
        Assert.Equal((email, "John", "Doe", "John Doe", "/profiles/a.png", "0812345678", "123 Road", "1990-01-31", "MALE"),
            (data.GetProperty("emailAddress").GetString(), data.GetProperty("firstName").GetString(), data.GetProperty("lastName").GetString(),
             data.GetProperty("displayName").GetString(), data.GetProperty("profilePicturePath").GetString(), data.GetProperty("phoneNumber").GetString(),
             data.GetProperty("address").GetString(), data.GetProperty("dateOfBirth").GetString(), data.GetProperty("gender").GetString()));
    }

    [Fact]
    public async Task TC_PROFILEQ_002_unset_fields_are_present_as_null()
    {
        var (userId, _, jwt) = await CallerAsync(full: false);
        var data = (await Status(await Send(jwt), 200, "S10000", null)).GetProperty("data");
        Assert.Equal(DataFields, data.EnumerateObject().Select(p => p.Name));
        Assert.All(DataFields[2..], f => Assert.Equal(JsonValueKind.Null, data.GetProperty(f).ValueKind));

        await ExecuteAsync(_postgres.ConnectionString, "INSERT INTO authen.m_user_information (created_by, user_id, gender) VALUES ('test', @u, '')", ("u", userId));
        data = (await Status(await Send(jwt), 200, "S10000", null)).GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("gender").ValueKind);
    }

    [Fact]
    public async Task TC_PROFILEQ_003_no_query_and_no_body_before_the_credential()
    {
        var (otherId, _, jwt) = await CallerAsync(full: false);
        foreach (var token in new[] { jwt, null })
        {
            await Status(await Send(token, query: $"?userId={otherId}"), 400, "E10001", RequestShape);
            await Status(await Send(token, body: "{}"), 400, "E10001", RequestShape);
        }

        await Status(await Send(jwt, timestamp: false), 400, "E10001", "Timestamp is a required header.");
    }

    [Fact]
    public async Task TC_PROFILEQ_004_credential_failures()
    {
        var (userId, _, jwt) = await CallerAsync(full: false);
        await Status(await Send(null), 401, "E10002", Invalid);
        await Status(await Send(UpsertUserProfileApiTests.Jwt(userId.ToString(), "another-signing-key-0123456789-abcdef")), 401, "E10002", Invalid);
        await Status(await Send(UpsertUserProfileApiTests.Jwt(userId.ToString())), 401, "E10002", Invalid);
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.t_user_access_token SET expired_at = @x WHERE user_id = @u", ("x", DateTime.UtcNow.AddSeconds(-1)), ("u", userId));
        await Status(await Send(jwt), 401, "E10002", Invalid);
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.t_user_access_token SET expired_at = @x, row_status = 'INACTIVE' WHERE user_id = @u", ("x", DateTime.UtcNow.AddMinutes(5)), ("u", userId));
        await Status(await Send(jwt), 401, "E10002", Invalid);

        var (lockedId, _, lockedJwt) = await CallerAsync(full: false);
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'LOCKED' WHERE id = @id", ("id", lockedId));
        await Status(await Send(lockedJwt), 401, "E10002", Invalid);
    }

    [Fact]
    public async Task TC_PROFILEQ_005_the_read_takes_no_lock()
    {
        var (userId, _, jwt) = await CallerAsync(full: true);
        await using var blocker = new NpgsqlConnection(_postgres.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var lockRow = new NpgsqlCommand("SELECT id FROM authen.m_user WHERE id = @id FOR UPDATE", blocker, transaction))
        {
            lockRow.Parameters.AddWithValue("id", userId);
            await lockRow.ExecuteScalarAsync();
        }

        var client = _factory.WithWebHostBuilder(b => b.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();
        await Status(await Send(jwt, client: client), 200, "S10000", null);
        await transaction.RollbackAsync();
    }

    private sealed class FailingProfiles(IUserProfileQueryRepository inner) : IUserProfileQueryRepository
    {
        public Task<ProfileUser?> GetUserAsync(Guid u, byte[] h, DateTime n, CancellationToken ct) => inner.GetUserAsync(u, h, n, ct);
        public Task<ProfileInformation?> GetInformationAsync(Guid u, CancellationToken ct) => throw new InvalidOperationException("injected");
    }

    [Fact]
    public async Task TC_PROFILEQ_006_an_unexpected_failure_is_500()
    {
        var (_, _, jwt) = await CallerAsync(full: true);
        var client = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddScoped<UserProfileQueryRepository>();
            s.AddScoped<IUserProfileQueryRepository>(p => new FailingProfiles(p.GetRequiredService<UserProfileQueryRepository>()));
        })).CreateClient();

        await Status(await Send(jwt, client: client), 500, null, "An unexpected error occurred.");
    }
}
