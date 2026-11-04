namespace Ecommerce.Application.Features.Authen.Login;

public sealed record LoginResponse(
    Guid UserId,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);
