namespace Ecommerce.Application.Features.Authen.Login;

/// <summary>data of 200 S10000 (spec 02-login Data Object).</summary>
public sealed record LoginResponse(string TokenType, string AccessToken, string RefreshAccessToken, int ExpiresIn);
