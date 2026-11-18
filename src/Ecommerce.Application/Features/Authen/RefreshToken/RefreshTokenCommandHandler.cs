using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Domain;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.RefreshToken;

/// <summary>
/// Orchestrates spec 05-refresh-token: hash → owner lookup → READ COMMITTED → user lock → fresh now → reread → classify.
/// Rotate: consume once and issue a fresh pair in the same session (COMMIT, 200). Replay outside the grace window: close
/// the session and COMMIT, then 401. Recent replay or anything invalid: roll back, 401.
/// </summary>
public sealed class RefreshTokenCommandHandler(
    IRefreshTokenRepository refreshTokens,
    ILoginRepository logins,
    ITokenService tokenService,
    ITransactionRunner transactions,
    RefreshSettings settings,
    TimeProvider clock) : ICommandHandler<RefreshTokenCommand, TokenPairResponse>
{
    public async Task<Result<TokenPairResponse>> HandleAsync(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var hash = tokenService.Hash(request.RefreshAccessToken!);

        try
        {
            var owner = await refreshTokens.FindOwnerAsync(hash, clock.GetUtcNow().UtcDateTime, cancellationToken);
            if (owner is null) return AuthenErrors.RefreshTokenInvalid;

            // A committed replay revocation is a success of the transaction but still answers 401 (null pair).
            var outcome = await transactions.RunAsync<TokenPairResponse?>(IsolationLevel.ReadCommitted, async ct =>
            {
                var user = await refreshTokens.LockActiveUserAsync(owner.UserId, ct);
                if (user is null) return Result.Failure<TokenPairResponse?>(AuthenErrors.RefreshTokenInvalid);

                var now = clock.GetUtcNow().UtcDateTime; // after the user lock
                var row = await refreshTokens.RereadAsync(owner, hash, ct);
                switch (RefreshTokenReuse.Classify(row, now, settings.ReuseGrace))
                {
                    case RefreshDecision.Rotate:
                        if (!await refreshTokens.ConsumeAsync(row!, hash, now, ct)) return Result.Failure<TokenPairResponse?>(AuthenErrors.RefreshTokenInvalid);
                        return Result.Success<TokenPairResponse?>(await new SessionTokens(logins, tokenService).IssueAsync(user, row!.SessionId, ct));
                    case RefreshDecision.Replay:
                        await refreshTokens.CloseSessionAsync(row!.SessionId, user.Id, now, ct);
                        return Result.Success<TokenPairResponse?>(null);
                    default:
                        return Result.Failure<TokenPairResponse?>(AuthenErrors.RefreshTokenInvalid);
                }
            }, cancellationToken);

            if (outcome.IsFailure) return outcome.FirstError;
            return outcome.Value is { } pair ? pair : AuthenErrors.RefreshTokenInvalid;
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }
}
