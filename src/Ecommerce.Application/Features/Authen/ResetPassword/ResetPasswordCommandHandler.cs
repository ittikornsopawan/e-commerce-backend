using Ecommerce.Application.Abstractions;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.ResetPassword;

public sealed class ResetPasswordCommandHandler(
    IPasswordRepository passwords,
    ITokenRepository tokens,
    IPasswordHasher hasher,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ResetPasswordCommand>
{
    public async Task<Result> HandleAsync(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var newPassword = PlainPassword.Create(request.NewPassword).Value;
        var now = clock.GetUtcNow().UtcDateTime;

        var token = await tokens.FindActivePasswordResetAsync(tokenService.Hash(request.Token), now, cancellationToken);
        if (token is null)
        {
            return Result.Failure(AuthenErrors.InvalidResetToken);
        }

        var consumed = token.Consume(now);
        if (consumed.IsFailure)
        {
            return consumed;
        }

        var credential = hasher.Hash(newPassword.Value);

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await passwords.SetAsync(token.UserId, credential, ct);
            await tokens.ConsumePasswordResetAsync(token, ct);
            await tokens.RevokeSessionsAsync(token.UserId, ct);
        }, cancellationToken);

        return Result.Success();
    }
}
