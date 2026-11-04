using Ecommerce.Domain.Common;

namespace Ecommerce.Domain.Authen.ValueObjects;

/// <summary>A salted password hash. The plain password is never kept.</summary>
public sealed class PasswordCredential(byte[] hash, byte[] salt) : ValueObject
{
    public byte[] Hash { get; } = hash;

    public byte[] Salt { get; } = salt;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Convert.ToBase64String(Hash);
        yield return Convert.ToBase64String(Salt);
    }
}
