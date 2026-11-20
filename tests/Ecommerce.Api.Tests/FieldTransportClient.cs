using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;

namespace Ecommerce.Api.Tests;

/// <summary>
/// The frontend side of api-conventions "Encrypted sensitive fields" for tests: a per-request AES-256 key wrapped with
/// RSA-OAEP-SHA-256, and AES-256-GCM per field with the JSON path as additional authenticated data.
/// </summary>
internal static class FieldTransportClient
{
    public const string KeyId = "test-key-1";

    private static readonly RSA Backend = RSA.Create(3072);

    /// <summary>Gives the backend under test the private key of <see cref="KeyId"/>.</summary>
    public static IWebHostBuilder UseFieldTransportKey(this IWebHostBuilder builder) =>
        builder.UseSetting($"Security:FieldTransport:Keys:{KeyId}", Backend.ExportPkcs8PrivateKeyPem());

    /// <summary>Encrypts the string fields at <paramref name="paths"/> and adds both headers; a body that is not a JSON object is sent as it is.</summary>
    public static string Seal(HttpRequestMessage request, string body, params string[] paths)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        request.Headers.Add("encryptionKeyId", KeyId);
        request.Headers.Add("encryptedKey", Convert.ToBase64String(Backend.Encrypt(key, RSAEncryptionPadding.OaepSHA256)));

        JsonNode? node;
        try { node = JsonNode.Parse(body); }
        catch (System.Text.Json.JsonException) { return body; }
        if (node is not JsonObject root) return body;

        foreach (var path in paths)
        {
            var parts = path.Split('.');
            var owner = root;
            foreach (var part in parts[..^1]) owner = owner[part] as JsonObject ?? new JsonObject();
            if (owner[parts[^1]] is JsonValue value && value.TryGetValue<string>(out var plain))
                owner[parts[^1]] = EncryptField(key, path, plain);
        }

        return root.ToJsonString();
    }

    public static string EncryptField(byte[] key, string path, string plain)
    {
        var iv = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(iv, data, cipher, tag, Encoding.UTF8.GetBytes(path));
        return Convert.ToBase64String([.. iv, .. cipher, .. tag]);
    }

    public static string WrapKey(byte[] key) => Convert.ToBase64String(Backend.Encrypt(key, RSAEncryptionPadding.OaepSHA256));
}
