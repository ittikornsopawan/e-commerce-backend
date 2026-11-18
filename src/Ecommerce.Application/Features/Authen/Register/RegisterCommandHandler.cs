using System.Data;
using System.Globalization;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Domain;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Domain;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.Register;

/// <summary>
/// Orchestrates spec 01-register: hash first, then one REPEATABLE READ transaction with the duplicate check (409),
/// the consent and referrer reads, the consent policy and every insert. The runner commits only a success.
/// </summary>
public sealed class RegisterCommandHandler(
    IRegistrationRepository registrations,
    IPasswordHasher hasher,
    ITransactionRunner transactions,
    TimeProvider clock) : ICommandHandler<RegisterCommand, RegisterResponse>
{
    public async Task<Result<RegisterResponse>> HandleAsync(RegisterCommand r, CancellationToken cancellationToken)
    {
        // The validator has run: required values are present and formats parse.
        var credential = hasher.Hash(r.Password!);
        Guid? referrerId = r.ReferrerId is null ? null : Guid.Parse(r.ReferrerId);
        var consents = (r.Consents ?? []).Select(c => new SubmittedConsent(Guid.Parse(c.ConsentId!), c.ConsentVersion!, c.Result)).ToList();

        try
        {
            return await transactions.RunAsync(IsolationLevel.RepeatableRead, async ct =>
            {
                var now = clock.GetUtcNow().UtcDateTime; // captured just before the first read of this attempt

                if (await registrations.IdentifierExistsAsync(r.Username!, r.EmailAddress!, ct))
                    return Result.Failure<RegisterResponse>(AuthenErrors.IdentifierTaken);

                var applicable = await registrations.GetApplicableConsentsAsync(now, ct);
                var referrerFound = referrerId is null || await registrations.UserExistsAsync(referrerId.Value, ct);
                if (RegistrationConsentPolicy.Check(applicable, consents, referrerFound) is { IsFailure: true } rejected)
                    return Result.Failure<RegisterResponse>(rejected.FirstError);

                var userId = await registrations.AddAsync(ToRegistration(r, credential, referrerId, consents, now), ct);
                return Result.Success(new RegisterResponse(userId));
            }, cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            return AuthenErrors.IdentifierTaken;
        }
        catch (MissingReferenceException)
        {
            return AuthenErrors.ConsentOrReferrerNotFound;
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private static NewRegistration ToRegistration(
        RegisterCommand r, PasswordCredential credential, Guid? referrerId, IReadOnlyList<SubmittedConsent> consents, DateTime now) =>
        new(r.Username!, r.EmailAddress!, credential, r.FirstName, r.LastName, r.DisplayName, r.ProfilePicturePath, r.PhoneNumber,
            r.Address, r.DateOfBirth is null ? null : DateOnly.ParseExact(r.DateOfBirth, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            r.Gender, referrerId,
            r.Addresses is { } a ? new RegistrationAddress(a.Title!, a.Address!, a.City!, a.State!, a.PostalCode!, a.Country!) : null,
            consents, now);
}
