namespace Ecommerce.Api.Contracts.Authen;

public sealed record LoginRequest(string UsernameOrEmail, string Password);

public sealed record RequestPasswordResetRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);
