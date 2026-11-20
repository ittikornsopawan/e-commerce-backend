using System.Security.Cryptography;
using System.Text;
using Ecommerce.Infrastructure.Security;

namespace Ecommerce.Infrastructure.Tests;

// api-conventions "Encrypted sensitive fields": RSA-OAEP-SHA-256 key unwrap and AES-256-GCM field decrypt.
public sealed class FieldTransportTests
{
    private static readonly RSA Rsa = RSA.Create(3072);
    private static readonly FieldTransport Transport = new(new Dictionary<string, string> { ["k1"] = Rsa.ExportPkcs8PrivateKeyPem() });

    private static string Field(byte[] key, string path, string plain)
    {
        var iv = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(iv, data, cipher, tag, Encoding.UTF8.GetBytes(path));
        return Convert.ToBase64String([.. iv, .. cipher, .. tag]);
    }

    private static string Wrap(byte[] key) => Convert.ToBase64String(Rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA256));

    [Fact]
    public void A_field_round_trips_including_unicode_and_empty_text()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var session = Transport.Open("k1", Wrap(key))!;

        Assert.Equal("Pässw0rd-สวัสดี", session.Decrypt("password", Field(key, "password", "Pässw0rd-สวัสดี")));
        Assert.Equal("", session.Decrypt("address", Field(key, "address", "")));
    }

    [Fact]
    public void An_unknown_key_id_or_a_bad_wrapped_key_does_not_open()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        Assert.Null(Transport.Open("other", Wrap(key)));
        Assert.Null(Transport.Open("k1", "not base64!"));
        Assert.Null(Transport.Open("k1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(384))));
        Assert.Null(Transport.Open("k1", Wrap(RandomNumberGenerator.GetBytes(16))));
    }

    [Fact]
    public void A_wrong_path_a_tampered_tag_a_short_value_or_plain_text_is_null()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var session = Transport.Open("k1", Wrap(key))!;
        var tampered = Convert.FromBase64String(Field(key, "password", "x"));
        tampered[^1] ^= 1;

        Assert.Null(session.Decrypt("addresses.city", Field(key, "password", "x")));
        Assert.Null(session.Decrypt("password", Convert.ToBase64String(tampered)));
        Assert.Null(session.Decrypt("password", Convert.ToBase64String(new byte[27])));
        Assert.Null(session.Decrypt("password", "Passw0rd-Example"));
    }

    [Fact]
    public async Task Fifty_sessions_open_and_decrypt_in_parallel()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            var key = RandomNumberGenerator.GetBytes(32);
            using var session = Transport.Open("k1", Wrap(key));
            return session?.Decrypt("password", Field(key, "password", $"Passw0rd-{i}"));
        })));

        Assert.Equal(Enumerable.Range(0, 50).Select(i => $"Passw0rd-{i}"), results);
    }

    [Fact]
    public void A_disposed_session_no_longer_decrypts_because_its_key_is_zeroed()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var session = Transport.Open("k1", Wrap(key))!;
        var wire = Field(key, "password", "x");
        Assert.Equal("x", session.Decrypt("password", wire));

        session.Dispose();

        Assert.Null(session.Decrypt("password", wire));
    }

    [Fact]
    public void A_key_below_3072_bits_or_a_bad_pem_is_refused_at_startup()
    {
        using var small = RSA.Create(2048);

        Assert.Throws<InvalidOperationException>(() => new FieldTransport(new Dictionary<string, string> { ["k"] = small.ExportPkcs8PrivateKeyPem() }));
        Assert.Throws<InvalidOperationException>(() => new FieldTransport(new Dictionary<string, string> { ["k"] = "not a pem" }));
    }
}
