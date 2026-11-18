namespace Ecommerce.Infrastructure.Options;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>A statement slower than this fails, and the use case answers 408 E90001.</summary>
    public int CommandTimeoutSeconds { get; init; } = 30;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException($"{Section}:ConnectionString is not configured.");
        }
    }
}

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; init; } = "ecommerce";

    public string Audience { get; init; } = "ecommerce-api";

    /// <summary>HMAC signing key, at least 32 characters. Keep it out of source control.</summary>
    public string SigningKey { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 15;

    public int RefreshTokenDays { get; init; } = 14;

    internal void Validate()
    {
        if (SigningKey.Length < 32)
        {
            throw new InvalidOperationException($"{Section}:SigningKey must be at least 32 characters.");
        }

        if (AccessTokenMinutes <= 0 || RefreshTokenDays <= 0)
        {
            throw new InvalidOperationException($"{Section} token lifetimes must be positive.");
        }
    }
}

public sealed class SecurityOptions
{
    public const string Section = "Security";

    /// <summary>Base64 of 32 random bytes. Protects the bytea columns of authen.m_user. Losing it makes those columns unreadable.</summary>
    public string FieldEncryptionKey { get; init; } = string.Empty;

    public int PasswordResetMinutes { get; init; } = 30;

    /// <summary>Spec 03-reset-password: at most this many resets per user in the window (3 per 60 minutes).</summary>
    public int PasswordResetMaxRequests { get; init; } = 3;

    public int PasswordResetWindowMinutes { get; init; } = 60;

    /// <summary>The trusted reset page; the raw token is appended. Configuration, never request input.</summary>
    public string PasswordResetUrl { get; init; } = "https://www.domain.com/reset-password?token=";

    /// <summary>Spec 02-login: FAILED attempts in this window count toward the limit of 5.</summary>
    public int LoginFailureWindowMinutes { get; init; } = 15;

    /// <summary>Spec 05-refresh-token: a consumed refresh token used again within this many seconds is a concurrent duplicate.</summary>
    public int RefreshReuseGraceSeconds { get; init; } = 10;

    internal byte[] DecodeFieldKey()
    {
        try
        {
            var key = Convert.FromBase64String(FieldEncryptionKey);
            if (key.Length == 32)
            {
                return key;
            }
        }
        catch (FormatException)
        {
            // fall through to the error below
        }

        throw new InvalidOperationException($"{Section}:FieldEncryptionKey must be base64 of exactly 32 bytes.");
    }
}
