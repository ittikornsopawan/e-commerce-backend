using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/authen-and-author/01-register/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class RegisterApiTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string Path = "/v1/auth/register";
    private readonly PostgresFixture _postgres;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FieldProtector _protector = new(FieldKey);
    private Guid _required;
    private Guid _optional;

    public RegisterApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        _factory = factory.WithWebHostBuilder(b => b
            .UseSetting("Database:ConnectionString", postgres.ConnectionString)
            .UseSetting("Jwt:SigningKey", "unit-test-signing-key-0123456789-abcdef")
            .UseSetting("Security:FieldEncryptionKey", Convert.ToBase64String(FieldKey))
            .UseSetting("RateLimit:StrictPerMinute", "10000"));
    }

    // Every case starts with exactly one applicable required consent (C_REQ) and one optional (C_OPT).
    public async Task InitializeAsync()
    {
        await RetireAllConsentsAsync(_postgres.ConnectionString);
        var type = await ConsentTypeAsync(_postgres.ConnectionString);
        _required = await ConsentAsync(_postgres.ConnectionString, type, required: true);
        _optional = await ConsentAsync(_postgres.ConnectionString, type, required: false);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private JsonObject Valid(string? suffix = null)
    {
        suffix ??= Guid.NewGuid().ToString("N")[..12];
        return new JsonObject
        {
            ["username"] = $"user.{suffix}",
            ["emailAddress"] = $"user.{suffix}@example.test",
            ["password"] = "Passw0rd-Example",
            ["consents"] = new JsonArray(Consent(_required, true))
        };
    }

    private static JsonObject Consent(Guid id, bool? result, string version = "1.0.0")
    {
        var consent = new JsonObject { ["consentId"] = id.ToString(), ["consentVersion"] = version };
        if (result is not null) consent["result"] = result;
        return consent;
    }

    private Task<HttpResponseMessage> Send(JsonNode body, string? timestamp = "now", string contentType = "application/json", string? requestId = "req-test", HttpClient? client = null) =>
        Send(body.ToJsonString(), timestamp, contentType, requestId, client);

    private Task<HttpResponseMessage> Send(string body, string? timestamp = "now", string contentType = "application/json", string? requestId = "req-test", HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent(body, Encoding.UTF8) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        if (timestamp is not null) request.Headers.Add("timestamp", timestamp == "now" ? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture) : timestamp);
        if (requestId is not null) request.Headers.Add("requestId", requestId);
        return (client ?? _factory.CreateClient()).SendAsync(request);
    }

    private static async Task<JsonElement> Status(HttpResponseMessage response, int code, string? bizCode, string? bizMessage = null)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement.Clone();
        Assert.Equal(code, (int)response.StatusCode);
        var status = root.GetProperty("status");
        Assert.Equal(code, status.GetProperty("code").GetInt32());
        if (bizCode is null) Assert.False(status.TryGetProperty("bizCode", out _));
        else Assert.Equal(bizCode, status.GetProperty("bizCode").GetString());
        if (bizMessage is not null) Assert.Equal(bizMessage, status.GetProperty("bizMessage").GetString());
        return root;
    }

    private async Task<bool> UserExists(string username)
    {
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var bytes = _protector.Protect(username);
        return await db.Users.AnyAsync(u => u.Username == bytes);
    }

    private async Task Rejected(JsonObject body, int code, string bizCode, string? bizMessage)
    {
        var response = await Send(body);
        var root = await Status(response, code, bizCode, bizMessage);
        Assert.False(root.TryGetProperty("data", out _));
        Assert.False(await UserExists(body["username"]!.GetValue<string>()));
    }

    [Fact]
    public async Task TC_REG_001_full_registration_succeeds()
    {
        var referrer = Valid();
        var referrerId = (await Status(await Send(referrer), 201, "S10000")).GetProperty("data").GetProperty("userId").GetGuid();
        var body = Valid();
        body["firstName"] = "John"; body["lastName"] = "Doe"; body["displayName"] = "John Doe";
        body["profilePicturePath"] = "/profiles/a.png"; body["phoneNumber"] = "0812345678"; body["address"] = "123 Road";
        body["dateOfBirth"] = "1990-01-31"; body["gender"] = "MALE"; body["referrerId"] = referrerId.ToString();
        body["addresses"] = new JsonObject { ["title"] = "Home", ["isDefault"] = false, ["address"] = "1 Road", ["city"] = "Watthana", ["state"] = "Bangkok", ["postalCode"] = "10110", ["country"] = "Thailand" };
        body["consents"] = new JsonArray(Consent(_required, true), Consent(_optional, false));

        var response = await Send(body, requestId: "req-TC-REG-001");

        var root = await Status(response, 201, "S10000", "Register Success");
        var status = root.GetProperty("status");
        Assert.Equal("Created", status.GetProperty("message").GetString());
        Assert.Equal("SUCCESS", status.GetProperty("status").GetString());
        Assert.Equal("req-TC-REG-001", status.GetProperty("requestId").GetString());
        var data = root.GetProperty("data");
        Assert.Single(data.EnumerateObject());
        var userId = data.GetProperty("userId").GetGuid();
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.Equal("auth-register", user.CreatedBy);
        Assert.Equal("John", _protector.Unprotect(user.FirstName!));
        Assert.Equal(1, await db.UserPasswords.CountAsync(p => p.UserId == userId && p.RowType == "PASSWORD"));
        Assert.Equal(1, await db.UserInformation.CountAsync(i => i.UserId == userId && i.Gender == "MALE"));
        Assert.True((await db.UserAddresses.SingleAsync(a => a.UserId == userId)).IsDefault);
        Assert.Equal(2, await db.UserConsents.CountAsync(c => c.UserId == userId));
        Assert.Equal(referrerId, (await db.UserReferrals.SingleAsync(r => r.UserId == userId)).ReferrerId);
    }

    [Theory]
    [InlineData("username", "Username is a required field.")]
    [InlineData("emailAddress", "Email Address is a required field.")]
    [InlineData("password", "Password is a required field.")]
    public async Task TC_REG_002_a_missing_mandatory_field_is_400(string field, string message)
    {
        var body = Valid();
        var username = body["username"]!.GetValue<string>();
        body.Remove(field);

        var root = await Status(await Send(body), 400, "E10001", message);

        Assert.False(root.TryGetProperty("data", out _));
        if (field != "username") Assert.False(await UserExists(username));
    }

    [Fact]
    public async Task TC_REG_002_all_three_missing_keep_the_spec_example_message()
    {
        var body = new JsonObject { ["consents"] = new JsonArray(Consent(_required, true)) };
        await Status(await Send(body), 400, "E10001", "Username, Email Address and Password are required fields.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TC_REG_003_a_blank_mandatory_field_is_400(string blank)
    {
        var body = Valid();
        body["emailAddress"] = blank;
        await Rejected(body, 400, "E10001", "Email Address is a required field.");
    }

    [Theory]
    [InlineData("Passw0r", false)]
    [InlineData("Passw0rd", true)]
    [InlineData("password1", false)]
    [InlineData("PASSWORD1", false)]
    [InlineData("Password", false)]
    public async Task TC_REG_004_password_policy(string password, bool accepted)
    {
        var body = Valid();
        body["password"] = password;
        if (accepted) await Status(await Send(body), 201, "S10000");
        else await Rejected(body, 400, "E10001",
            "Password must be 8 to 128 characters and contain an upper-case letter, a lower-case letter and a digit.");
    }

    [Fact]
    public async Task TC_REG_004_password_length_bounds()
    {
        var body = Valid();
        body["password"] = "Aa1" + new string('x', 125);
        await Status(await Send(body), 201, "S10000");
        body = Valid();
        body["password"] = "Aa1" + new string('x', 126);
        await Rejected(body, 400, "E10001", null);
    }

    [Theory]
    [InlineData("username", 128, "Username must be at most 128 characters.")]
    [InlineData("firstName", 100, "First Name must be at most 100 characters.")]
    [InlineData("lastName", 100, "Last Name must be at most 100 characters.")]
    [InlineData("displayName", 128, "Display Name must be at most 128 characters.")]
    public async Task TC_REG_005_length_limits(string field, int max, string message)
    {
        var body = Valid();
        var prefix = field == "username" ? body["username"]!.GetValue<string>() : "";
        body[field] = (prefix + new string('a', max))[..max];
        await Status(await Send(body), 201, "S10000");

        body = Valid();
        var username = body["username"]!.GetValue<string>();
        body[field] = (field == "username" ? username : "") + new string('a', max + 1);
        body[field] = body[field]!.GetValue<string>()[..(max + 1)];
        var root = await Status(await Send(body), 400, "E10001", message);
        Assert.False(root.TryGetProperty("data", out _));
    }

    [Fact]
    public async Task TC_REG_005_email_length_limit()
    {
        var body = Valid();
        body["emailAddress"] = new string('a', 254 - "@example.test".Length) + "@example.test";
        await Status(await Send(body), 201, "S10000");
        body = Valid();
        body["emailAddress"] = new string('a', 255 - "@example.test".Length) + "@example.test";
        await Rejected(body, 400, "E10001", "Email Address must be at most 254 characters.");
    }

    [Theory]
    [InlineData("dateOfBirth", "1990-13-01", "Date Of Birth must be a valid date (YYYY-MM-DD).")]
    [InlineData("dateOfBirth", "31-01-1990", "Date Of Birth must be a valid date (YYYY-MM-DD).")]
    [InlineData("gender", "male", "Gender must be MALE, FEMALE or OTHER.")]
    [InlineData("gender", "X", "Gender must be MALE, FEMALE or OTHER.")]
    [InlineData("referrerId", "abc", "Referrer Id must be a valid UUID.")]
    [InlineData("emailAddress", "abc", "Email Address must be a valid email address.")]
    [InlineData("emailAddress", "a@b@c", "Email Address must be a valid email address.")]
    public async Task TC_REG_006_format_of_scalars(string field, string value, string message)
    {
        var body = Valid();
        body[field] = value;
        await Rejected(body, 400, "E10001", message);
    }

    [Theory]
    [InlineData("gender", "OTHER")]
    [InlineData("dateOfBirth", "2000-02-29")]
    public async Task TC_REG_006_valid_optional_scalars(string field, string value)
    {
        var body = Valid();
        body[field] = value;
        await Status(await Send(body), 201, "S10000");
    }

    [Fact]
    public async Task TC_REG_006_several_failures_are_listed_in_field_order()
    {
        var body = Valid();
        body["gender"] = "X";
        body["referrerId"] = "abc";
        await Rejected(body, 400, "E10001", "Gender must be MALE, FEMALE or OTHER, Referrer Id must be a valid UUID.");
    }

    private static JsonObject Address() => new()
    {
        ["title"] = "Home", ["address"] = "1 Road", ["city"] = "Watthana", ["state"] = "Bangkok", ["postalCode"] = "10110", ["country"] = "Thailand"
    };

    [Fact]
    public async Task TC_REG_007_address_object_validation()
    {
        var body = Valid();
        body["addresses"] = new JsonArray(Address());
        await Rejected(body, 400, "E10001", "Addresses must be an object.");

        body = Valid(); var a = Address(); a.Remove("city"); body["addresses"] = a;
        await Rejected(body, 400, "E10001", "addresses: City is a required field.");

        body = Valid(); a = Address(); a["postalCode"] = ""; body["addresses"] = a;
        await Rejected(body, 400, "E10001", "addresses: Postal Code is a required field.");

        body = Valid(); a = Address(); a["title"] = new string('t', 129); body["addresses"] = a;
        await Rejected(body, 400, "E10001", "addresses: Title must be at most 128 characters.");

        body = Valid(); a = Address(); a["isDefault"] = "yes"; body["addresses"] = a;
        await Rejected(body, 400, "E10001", "addresses: Is Default must be a boolean.");

        body = Valid(); a = Address(); a["title"] = new string('t', 128); body["addresses"] = a;
        await Status(await Send(body), 201, "S10000");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public async Task TC_REG_007_a_body_that_is_not_a_json_object_is_400_alone(string body)
    {
        var root = await Status(await Send(body), 400, "E10001", "Request Body must be an object.");
        Assert.False(root.TryGetProperty("data", out _));
    }

    [Fact]
    public async Task TC_REG_008_consent_item_validation()
    {
        var body = Valid();
        body["consents"] = new JsonArray(Consent(_required, true), new JsonObject { ["consentId"] = "x", ["consentVersion"] = "1.0.0" });
        await Rejected(body, 400, "E10001", "consents[1]: Consent Id must be a valid UUID.");

        body = Valid();
        body["consents"] = new JsonArray(Consent(_required, true, version: ""));
        await Rejected(body, 400, "E10001", "consents[0]: Consent Version is a required field.");

        body = Valid();
        body["consents"] = new JsonArray(Consent(_required, true, version: "1.0.0.123"));
        await Rejected(body, 400, "E10001", "consents[0]: Consent Version must be at most 8 characters.");

        body = Valid();
        var consent = Consent(_required, null); consent["result"] = "true";
        body["consents"] = new JsonArray(consent);
        await Rejected(body, 400, "E10001", "consents[0]: Result must be a boolean.");
    }

    [Fact]
    public async Task TC_REG_009_a_duplicate_consentId_is_400()
    {
        var body = Valid();
        body["consents"] = new JsonArray(Consent(_required, true), Consent(_required, false));
        await Rejected(body, 400, "E10001", "consents[1]: Consent Id must not be duplicated.");
    }

    [Fact]
    public async Task TC_REG_010_headers()
    {
        var body = Valid();
        await Status(await Send(body, timestamp: null), 400, "E10001", "Timestamp is a required header.");
        await Status(await Send(body, timestamp: "yesterday"), 400, "E10001", "Timestamp must be a valid UTC timestamp.");
        await Status(await Send(body, timestamp: "2026-11-18T12:00:00+07:00"), 400, "E10001", "Timestamp must be a valid UTC timestamp.");
        await Status(await Send(body, contentType: "text/plain"), 400, "E10001", "Content-Type must be application/json.");
        Assert.False(await UserExists(body["username"]!.GetValue<string>()));

        var root = await Status(await Send(body, requestId: null), 201, "S10000");
        Assert.StartsWith("req-", root.GetProperty("status").GetProperty("requestId").GetString());
    }

    [Theory]
    [InlineData("ACTIVE")]
    [InlineData("INACTIVE")]
    [InlineData("DELETED")]
    public async Task TC_REG_011_duplicate_username_or_email_in_any_state(string rowStatus)
    {
        var first = Valid();
        var userId = (await Status(await Send(first), 201, "S10000")).GetProperty("data").GetProperty("userId").GetGuid();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user SET row_status = @s WHERE id = @id", ("s", rowStatus), ("id", userId));

        var sameUsername = Valid();
        sameUsername["username"] = first["username"]!.GetValue<string>();
        await Status(await Send(sameUsername), 409, "E10003", "Username or email address already exists.");
        var sameEmail = Valid();
        sameEmail["emailAddress"] = first["emailAddress"]!.GetValue<string>();
        await Rejected(sameEmail, 409, "E10003", "Username or email address already exists.");
    }

    [Fact]
    public async Task TC_REG_012_concurrent_duplicates_give_one_201_and_one_409()
    {
        var a = Valid();
        var b = Valid();
        b["username"] = a["username"]!.GetValue<string>();

        var responses = await Task.WhenAll(Send(a), Send(b));

        Assert.Equal([201, 409], responses.Select(r => (int)r.StatusCode).Order());
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var bytes = _protector.Protect(a["username"]!.GetValue<string>());
        Assert.Equal(1, await db.Users.CountAsync(u => u.Username == bytes));
    }

    [Fact]
    public async Task TC_REG_013_referrer()
    {
        var body = Valid();
        body["referrerId"] = Guid.NewGuid().ToString();
        await Rejected(body, 404, "E10005", "Consent or referrer not found.");

        var referrer = Valid();
        var referrerId = (await Status(await Send(referrer), 201, "S10000")).GetProperty("data").GetProperty("userId").GetGuid();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'DELETED' WHERE id = @id", ("id", referrerId));
        body = Valid();
        body["referrerId"] = referrerId.ToString();
        await Status(await Send(body), 201, "S10000");
    }

    [Fact]
    public async Task TC_REG_014_a_submitted_consent_that_does_not_apply_is_404()
    {
        var type = await ConsentTypeAsync(_postgres.ConnectionString);
        var inactiveType = await ConsentTypeAsync(_postgres.ConnectionString, "INACTIVE");
        Guid[] notApplicable =
        [
            Guid.NewGuid(),
            await ConsentAsync(_postgres.ConnectionString, type, required: true, effectiveFromNow: TimeSpan.FromDays(1)),
            await ConsentAsync(_postgres.ConnectionString, type, required: true, expiredFromNow: TimeSpan.FromMinutes(-1)),
            await ConsentAsync(_postgres.ConnectionString, type, required: true, rowStatus: "INACTIVE"),
            await ConsentAsync(_postgres.ConnectionString, inactiveType, required: true)
        ];

        foreach (var id in notApplicable)
        {
            var body = Valid();
            body["consents"] = new JsonArray(Consent(_required, true), Consent(id, true));
            await Rejected(body, 404, "E10005", "Consent or referrer not found.");
        }
    }

    [Fact]
    public async Task TC_REG_015_a_stale_consent_version_is_400()
    {
        var body = Valid();
        body["consents"] = new JsonArray(Consent(_required, true, version: "0.9.0"));
        await Rejected(body, 400, "E10001", "Consent version is no longer current.");
    }

    [Fact]
    public async Task TC_REG_016_a_required_consent_needs_an_explicit_true()
    {
        foreach (var consents in new JsonNode?[] { null, new JsonArray(), new JsonArray(Consent(_required, false)), new JsonArray(Consent(_required, null)) })
        {
            var body = Valid();
            if (consents is null) body.Remove("consents"); else body["consents"] = consents;
            await Rejected(body, 400, "E10001", "All current required consents must be accepted.");
        }
    }

    [Fact]
    public async Task TC_REG_017_only_applicable_consents_are_required()
    {
        var type = await ConsentTypeAsync(_postgres.ConnectionString);
        var inactiveType = await ConsentTypeAsync(_postgres.ConnectionString, "INACTIVE");
        await ConsentAsync(_postgres.ConnectionString, type, required: true, effectiveFromNow: TimeSpan.FromDays(1));
        await ConsentAsync(_postgres.ConnectionString, type, required: true, expiredFromNow: TimeSpan.FromMinutes(-1));
        await ConsentAsync(_postgres.ConnectionString, type, required: true, rowStatus: "INACTIVE");
        await ConsentAsync(_postgres.ConnectionString, inactiveType, required: true);

        await Status(await Send(Valid()), 201, "S10000");
    }

    [Fact]
    public async Task TC_REG_018_no_applicable_required_consent_allows_no_consents()
    {
        await RetireAllConsentsAsync(_postgres.ConnectionString);
        var type = await ConsentTypeAsync(_postgres.ConnectionString);
        await ConsentAsync(_postgres.ConnectionString, type, required: false);

        var omitted = Valid(); omitted.Remove("consents");
        var empty = Valid(); empty["consents"] = new JsonArray();
        foreach (var body in new[] { omitted, empty })
        {
            var userId = (await Status(await Send(body), 201, "S10000")).GetProperty("data").GetProperty("userId").GetGuid();
            Assert.Equal(0, await CountAsync(_postgres.ConnectionString, "authen.t_user_consent", userId));
        }
    }

    [Fact]
    public async Task TC_REG_019_consent_rows_are_read_evidence_with_the_answer_or_false()
    {
        var body = Valid();
        body["consents"] = new JsonArray(Consent(_optional, null), Consent(_required, true));

        var userId = (await Status(await Send(body), 201, "S10000")).GetProperty("data").GetProperty("userId").GetGuid();

        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var rows = await db.UserConsents.Where(c => c.UserId == userId).ToListAsync();
        Assert.False(rows.Single(r => r.ConsentId == _optional).Result);
        Assert.True(rows.Single(r => r.ConsentId == _required).Result);
        Assert.All(rows, r => Assert.True(r.IsRead));
        Assert.Single(rows.Select(r => r.ReadAt).Distinct());
        Assert.InRange(rows[0].ReadAt!.Value, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task TC_REG_020_minimal_registration()
    {
        var userId = (await Status(await Send(Valid()), 201, "S10000")).GetProperty("data").GetProperty("userId").GetGuid();

        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        Assert.Null((await db.UserInformation.SingleAsync(i => i.UserId == userId)).PhoneNumber);
        Assert.Equal(0, await CountAsync(_postgres.ConnectionString, "authen.t_user_address", userId));
        Assert.Equal(0, await CountAsync(_postgres.ConnectionString, "authen.l_user_referral", userId));
    }

    [Fact]
    public async Task TC_REG_021_the_address_is_default_whatever_isDefault_says()
    {
        foreach (bool? isDefault in new bool?[] { false, null })
        {
            var body = Valid();
            var address = Address();
            if (isDefault is not null) address["isDefault"] = isDefault;
            body["addresses"] = address;
            var userId = (await Status(await Send(body), 201, "S10000")).GetProperty("data").GetProperty("userId").GetGuid();
            await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
            Assert.True((await db.UserAddresses.SingleAsync(a => a.UserId == userId)).IsDefault);
        }
    }

    private sealed class FailingRepository(IRegistrationRepository inner) : IRegistrationRepository
    {
        public Task<bool> IdentifierExistsAsync(string u, string e, CancellationToken ct) => inner.IdentifierExistsAsync(u, e, ct);
        public Task<IReadOnlyList<ApplicableConsent>> GetApplicableConsentsAsync(DateTime now, CancellationToken ct) => inner.GetApplicableConsentsAsync(now, ct);
        public Task<bool> UserExistsAsync(Guid id, CancellationToken ct) => inner.UserExistsAsync(id, ct);
        public async Task<Guid> AddAsync(NewRegistration r, CancellationToken ct)
        {
            await inner.AddAsync(r, ct); // every row is written, then the failure
            throw new InvalidOperationException("injected after the inserts");
        }
    }

    [Fact]
    public async Task TC_REG_022_an_unexpected_failure_is_500_without_bizCode_and_rolls_back()
    {
        var client = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddScoped<Ecommerce.Infrastructure.Persistence.Authen.RegistrationRepository>();
            s.AddScoped<IRegistrationRepository>(p => new FailingRepository(
                p.GetRequiredService<Ecommerce.Infrastructure.Persistence.Authen.RegistrationRepository>()));
        })).CreateClient();
        var body = Valid();

        var root = await Status(await Send(body, client: client), 500, null, "An unexpected error occurred.");

        Assert.False(root.TryGetProperty("data", out _));
        Assert.False(await UserExists(body["username"]!.GetValue<string>()));
    }

    [Fact]
    public async Task TC_REG_026_the_eleventh_request_in_a_minute_is_429()
    {
        var client = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimit:StrictPerMinute", "10")).CreateClient();
        for (var i = 0; i < 10; i++) await Send("{}", client: client);

        var response = await Send(Valid(), client: client);

        await Status(response, 429, "E90002", "Too many requests. Try again later.");
        Assert.InRange(int.Parse(response.Headers.GetValues("Retry-After").Single()), 1, 60);
    }

    [Fact]
    public async Task TC_REG_027_identifiers_are_stored_and_compared_as_sent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var body = Valid();
        body["username"] = $"John.Doe.{suffix}";
        body["emailAddress"] = $"John.Doe.{suffix}@Example.test";
        var userId = (await Status(await Send(body), 201, "S10000")).GetProperty("data").GetProperty("userId").GetGuid();

        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.Equal($"John.Doe.{suffix}", _protector.Unprotect(user.Username));
        Assert.Equal($"John.Doe.{suffix}@Example.test", _protector.Unprotect(user.EmailAddress));

        var lower = Valid();
        lower["username"] = $"john.doe.{suffix}";
        lower["emailAddress"] = $"john.doe.{suffix}@example.test";
        await Status(await Send(lower), 201, "S10000");
    }
}
