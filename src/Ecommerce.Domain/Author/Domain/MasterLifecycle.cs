namespace Ecommerce.Domain.Author.Domain;

/// <summary>
/// The row statuses of the author masters (role, permission, scope; specs 01, 04, 07): DELETED is final, the others are
/// reversible, and DELETED or ARCHIVED takes the row out of use, so it is refused while a binding still uses it.
/// </summary>
public static class MasterLifecycle
{
    public const string Active = "ACTIVE";
    public const string Deleted = "DELETED";
    public const string Archived = "ARCHIVED";
    public static readonly string[] Statuses = [Active, "INACTIVE", Deleted, "LOCKED", Archived];

    /// <summary>True when the target status needs the "not in use" check before the write.</summary>
    public static bool TakesOutOfUse(string rowStatus) => rowStatus is Deleted or Archived;
}

/// <summary>Role rules of spec 01: the role code and the seeded system role, which this API never changes.</summary>
public static partial class RoleCode
{
    public const string PlatformAdmin = "PLATFORM_ADMIN";

    /// <summary>Capital letters, digits and _, starting with a letter, at most 64 characters; never trimmed.</summary>
    public static bool IsValid(string name) => Pattern().IsMatch(name);

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Z][A-Z0-9_]{0,63}$")]
    private static partial System.Text.RegularExpressions.Regex Pattern();
}
