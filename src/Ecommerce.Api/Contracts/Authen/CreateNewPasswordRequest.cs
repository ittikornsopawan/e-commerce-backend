namespace Ecommerce.Api.Contracts.Authen;

/// <summary>Body of POST v1/auth/create-new-password.</summary>
public sealed record CreateNewPasswordRequest(string? ResetToken, string? NewPassword) : IEncryptedRequest
{
    public IEncryptedRequest Decrypt(EncryptedFields f) => this with { ResetToken = f.Field("resetToken", ResetToken), NewPassword = f.Field("newPassword", NewPassword) };
}
