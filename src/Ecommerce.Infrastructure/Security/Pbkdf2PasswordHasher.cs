using System.Security.Cryptography;
using Ecommerce.Application.Abstractions;
using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 with a random 16-byte salt. The schema has no column for the iteration count,
/// so it is fixed here; raising it later means existing hashes stop verifying unless a column is added.
/// </summary>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltLength = 16;
    private const int HashLength = 32;
    private const int Iterations = 600_000;

    public PasswordCredential Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        return new PasswordCredential(Derive(password, salt), salt);
    }

    public bool Verify(string password, PasswordCredential credential) =>
        CryptographicOperations.FixedTimeEquals(Derive(password, credential.Salt), credential.Hash);

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashLength);
}
