using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Domain.Authen.Domain;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.ResetPassword;

/// <summary>
/// Orchestrates spec 03-reset-password: prepare the token for every valid request → autocommit lookup → READ COMMITTED
/// with the user lock and recheck → fresh now → count → refuse or replace → COMMIT → email hand-off. Every business
/// outcome is the same success; the API answers the generic 200.
/// </summary>
public sealed class RequestPasswordResetCommandHandler(
    IPasswordResetRequestRepository resets,
    ITokenService tokenService,
    IEmailSender emailSender,
    ITransactionRunner transactions,
    PasswordResetSettings settings,
    TimeProvider clock) : ICommandHandler<RequestPasswordResetCommand>
{
    public async Task<Result> HandleAsync(RequestPasswordResetCommand request, CancellationToken cancellationToken)
    {
        var email = request.EmailAddress!;
        // The same preparation for every valid request, before the lookup.
        var token = tokenService.IssuePasswordResetToken().Value;
        var tokenHash = tokenService.Hash(token);

        try
        {
            var userId = await resets.FindActiveUserIdAsync(email, cancellationToken);
            if (userId is null) return Result.Success();

            var issued = await transactions.RunAsync(IsolationLevel.ReadCommitted,
                ct => IssueAsync(userId.Value, email, tokenHash, ct), cancellationToken);

            // After COMMIT only; a delivery problem never changes the answer.
            if (issued.Value) await emailSender.SendPasswordResetAsync(email, settings.ResetUrl + token, cancellationToken);
            return Result.Success();
        }
        catch (TimeoutException)
        {
            return Result.Failure(CommonErrors.Timeout);
        }
    }

    /// <summary>One attempt under the user lock: refuse past the hourly limit, else replace the ACTIVE reset. True when issued.</summary>
    private async Task<Result<bool>> IssueAsync(Guid userId, string email, byte[] tokenHash, CancellationToken ct)
    {
        if (!await resets.LockActiveUserAsync(userId, email, ct)) return Result.Success(false);

        var now = clock.GetUtcNow().UtcDateTime; // after the user lock
        if (PasswordResetThrottle.IsRefused(await resets.CountRequestsAsync(userId, now - settings.Window, ct), settings.MaxRequests))
            return Result.Success(false);

        await resets.ReplaceActiveResetAsync(userId, tokenHash, now, now + settings.TokenLifetime, ct);
        return Result.Success(true);
    }
}
