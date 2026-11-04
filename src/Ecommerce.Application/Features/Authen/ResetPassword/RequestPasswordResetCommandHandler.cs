using Ecommerce.Application.Abstractions;
using Ecommerce.Domain.Authen.Entities;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.ResetPassword;

/// <summary>
/// Always succeeds, whether or not the email is registered, so the endpoint cannot be used to find out who has an account.
/// </summary>
public sealed class RequestPasswordResetCommandHandler(
    IUserRepository users,
    ITokenRepository tokens,
    ITokenService tokenService,
    IEmailSender emailSender,
    IUnitOfWork unitOfWork) : ICommandHandler<RequestPasswordResetCommand>
{
    public async Task<Result> HandleAsync(RequestPasswordResetCommand request, CancellationToken cancellationToken)
    {
        var email = EmailAddress.Create(request.Email).Value;

        var user = await users.FindByEmailAsync(email, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result.Success();
        }

        var issued = tokenService.IssuePasswordResetToken();
        var token = PasswordResetToken.Issue(user.Id, tokenService.Hash(issued.Value), issued.ExpiresAtUtc);

        await unitOfWork.ExecuteInTransactionAsync(ct => tokens.AddPasswordResetAsync(token, ct), cancellationToken);

        await emailSender.SendPasswordResetAsync(user.Email.Value, user.DisplayName.Value, issued.Value, cancellationToken);

        return Result.Success();
    }
}
