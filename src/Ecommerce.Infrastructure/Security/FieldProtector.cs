using System.Security.Cryptography;
using System.Text;

namespace Ecommerce.Infrastructure.Security;

/// <summary>
/// Encrypts personal data stored in bytea columns. The same input always gives the same output, so unique
/// columns (username, email) can still be matched with <c>WHERE column = @value</c>.
/// </summary>
/// <remarks>
/// Synthetic-IV construction: IV = first 16 bytes of HMAC-SHA256(plaintext), then AES-256-CBC. Unprotect
/// recomputes the HMAC, which also authenticates the data. Layout: IV (16 bytes) || ciphertext.
/// </remarks>
internal sealed class FieldProtector
{
    private const int IvLength = 16;

    private readonly byte[] _encryptionKey;
    private readonly byte[] _macKey;

    public FieldProtector(byte[] masterKey)
    {
        _encryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: "ecommerce:field:enc"u8.ToArray());
        _macKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: "ecommerce:field:mac"u8.ToArray());
    }

    public byte[] Protect(string value)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        var iv = ComputeIv(plain);

        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        var cipher = aes.EncryptCbc(plain, iv, PaddingMode.PKCS7);

        var result = new byte[IvLength + cipher.Length];
        iv.CopyTo(result, 0);
        cipher.CopyTo(result, IvLength);
        return result;
    }

    public string Unprotect(byte[] protectedValue)
    {
        if (protectedValue.Length <= IvLength)
        {
            throw new CryptographicException("Protected value is too short.");
        }

        var iv = protectedValue.AsSpan(0, IvLength);

        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        var plain = aes.DecryptCbc(protectedValue.AsSpan(IvLength), iv, PaddingMode.PKCS7);

        if (!CryptographicOperations.FixedTimeEquals(ComputeIv(plain), iv))
        {
            throw new CryptographicException("Protected value failed authentication.");
        }

        return Encoding.UTF8.GetString(plain);
    }

    private byte[] ComputeIv(byte[] plain) => HMACSHA256.HashData(_macKey, plain)[..IvLength];
}
