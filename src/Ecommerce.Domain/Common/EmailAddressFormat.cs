using System.Net.Mail;

namespace Ecommerce.Domain.Common;

/// <summary>
/// The one email rule of api-conventions.md "Email address" (owner decision 2026-11-18): at most 254 characters, exactly
/// one @ after a non-empty local part, a dot in the domain, and a plain address only (no space, display name or angle
/// brackets). It checks validity only; callers store and compare the value as sent.
/// </summary>
public static class EmailAddressFormat
{
    public const int MaxLength = 254;

    public static bool IsValid(string value)
    {
        var at = value.IndexOf('@');
        if (value.Length > MaxLength || at <= 0 || at != value.LastIndexOf('@') || value.IndexOf('.', at) < 0)
            return false;

        // MailAddress accepts a display name, angle brackets or padding and strips them; requiring its Address to equal
        // the input refuses all three, and it refuses an inner space itself.
        return MailAddress.TryCreate(value, out var parsed) && parsed.Address == value;
    }
}
