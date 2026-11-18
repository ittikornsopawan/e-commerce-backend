using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Authen.Domain;

/// <summary>
/// Decides whether the consents of a registration may be stored (spec 01-register, sequence "Validate submitted
/// ids/versions and all applicable required acceptances"). Order: missing consent or referrer (404 E10005), then a
/// displayed version that is not current (400), then a required consent without an explicit true (400).
/// </summary>
public static class RegistrationConsentPolicy
{
    public static Result Check(
        IReadOnlyCollection<ApplicableConsent> applicable, IReadOnlyCollection<SubmittedConsent> submitted, bool referrerFound)
    {
        var current = applicable.ToDictionary(c => c.ConsentId);

        if (!referrerFound || submitted.Any(s => !current.ContainsKey(s.ConsentId)))
        {
            return Result.Failure(AuthenErrors.ConsentOrReferrerNotFound);
        }

        if (submitted.Any(s => current[s.ConsentId].Version != s.Version))
        {
            return Result.Failure(AuthenErrors.ConsentVersionNotCurrent);
        }

        var accepted = submitted.Where(s => s.Result == true).Select(s => s.ConsentId).ToHashSet();
        return applicable.Where(c => c.IsRequired).All(c => accepted.Contains(c.ConsentId))
            ? Result.Success()
            : Result.Failure(AuthenErrors.RequiredConsentNotAccepted);
    }
}
