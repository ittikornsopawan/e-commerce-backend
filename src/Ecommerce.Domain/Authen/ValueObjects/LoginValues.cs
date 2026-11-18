namespace Ecommerce.Domain.Authen.ValueObjects;

/// <summary>The result stored in authen.l_user_login_attempt.</summary>
public enum LoginResult
{
    Success,
    Failed,
    Refused
}

/// <summary>A locked ACTIVE user that matched the identifier; text is decrypted (spec 02-login "Lock the user").</summary>
public sealed record LoginUser(Guid Id, string Username, string EmailAddress, string? DisplayName);

/// <summary>The two token rows of one login session; tokens are stored only as hashes.</summary>
public sealed record LoginSession(
    Guid UserId, Guid SessionId, byte[] AccessTokenHash, DateTime AccessExpiresAtUtc, byte[] RefreshTokenHash, DateTime RefreshExpiresAtUtc);
