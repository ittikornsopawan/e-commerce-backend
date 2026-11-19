using System.Globalization;
using System.Text.RegularExpressions;

namespace Ecommerce.Domain.Common;

/// <summary>Value rules of the profile fields (spec 07-upsert-user-profile, sequence validation note).</summary>
public static partial class ProfileFieldRules
{
    public static readonly string[] Genders = ["MALE", "FEMALE", "OTHER"];

    /// <summary>8 to 15 digits with an optional leading +.</summary>
    public static bool IsPhoneNumber(string value) => Phone().IsMatch(value);

    /// <summary>A real YYYY-MM-DD date on or before <paramref name="todayUtc"/>.</summary>
    public static bool IsDateOfBirth(string value, DateOnly todayUtc) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date <= todayUtc;

    [GeneratedRegex(@"^\+?[0-9]{8,15}$")]
    private static partial Regex Phone();
}
