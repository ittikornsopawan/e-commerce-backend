using Ecommerce.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ecommerce.Infrastructure.Email;

/// <summary>
/// Development stand-in: writes to the log instead of sending mail. Replace it with a real
/// <see cref="IEmailSender"/> (SMTP, SES, ...) before going live.
/// </summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendPasswordResetAsync(string toEmail, string displayName, string resetToken, CancellationToken cancellationToken)
    {
        logger.LogInformation("Password reset requested for {DisplayName}. Mail is not sent by this sender.", displayName);
        logger.LogDebug("Password reset token for {Email}: {Token}", toEmail, resetToken);
        return Task.CompletedTask;
    }
}
