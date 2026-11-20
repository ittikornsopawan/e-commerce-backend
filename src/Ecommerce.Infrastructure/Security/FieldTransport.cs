using System.Security.Cryptography;
using System.Text;
using Ecommerce.Application.Abstractions;

namespace Ecommerce.Infrastructure.Security;

/// <summary>
/// RSA-OAEP-SHA-256 unwrap of the request key and AES-256-GCM decrypt of each field. Wire value of a field:
/// base64(IV 12 bytes || ciphertext || tag 16 bytes), with the field's JSON path as additional authenticated data.
/// </summary>
internal sealed class FieldTransport : IFieldTransport
{
    private const int MinRsaBits = 3072;
    private const int KeyLength = 32;
    private const int IvLength = 12;
    private const int TagLength = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    // RSA instances are not guaranteed thread-safe, so only the key parameters are kept; each Open builds its own RSA.
    private readonly Dictionary<string, RSAParameters> _privateKeys = [];

    /// <param name="pemByKeyId">Private key (PEM) of every active key id, from Security:FieldTransport:Keys.</param>
    public FieldTransport(IReadOnlyDictionary<string, string> pemByKeyId)
    {
        foreach (var (id, pem) in pemByKeyId)
        {
            var rsa = RSA.Create();
            try
            {
                rsa.ImportFromPem(pem);
            }
            catch (Exception e) when (e is ArgumentException or CryptographicException)
            {
                rsa.Dispose();
                throw new InvalidOperationException($"Security:FieldTransport:Keys:{id} is not a valid PEM private key.", e);
            }

            if (rsa.KeySize < MinRsaBits)
            {
                rsa.Dispose();
                throw new InvalidOperationException($"Security:FieldTransport:Keys:{id} must be an RSA key of at least {MinRsaBits} bits.");
            }

            _privateKeys[id] = rsa.ExportParameters(true);
            rsa.Dispose();
        }
    }

    public IFieldTransportSession? Open(string keyId, string encryptedKey)
    {
        if (!_privateKeys.TryGetValue(keyId, out var parameters)) return null;

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportParameters(parameters);
            var key = rsa.Decrypt(Convert.FromBase64String(encryptedKey), RSAEncryptionPadding.OaepSHA256);
            if (key.Length == KeyLength) return new Session(key);

            CryptographicOperations.ZeroMemory(key);
            return null;
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            return null;
        }
    }

    private sealed class Session(byte[] key) : IFieldTransportSession
    {
        public void Dispose() => CryptographicOperations.ZeroMemory(key);

        public string? Decrypt(string path, string wireValue)
        {
            try
            {
                var wire = Convert.FromBase64String(wireValue);
                if (wire.Length < IvLength + TagLength) return null;

                var iv = wire.AsSpan(0, IvLength);
                var tag = wire.AsSpan(wire.Length - TagLength);
                var cipher = wire.AsSpan(IvLength, wire.Length - IvLength - TagLength);
                var plain = new byte[cipher.Length];

                using var aes = new AesGcm(key, TagLength);
                aes.Decrypt(iv, cipher, tag, plain, Encoding.UTF8.GetBytes(path));
                return StrictUtf8.GetString(plain);
            }
            catch (Exception e) when (e is FormatException or CryptographicException or DecoderFallbackException)
            {
                return null;
            }
        }
    }
}
