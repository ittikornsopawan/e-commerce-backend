namespace Ecommerce.Api.Contracts.Authen;

/// <summary>Body of POST v1/auth/reset-password.</summary>
public sealed record RequestPasswordResetBody(string? EmailAddress) : IEncryptedRequest
{
    public IEncryptedRequest Decrypt(EncryptedFields f) => this with { EmailAddress = f.Field("emailAddress", EmailAddress) };
}
