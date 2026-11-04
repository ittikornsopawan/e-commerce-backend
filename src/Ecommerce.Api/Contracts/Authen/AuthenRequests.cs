namespace Ecommerce.Api.Contracts.Authen;

public sealed record RegisterRequest(
    string Username,
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? DisplayName);

public sealed record LoginRequest(string UsernameOrEmail, string Password);

public sealed record RequestPasswordResetRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);
