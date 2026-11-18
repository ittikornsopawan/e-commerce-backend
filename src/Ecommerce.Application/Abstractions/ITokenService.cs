using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Application.Abstractions;

/// <param name="Value">The token as given to the client. Never stored.</param>
/// <param name="ExpiresAtUtc">When the token stops being valid.</param>
public sealed record IssuedToken(string Value, DateTime ExpiresAtUtc);

/// <summary>The JWT payload of spec 02-login: user snapshot and the resolved authorization claims.</summary>
public sealed record AccessTokenClaims(LoginUser User, ResolvedClaims Claims);

public interface ITokenService
{
    /// <summary>How long an access token lives (expiresIn).</summary>
    TimeSpan AccessTokenLifetime { get; }

    /// <summary>A signed HS256 JWT with iss, sub, aud, jti, iat, exp, user, roles, permissions and scopes.</summary>
    IssuedToken IssueAccessToken(AccessTokenClaims claims);

    /// <summary>A long-lived random token: 32 random bytes in base64url.</summary>
    IssuedToken IssueRefreshToken();

    /// <summary>A short-lived random token to be sent to the user by email.</summary>
    IssuedToken IssuePasswordResetToken();

    /// <summary>One-way hash used to store and look up tokens, so a database leak does not leak usable tokens.</summary>
    byte[] Hash(string token);
}

/// <summary>The brute-force window of spec 02-login (15 minutes by default, configurable).</summary>
public sealed record LoginSettings(TimeSpan FailureWindow);
