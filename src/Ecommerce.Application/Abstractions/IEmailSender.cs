namespace Ecommerce.Application.Abstractions;

public interface IEmailSender
{
    Task SendPasswordResetAsync(string toEmail, string displayName, string resetToken, CancellationToken cancellationToken);
}
