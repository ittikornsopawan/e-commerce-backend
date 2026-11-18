using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.CreateNewPassword;

/// <summary>
/// Orchestrates spec 04-create-new-password: hash the token → autocommit routing lookup → READ COMMITTED → user lock →
/// fresh now → guarded single-use consumption → current password → must differ → replace password → close every
/// session → COMMIT. Any failure result rolls everything back, so a refused same-password request keeps the token usable.
/// </summary>
public sealed class CreateNewPasswordCommandHandler(
    ICreateNewPasswordRepository passwords,
    IPasswordHasher hasher,
    ITokenService tokenService,
    ITransactionRunner transactions,
    TimeProvider clock) : ICommandHandler<CreateNewPasswordCommand>
{
    public async Task<Result> HandleAsync(CreateNewPasswordCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.Hash(request.ResetToken!);
        var newPassword = request.NewPassword!;

        try
        {
            var route = await passwords.FindResetAsync(tokenHash, clock.GetUtcNow().UtcDateTime, cancellationToken);
            if (route is null) return Result.Failure(AuthenErrors.InvalidResetToken);

            var result = await transactions.RunAsync(IsolationLevel.ReadCommitted, async ct =>
            {
                if (!await passwords.LockActiveUserAsync(route.UserId, ct)) return Result.Failure<bool>(AuthenErrors.InvalidResetToken);

                var now = clock.GetUtcNow().UtcDateTime; // after the user lock; never the lookup time
                if (!await passwords.ConsumeResetAsync(route, tokenHash, now, ct)) return Result.Failure<bool>(AuthenErrors.InvalidResetToken);

                var current = await passwords.GetCurrentPasswordAsync(route.UserId, ct)
                    ?? throw new InvalidOperationException("The user has no ACTIVE PASSWORD row."); // spec: internal failure, 500
                if (hasher.Verify(newPassword, current.Credential)) return Result.Failure<bool>(AuthenErrors.SamePassword);

                if (!await passwords.ReplacePasswordAsync(route.UserId, current.PasswordId, hasher.Hash(newPassword), now, ct))
                    throw new InvalidOperationException("The current PASSWORD row changed under the user lock.");
                await passwords.CloseAllTokensAsync(route.UserId, now, ct);
                return Result.Success(true);
            }, cancellationToken);

            return result.IsSuccess ? Result.Success() : Result.Failure(result.FirstError);
        }
        catch (TimeoutException)
        {
            return Result.Failure(CommonErrors.Timeout);
        }
    }
}
