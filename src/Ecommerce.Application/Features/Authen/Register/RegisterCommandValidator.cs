using System.Globalization;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Domain.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Authen.Register;

/// <summary>
/// Input rules of spec 01-register, checked before any database work. Every failed rule is reported in request field
/// order as one 400 E10001 message (api-conventions.md "Validation messages").
/// </summary>
public sealed class RegisterCommandValidator : IValidator<RegisterCommand>
{
    public static readonly string[] Genders = ["MALE", "FEMALE", "OTHER"];

    public IReadOnlyList<Error> Validate(RegisterCommand r)
    {
        var messages = new List<string>();
        AddCredentialMessages(r, messages);
        AddProfileMessages(r, messages);
        if (r.Addresses is { } a) messages.AddRange(AddressMessages(a));
        if (r.Consents is { } consents) messages.AddRange(ConsentMessages(consents));

        return messages.Count switch
        {
            0 => [],
            // A message the spec shows on its own keeps its own text.
            1 when messages[0] == AuthenErrors.RequiredFieldsMissing.Description => [AuthenErrors.RequiredFieldsMissing],
            _ => [Error.Validation("Register.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))]
        };
    }

    /// <summary>username, emailAddress and password: required together, then length, format and password policy.</summary>
    private static void AddCredentialMessages(RegisterCommand r, List<string> messages)
    {
        var missing = new[] { ("username", r.Username), ("emailAddress", r.EmailAddress), ("password", r.Password) }
            .Where(f => string.IsNullOrWhiteSpace(f.Item2)).Select(f => f.Item1).ToArray();
        if (missing.Length > 0) messages.Add(Required(missing));

        AddIfTooLong(messages, "username", r.Username, 128);
        AddIfTooLong(messages, "emailAddress", r.EmailAddress, 254);
        if (!string.IsNullOrWhiteSpace(r.EmailAddress) && r.EmailAddress.Length <= 254 && !EmailAddressFormat.IsValid(r.EmailAddress))
            messages.Add(Email("emailAddress"));
        if (!string.IsNullOrWhiteSpace(r.Password) && PlainPassword.Create(r.Password) is { IsFailure: true } password)
            messages.Add(password.FirstError.Description);
    }

    /// <summary>The optional profile fields and the referrer.</summary>
    private static void AddProfileMessages(RegisterCommand r, List<string> messages)
    {
        AddIfTooLong(messages, "firstName", r.FirstName, 100);
        AddIfTooLong(messages, "lastName", r.LastName, 100);
        AddIfTooLong(messages, "displayName", r.DisplayName, 128);
        if (r.DateOfBirth is not null && !DateOnly.TryParseExact(r.DateOfBirth, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            messages.Add(Date("dateOfBirth"));
        if (r.Gender is not null && !Genders.Contains(r.Gender)) messages.Add(OneOf("gender", Genders));
        if (r.ReferrerId is not null && !Guid.TryParse(r.ReferrerId, out _)) messages.Add(Uuid("referrerId"));
    }

    private static IEnumerable<string> AddressMessages(RegisterAddressInput a)
    {
        var missing = new[]
        {
            ("title", a.Title), ("address", a.Address), ("city", a.City), ("state", a.State), ("postalCode", a.PostalCode), ("country", a.Country)
        }.Where(f => string.IsNullOrWhiteSpace(f.Item2)).Select(f => f.Item1).ToArray();
        if (missing.Length > 0) yield return At("addresses", Required(missing));
        if (a.Title is { Length: > 128 }) yield return At("addresses", MaxLength("title", 128));
    }

    private static IEnumerable<string> ConsentMessages(IReadOnlyList<RegisterConsentInput> consents)
    {
        var seen = new HashSet<Guid>();
        for (var i = 0; i < consents.Count; i++)
        {
            var c = consents[i];
            var at = $"consents[{i}]";
            if (string.IsNullOrWhiteSpace(c.ConsentId)) yield return At(at, Required("consentId"));
            else if (!Guid.TryParse(c.ConsentId, out var id)) yield return At(at, Uuid("consentId"));
            else if (!seen.Add(id)) yield return At(at, Duplicated("consentId"));
            if (string.IsNullOrWhiteSpace(c.ConsentVersion)) yield return At(at, Required("consentVersion"));
            else if (c.ConsentVersion.Length > 8) yield return At(at, MaxLength("consentVersion", 8));
        }
    }

    private static void AddIfTooLong(List<string> messages, string field, string? value, int max)
    {
        if (value is not null && value.Length > max) messages.Add(MaxLength(field, max));
    }
}
