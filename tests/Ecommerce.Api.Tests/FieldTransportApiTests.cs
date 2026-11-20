using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ecommerce.Api.Tests;

// api-conventions "Encrypted sensitive fields" for 01 register, 02 login, 03 reset password and 04 create new password:
// the encryption headers and the one decrypt-failure answer. No database is reached: both fail before the use case.
public sealed class FieldTransportApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Policy = "Password must be 8 to 128 characters and contain an upper-case letter, a lower-case letter and a digit.";
    private const string Decrypt = "Encrypted fields could not be decrypted.";

    private sealed record Endpoint(string Path, JsonObject Body);

    private static readonly Endpoint Register = new("/v1/auth/register", new() { ["username"] = "john.doe", ["emailAddress"] = "john.doe@example.com", ["password"] = "Passw0rd-Example" });
    private static readonly Endpoint Login = new("/v1/auth/login", new() { ["username"] = "john.doe", ["password"] = "Passw0rd-Example" });
    private static readonly Endpoint Reset = new("/v1/auth/reset-password", new() { ["emailAddress"] = "john.doe@example.com" });
    private static readonly Endpoint NewPassword = new("/v1/auth/create-new-password", new() { ["resetToken"] = "reset-token-value", ["newPassword"] = "NewPassw0rd-Example" });

    private HttpClient Client() => factory.WithWebHostBuilder(b => b
        .UseSetting("Database:ConnectionString", "Host=127.0.0.1;Port=1;Database=x;Timeout=1")
        .UseSetting("Jwt:SigningKey", "unit-test-signing-key-0123456789-abcdef")
        .UseSetting("Security:FieldEncryptionKey", Convert.ToBase64String(new byte[32]))
        .UseSetting("RateLimit:StrictPerMinute", "10000")
        .UseFieldTransportKey()).CreateClient();

    private static async Task<HttpResponseMessage> Post(HttpClient client, Endpoint e, JsonObject body, string? keyId, string? wrappedKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, e.Path) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        request.Headers.Add("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
        if (keyId is not null) request.Headers.Add("encryptionKeyId", keyId);
        if (wrappedKey is not null) request.Headers.Add("encryptedKey", wrappedKey);
        return await client.SendAsync(request);
    }

    private static async Task<string> Bad(HttpResponseMessage response, string bizMessage)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        var status = json.RootElement.GetProperty("status");
        Assert.Equal(400, (int)response.StatusCode);
        Assert.Equal("E10001", status.GetProperty("bizCode").GetString());
        Assert.Equal(bizMessage, status.GetProperty("bizMessage").GetString());
        return System.Text.RegularExpressions.Regex.Replace(text, "\"(timestamp|requestId)\":\"[^\"]*\"", "");
    }

    private static JsonObject Encrypted(Endpoint e, byte[] key, Func<string, string, string>? encrypt = null)
    {
        var body = new JsonObject();
        foreach (var (name, value) in e.Body) body[name] = (encrypt ?? ((path, plain) => FieldTransportClient.EncryptField(key, path, plain)))(name, value!.GetValue<string>());
        return body;
    }

    private async Task MissingHeaders(Endpoint e)
    {
        var client = Client();
        var key = RandomNumberGenerator.GetBytes(32);
        var body = Encrypted(e, key);
        var wrapped = FieldTransportClient.WrapKey(key);

        await Bad(await Post(client, e, body, null, wrapped), "Encryption Key Id is a required header.");
        await Bad(await Post(client, e, body, FieldTransportClient.KeyId, null), "Encrypted Key is a required header.");
        await Bad(await Post(client, e, body, null, null), "Encryption Key Id is a required header, Encrypted Key is a required header.");
        await Bad(await Post(client, e, body, "", wrapped), "Encryption Key Id is a required header.");
    }

    private async Task DecryptFailures(Endpoint e)
    {
        var client = Client();
        var key = RandomNumberGenerator.GetBytes(32);
        var wrapped = FieldTransportClient.WrapKey(key);
        var first = e.Body.First().Key;
        var tampered = Convert.FromBase64String(FieldTransportClient.EncryptField(key, first, "x"));
        tampered[^1] ^= 1;

        var answers = new List<string>
        {
            await Bad(await Post(client, e, Encrypted(e, key), "unknown-key", wrapped), Decrypt),
            await Bad(await Post(client, e, Encrypted(e, key), FieldTransportClient.KeyId, "not base64!"), Decrypt),
            await Bad(await Post(client, e, Encrypted(e, key), FieldTransportClient.KeyId, FieldTransportClient.WrapKey(RandomNumberGenerator.GetBytes(16))), Decrypt),
            await Bad(await Post(client, e, Encrypted(e, RandomNumberGenerator.GetBytes(32)), FieldTransportClient.KeyId, wrapped), Decrypt),
            await Bad(await Post(client, e, e.Body, FieldTransportClient.KeyId, wrapped), Decrypt),
            await Bad(await Post(client, e, Encrypted(e, key, (_, plain) => FieldTransportClient.EncryptField(key, "other", plain)), FieldTransportClient.KeyId, wrapped), Decrypt),
            await Bad(await Post(client, e, Encrypted(e, key, (_, _) => "AAAA"), FieldTransportClient.KeyId, wrapped), Decrypt),
            await Bad(await Post(client, e, Encrypted(e, key, (_, _) => Convert.ToBase64String(tampered)), FieldTransportClient.KeyId, wrapped), Decrypt)
        };

        Assert.Single(answers.Distinct());
        Assert.DoesNotContain("john.doe", answers[0]);
    }

    // The decrypt answer comes before the validation of the plain values, and valid ciphertext reaches that validation.
    private async Task DecryptBeforeValidation(Endpoint e, string field, string invalidPlain, string validationMessage)
    {
        var client = Client();
        var key = RandomNumberGenerator.GetBytes(32);
        var wrapped = FieldTransportClient.WrapKey(key);
        var invalid = (JsonObject)e.Body.DeepClone();
        invalid[field] = invalidPlain;

        await Bad(await Post(client, e, invalid, FieldTransportClient.KeyId, wrapped), Decrypt);
        await Bad(await Post(client, e, Encrypted(new Endpoint(e.Path, invalid), key), FieldTransportClient.KeyId, wrapped), validationMessage);
    }

    [Fact] public Task TC_LOGIN_022_missing_encryption_headers_are_400() => MissingHeaders(Login);
    [Fact] public Task TC_LOGIN_023_every_decrypt_failure_is_the_same_400() => DecryptFailures(Login);
    [Fact] public Task TC_LOGIN_024_decrypt_comes_before_validation() => DecryptBeforeValidation(Login, "password", "", "Password is a required field.");

    [Fact] public Task TC_REG_028_missing_encryption_headers_are_400() => MissingHeaders(Register);
    [Fact] public Task TC_REG_029_every_decrypt_failure_is_the_same_400() => DecryptFailures(Register);
    [Fact] public Task TC_REG_030_decrypt_comes_before_validation() => DecryptBeforeValidation(Register, "password", "short", Policy);

    [Fact] public Task TC_RESET_013_missing_encryption_headers_are_400() => MissingHeaders(Reset);
    [Fact] public Task TC_RESET_014_every_decrypt_failure_is_the_same_400() => DecryptFailures(Reset);

    [Fact] public Task TC_NEWPW_013_missing_encryption_headers_are_400() => MissingHeaders(NewPassword);
    [Fact] public Task TC_NEWPW_014_every_decrypt_failure_is_the_same_400() => DecryptFailures(NewPassword);
    [Fact] public Task TC_NEWPW_015_decrypt_comes_before_validation() => DecryptBeforeValidation(NewPassword, "newPassword", "short", Policy);
}
