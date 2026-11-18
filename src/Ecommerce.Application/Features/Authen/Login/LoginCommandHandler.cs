using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Domain;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.Login;

/// <summary>
/// Orchestrates spec 02-login in its lock order: identifier lock → failure count (REFUSED at 5) → user lock → password
/// → claims → tokens → attempt row → COMMIT. FAILED and REFUSED commit their attempt row and answer the same 401.
/// </summary>
public sealed class LoginCommandHandler(
    ILoginRepository logins,
    IPasswordHasher hasher,
    ITokenService tokenService,
    ITransactionRunner transactions,
    LoginSettings settings,
    TimeProvider clock) : ICommandHandler<LoginCommand, LoginResponse>
{
    // Verified when there is no user or no password, so those cases cost one password check too.
    private static readonly PasswordCredential Decoy = new(new byte[32], new byte[16]);

    public async Task<Result<LoginResponse>> HandleAsync(LoginCommand request, CancellationToken cancellationToken)
    {
        var identifier = request.Username!;
        var password = request.Password!;

        try
        {
            // The attempt row must commit even when the answer is 401, so the work always returns a success result.
            var outcome = await transactions.RunAsync(IsolationLevel.ReadCommitted, async ct =>
            {
                await logins.LockIdentifierAsync(identifier, ct);
                var now = clock.GetUtcNow().UtcDateTime; // after the identifier lock
                var tokens = await AttemptAsync(identifier, password, now, ct);
                return Result.Success(tokens);
            }, cancellationToken);

            return outcome.Value is { } issued
                ? new LoginResponse("Bearer", issued.Access.Value, issued.Refresh.Value, (int)tokenService.AccessTokenLifetime.TotalSeconds)
                : AuthenErrors.InvalidCredentials;
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private sealed record IssuedPair(IssuedToken Access, IssuedToken Refresh);

    private async Task<IssuedPair?> AttemptAsync(string identifier, string password, DateTime now, CancellationToken ct)
    {
        if (LoginThrottle.IsRefused(await logins.CountRecentFailuresAsync(identifier, now - settings.FailureWindow, ct)))
        {
            await logins.RecordAttemptAsync(identifier, null, LoginResult.Refused, now, ct);
            return null;
        }

        var users = await logins.LockActiveUsersAsync(identifier, ct);
        if (users.Count != 1)
        {
            hasher.Verify(password, Decoy); // unknown or ambiguous: never choose an account
            await logins.RecordAttemptAsync(identifier, null, LoginResult.Failed, now, ct);
            return null;
        }

        var user = users[0];
        var credential = await logins.GetCurrentPasswordAsync(user.Id, ct);
        if (!hasher.Verify(password, credential ?? Decoy) || credential is null)
        {
            await logins.RecordAttemptAsync(identifier, user.Id, LoginResult.Failed, now, ct);
            return null;
        }

        var facts = await logins.GetAuthorizationFactsAsync(user.Id, ct);
        var scopes = await logins.GetActiveScopesAsync(AuthorizationClaims.AssignedScopeIds(facts), ct);
        var claims = AuthorizationClaims.Resolve(facts, scopes);

        var access = tokenService.IssueAccessToken(new AccessTokenClaims(user, claims));
        var refresh = tokenService.IssueRefreshToken();
        await logins.AddSessionTokensAsync(new LoginSession(
            user.Id, Guid.NewGuid(), tokenService.Hash(access.Value), access.ExpiresAtUtc, tokenService.Hash(refresh.Value), refresh.ExpiresAtUtc), ct);
        await logins.RecordAttemptAsync(identifier, user.Id, LoginResult.Success, now, ct);
        return new IssuedPair(access, refresh);
    }
}
