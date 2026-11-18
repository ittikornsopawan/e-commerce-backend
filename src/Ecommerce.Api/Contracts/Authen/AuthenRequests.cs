namespace Ecommerce.Api.Contracts.Authen;

public sealed record ResetPasswordRequest(string Token, string NewPassword);
