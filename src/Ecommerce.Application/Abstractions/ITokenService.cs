using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Application.Abstractions;

/// <param name="Value">The token as given to the client. Never stored.</param>
/// <param name="ExpiresAtUtc">When the token stops being valid.</param>
public sealed record IssuedToken(string Value, DateTime ExpiresAtUtc);

public interface ITokenService
{
    /// <summary>A signed, short-lived JWT.</summary>
    IssuedToken IssueAccessToken(UserId userId, Username username);

    /// <summary>A long-lived random token.</summary>
    IssuedToken IssueRefreshToken();

    /// <summary>A short-lived random token to be sent to the user by email.</summary>
    IssuedToken IssuePasswordResetToken();

    /// <summary>One-way hash used to store and look up tokens, so a database leak does not leak usable tokens.</summary>
    byte[] Hash(string token);
}
