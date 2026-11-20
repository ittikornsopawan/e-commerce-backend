using Ecommerce.Domain.Author.Domain;
using Ecommerce.Application.Abstractions;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.Common;

/// <summary>
/// The verified JWT of a protected call: its UUID sub, the exact presented token, its exp and its <c>roles</c> claim.
/// <paramref name="Roles"/> is null when the claim is missing or malformed (the author specs answer 401); it may be empty.
/// </summary>
public sealed record Caller(Guid UserId, string AccessToken, DateTime TokenExpiresAtUtc, IReadOnlyList<string>? Roles = null)
{
    /// <summary>Exact, case-sensitive PLATFORM_ADMIN in the token's role snapshot; never read from the author tables.</summary>
    public bool IsPlatformAdmin => Roles?.Contains(RoleCode.PlatformAdmin, StringComparer.Ordinal) == true;
}

/// <summary>
/// The current-credential check of the protected authen APIs (specs 07 to 10), run inside the caller's transaction:
/// lock the ACTIVE user, take a fresh now, recheck the JWT expiry, then the ACTIVE stored access token. Returns now.
/// </summary>
public sealed class CallerCredential(ICallerCredentialRepository credentials, ITokenService tokenService, TimeProvider clock)
{
    public async Task<Result<DateTime>> VerifyAsync(Caller? caller, CancellationToken cancellationToken)
    {
        if (caller is null || !await credentials.LockActiveUserAsync(caller.UserId, cancellationToken))
            return Result.Failure<DateTime>(AuthenErrors.AccessTokenInvalid);

        var now = clock.GetUtcNow().UtcDateTime; // after the user lock
        if (caller.TokenExpiresAtUtc <= now
            || !await credentials.IsCurrentAccessTokenAsync(tokenService.Hash(caller.AccessToken), caller.UserId, now, cancellationToken))
            return Result.Failure<DateTime>(AuthenErrors.AccessTokenInvalid);

        return Result.Success(now);
    }

    /// <summary>The read-only variant (spec 10): no lock; fresh now, JWT expiry recheck, then the joined token and user.</summary>
    public async Task<Result<DateTime>> CheckAsync(Caller caller, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime; // just before the credential read
        if (caller.TokenExpiresAtUtc <= now
            || !await credentials.IsCurrentCredentialAsync(tokenService.Hash(caller.AccessToken), caller.UserId, now, cancellationToken))
            return Result.Failure<DateTime>(AuthenErrors.AccessTokenInvalid);

        return Result.Success(now);
    }
}
