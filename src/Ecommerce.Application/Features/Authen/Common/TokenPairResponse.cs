namespace Ecommerce.Application.Features.Authen.Common;

/// <summary>data of 200 S10000 for login (spec 02) and refresh (spec 05).</summary>
public sealed record TokenPairResponse(string TokenType, string AccessToken, string RefreshAccessToken, int ExpiresIn);
