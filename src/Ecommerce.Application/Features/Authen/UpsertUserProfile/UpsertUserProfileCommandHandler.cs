using System.Data;
using System.Globalization;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.UpsertUserProfile;

/// <summary>
/// Orchestrates spec 07: READ COMMITTED → current credential (user lock, fresh now, token) → m_user update only when a
/// user field was sent → information insert or update only when an information field was sent → COMMIT → data.userId.
/// </summary>
public sealed class UpsertUserProfileCommandHandler(
    CallerCredential credential,
    IUserProfileRepository profiles,
    ITransactionRunner transactions) : ICommandHandler<UpsertUserProfileCommand, UpsertUserProfileResponse>
{
    public async Task<Result<UpsertUserProfileResponse>> HandleAsync(UpsertUserProfileCommand r, CancellationToken cancellationToken)
    {
        var user = new ProfileUserChanges(r.FirstName, r.LastName, r.DisplayName, r.ProfilePicturePath);
        var information = new ProfileInformationChanges(r.PhoneNumber, r.Address, ToDate(r.DateOfBirth), r.Gender);

        try
        {
            return await transactions.RunAsync(IsolationLevel.ReadCommitted, async ct =>
            {
                var verified = await credential.VerifyAsync(r.Caller, ct);
                if (verified.IsFailure) return Result.Failure<UpsertUserProfileResponse>(verified.FirstError);
                var (userId, now) = (r.Caller!.UserId, verified.Value);

                if (user.Any && !await profiles.UpdateUserAsync(userId, user, now, ct))
                    throw new InvalidOperationException("The locked user row was not updated."); // spec: 500

                if (information.Any)
                {
                    var informationId = await profiles.FindInformationIdAsync(userId, ct);
                    if (informationId is null) await profiles.InsertInformationAsync(userId, information, ct);
                    else if (!await profiles.UpdateInformationAsync(informationId.Value, userId, information, now, ct))
                        throw new InvalidOperationException("The information row was not updated."); // spec: 500
                }

                return Result.Success(new UpsertUserProfileResponse(userId));
            }, cancellationToken);
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    // The validator has checked the format.
    private static Optional<DateOnly?> ToDate(Optional<string?> value) =>
        value.IsSet ? Optional<DateOnly?>.Of(value.Value is null ? null : DateOnly.ParseExact(value.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture)) : default;
}
