using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.GetUserProfile;

/// <summary>
/// Orchestrates spec 08: one REPEATABLE READ snapshot (no lock, no write) → fresh now and JWT expiry recheck → the user
/// joined with the caller's current access token → only then the information row.
/// </summary>
public sealed class GetUserProfileQueryHandler(
    IUserProfileQueryRepository profiles,
    ITokenService tokenService,
    ITransactionRunner transactions,
    TimeProvider clock) : IQueryHandler<GetUserProfileQuery, UserProfileResponse>
{
    public async Task<Result<UserProfileResponse>> HandleAsync(GetUserProfileQuery request, CancellationToken cancellationToken)
    {
        if (request.Caller is not { } caller) return AuthenErrors.AccessTokenInvalid;
        var hash = tokenService.Hash(caller.AccessToken);

        try
        {
            return await transactions.RunAsync(IsolationLevel.RepeatableRead, async ct =>
            {
                var now = clock.GetUtcNow().UtcDateTime; // just before the first query
                var user = caller.TokenExpiresAtUtc > now ? await profiles.GetUserAsync(caller.UserId, hash, now, ct) : null;
                if (user is null) return Result.Failure<UserProfileResponse>(AuthenErrors.AccessTokenInvalid);

                var information = await profiles.GetInformationAsync(caller.UserId, ct);
                return Result.Success(new UserProfileResponse(
                    caller.UserId, user.EmailAddress, user.FirstName, user.LastName, user.DisplayName, user.ProfilePicturePath,
                    information?.PhoneNumber, information?.Address, information?.DateOfBirth, information?.Gender));
            }, cancellationToken);
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }
}
