namespace Ecommerce.Application.Abstractions;

public interface IEmailSender
{
    /// <summary>Hands off the reset email after COMMIT (spec 03-reset-password). Delivery is outside the API contract.</summary>
    Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken cancellationToken);
}

/// <summary>Spec 03-reset-password settings: 3 resets per 1 hour, 30-minute tokens, the trusted reset page URL.</summary>
public sealed record PasswordResetSettings(int MaxRequests, TimeSpan Window, TimeSpan TokenLifetime, string ResetUrl);
