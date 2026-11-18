namespace Ecommerce.Api.Contracts.Authen;

/// <summary>Body of POST v1/auth/refresh-token.</summary>
public sealed record RefreshTokenRequest(string? RefreshAccessToken);
