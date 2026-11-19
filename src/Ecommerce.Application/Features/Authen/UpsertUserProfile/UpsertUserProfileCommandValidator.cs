using Ecommerce.Domain.Common;
using Ecommerce.Shared;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Authen.UpsertUserProfile;

/// <summary>Spec 07 field rules for every supplied non-null value; null is allowed for all eight fields.</summary>
public sealed class UpsertUserProfileCommandValidator(TimeProvider clock) : IValidator<UpsertUserProfileCommand>
{
    public const string DateOfBirthMessage = "Date of birth must be a real date on or before today's UTC date (YYYY-MM-DD).";
    public const string PhoneMessage = "Phone Number must be 8 to 15 digits with an optional leading +.";

    public IReadOnlyList<Error> Validate(UpsertUserProfileCommand r)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var messages = new List<string>();
        TooLong(messages, "firstName", r.FirstName, 100);
        TooLong(messages, "lastName", r.LastName, 100);
        TooLong(messages, "displayName", r.DisplayName, 128);
        if (r.PhoneNumber.Value is { } phone && !ProfileFieldRules.IsPhoneNumber(phone)) messages.Add(PhoneMessage);
        if (r.DateOfBirth.Value is { } date && !ProfileFieldRules.IsDateOfBirth(date, today)) messages.Add(DateOfBirthMessage);
        if (r.Gender.Value is { } gender && !ProfileFieldRules.Genders.Contains(gender)) messages.Add(OneOf("gender", ProfileFieldRules.Genders));

        return messages.Count == 0 ? [] : [Error.Validation("Profile.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }

    private static void TooLong(List<string> messages, string field, Optional<string?> value, int max)
    {
        if (value.Value is { Length: var length } && length > max) messages.Add(MaxLength(field, max));
    }
}
