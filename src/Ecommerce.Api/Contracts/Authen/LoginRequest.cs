namespace Ecommerce.Api.Contracts.Authen;

/// <summary>Body of POST v1/auth/login.</summary>
public sealed record LoginRequest(string? Username, string? Password) : IEncryptedRequest
{
    public IEncryptedRequest Decrypt(EncryptedFields f) => this with { Username = f.Field("username", Username), Password = f.Field("password", Password) };
}
