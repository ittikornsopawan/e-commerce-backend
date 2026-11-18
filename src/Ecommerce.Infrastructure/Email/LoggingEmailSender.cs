using Ecommerce.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ecommerce.Infrastructure.Email;

/// <summary>
/// Development stand-in: logs that a mail was handed off, never the address, the link or the token (spec 03-reset-password:
/// never log the raw token or the email plaintext). Replace it with a real <see cref="IEmailSender"/> before going live.
/// </summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken cancellationToken)
    {
        logger.LogInformation("Password reset email handed off. Mail is not sent by this sender.");
        return Task.CompletedTask;
    }
}
