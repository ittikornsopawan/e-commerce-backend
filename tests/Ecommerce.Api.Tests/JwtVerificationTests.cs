using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Ecommerce.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Ecommerce.Api.Tests;

// Spec: dev handoff section 2 "Auth": verify signature, issuer, audience, expiry, UUID sub and the roles claim; 401 E10002.
public sealed class JwtVerificationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Key = "unit-test-signing-key-0123456789-abcdef";
    private const string Issuer = "ecommerce";
    private const string Audience = "ecommerce-api";

    // UseSetting, not ConfigureAppConfiguration: Program.cs reads the options while it builds, before late sources apply.
    private HttpClient Client() => factory.WithWebHostBuilder(b => b
        .UseSetting("Database:ConnectionString", "Host=127.0.0.1;Port=1;Database=x;Timeout=1")
        .UseSetting("Jwt:SigningKey", Key)
        .UseSetting("Security:FieldEncryptionKey", Convert.ToBase64String(new byte[32]))).CreateClient();

    private static string Token(
        string? sub = null, string issuer = Issuer, string audience = Audience, string key = Key,
        DateTime? expires = null, string[]? roles = null)
    {
        var claims = new List<Claim> { new("sub", sub ?? Guid.NewGuid().ToString()) };
        claims.AddRange((roles ?? []).Select(r => new Claim("roles", r)));
        var jwt = new JwtSecurityToken(
            issuer, audience, claims, DateTime.UtcNow.AddMinutes(-10), expires ?? DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public static IEnumerable<object?[]> BadTokens() =>
    [
        [null],
        ["not-a-jwt"],
        [Token(expires: DateTime.UtcNow.AddMinutes(-5))],
        [Token(issuer: "someone-else")],
        [Token(audience: "another-api")],
        [Token(key: "a-different-signing-key-0123456789-abcd")],
        [Token(sub: "not-a-uuid")]
    ];

    /// <summary>GET v1/user/profile (spec 08) with a valid timestamp, so only the JWT decides between 401 and the handler.</summary>
    internal static HttpRequestMessage ProfileProbe()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/user/profile");
        request.Headers.Add("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
        return request;
    }

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task A_bad_token_answers_401_E10002_in_the_envelope(string? token)
    {
        var client = Client();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(ProfileProbe());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var status = json.RootElement.GetProperty("status");
        Assert.Equal(401, status.GetProperty("code").GetInt32());
        Assert.Equal("E10002", status.GetProperty("bizCode").GetString());
        Assert.Equal("Invalid or expired access token.", status.GetProperty("bizMessage").GetString());
        Assert.Equal("ERROR", status.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_valid_token_passes_verification()
    {
        var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(roles: ["CUSTOMER"]));

        var response = await client.SendAsync(ProfileProbe());

        // The handler then needs the database, which is not reachable here; reaching it proves the token was accepted.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Caller_claims_read_the_uuid_sub()
    {
        var id = Guid.NewGuid();
        Assert.True(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())], "jwt")).TryGetUserId(out var parsed));
        Assert.Equal(id, parsed);
        Assert.False(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "x")], "jwt")).TryGetUserId(out _));
    }

    private static Ecommerce.Application.Features.Authen.Common.Caller CallerWith(IReadOnlyList<string> roles) => new(Guid.NewGuid(), "jwt", DateTime.UtcNow, roles);

    [Fact]
    public void The_roles_claim_is_an_array_of_unique_non_empty_names_and_PLATFORM_ADMIN_is_exact()
    {
        var sub = Guid.NewGuid().ToString();
        Assert.True(CallerClaims.TryGetRoles(UpsertUserProfileApiTests.Jwt(sub, roles: new[] { "PLATFORM_ADMIN", "CUSTOMER" }), out var admin));
        Assert.True(CallerWith(admin).IsPlatformAdmin);
        Assert.True(CallerClaims.TryGetRoles(UpsertUserProfileApiTests.Jwt(sub, roles: new[] { "platform_admin" }), out var lower));
        Assert.False(CallerWith(lower).IsPlatformAdmin);
        Assert.True(CallerClaims.TryGetRoles(UpsertUserProfileApiTests.Jwt(sub, roles: Array.Empty<string>()), out var none));
        Assert.Empty(none);

        Assert.False(CallerClaims.TryGetRoles(UpsertUserProfileApiTests.Jwt(sub), out _));                                  // missing
        Assert.False(CallerClaims.TryGetRoles(UpsertUserProfileApiTests.Jwt(sub, roles: "PLATFORM_ADMIN"), out _));        // not an array
        Assert.False(CallerClaims.TryGetRoles(UpsertUserProfileApiTests.Jwt(sub, roles: new[] { "A", "A" }), out _));      // duplicated
        Assert.False(CallerClaims.TryGetRoles(UpsertUserProfileApiTests.Jwt(sub, roles: new[] { "" }), out _));            // empty name
    }
}
