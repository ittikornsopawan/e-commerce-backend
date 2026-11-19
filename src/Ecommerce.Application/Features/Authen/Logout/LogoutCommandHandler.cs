using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.Logout;

/// <summary>
/// Orchestrates spec 06-logout: READ COMMITTED → user lock → fresh now → recheck the JWT expiry → stored ACTIVE access
/// row → close the session's access then refresh rows → COMMIT. Every credential failure is one 401 with no change.
/// </summary>
public sealed class LogoutCommandHandler(
    ILogoutRepository logouts,
    ITokenService tokenService,
    ITransactionRunner transactions,
    TimeProvider clock) : ICommandHandler<LogoutCommand>
{
    public async Task<Result> HandleAsync(LogoutCommand request, CancellationToken cancellationToken)
    {
        var hash = tokenService.Hash(request.AccessToken);
        try
        {
            var result = await transactions.RunAsync(IsolationLevel.ReadCommitted, async ct =>
            {
                if (!await logouts.LockActiveUserAsync(request.UserId, ct)) return Result.Failure<bool>(AuthenErrors.AccessTokenInvalid);

                var now = clock.GetUtcNow().UtcDateTime; // after the user lock: a JWT that expired while waiting is refused
                if (request.TokenExpiresAtUtc <= now) return Result.Failure<bool>(AuthenErrors.AccessTokenInvalid);

                var sessionId = await logouts.GetSessionAsync(hash, request.UserId, now, ct);
                if (sessionId is null) return Result.Failure<bool>(AuthenErrors.AccessTokenInvalid);

                if (await logouts.CloseSessionAsync(sessionId.Value, request.UserId, now, ct) == 0)
                    throw new InvalidOperationException("The authorizing access token was not closed."); // spec: internal failure, 500
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
