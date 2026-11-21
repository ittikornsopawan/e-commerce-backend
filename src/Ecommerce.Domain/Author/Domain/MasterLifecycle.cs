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

    /// <summary>The rowStatus filter of the author inquiries (03, 06, 09): every status but DELETED, which is never returned.</summary>
    public static readonly string[] Visible = [Active, "INACTIVE", "LOCKED", Archived];

    /// <summary>True when the target status needs the "not in use" check before the write.</summary>
    public static bool TakesOutOfUse(string rowStatus) => rowStatus is Deleted or Archived;
}

/// <summary>Role rules of spec 01: the role code and the seeded system role, which this API never changes.</summary>
public static class RoleCode
{
    public const string PlatformAdmin = "PLATFORM_ADMIN";

    /// <summary>The role code is an <see cref="UpperCode"/>.</summary>
    public static bool IsValid(string name) => UpperCode.IsValid(name);
}

/// <summary>The code form of roles (01) and scopes (07): capital letters, digits and _, starting with a letter, at most 64; never trimmed.</summary>
public static partial class UpperCode
{
    public static bool IsValid(string code) => Pattern().IsMatch(code);

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Z][A-Z0-9_]{0,63}$")]
    private static partial System.Text.RegularExpressions.Regex Pattern();
}

/// <summary>Permission rules of spec 04: the permission code, such as <c>catalog.manage</c>.</summary>
public static partial class PermissionCode
{
    /// <summary>Lower-case letters, digits and _, at least two parts separated by a dot, at most 64 characters; never trimmed.</summary>
    public static bool IsValid(string name) => name.Length <= 64 && Pattern().IsMatch(name);

    [System.Text.RegularExpressions.GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$")]
    private static partial System.Text.RegularExpressions.Regex Pattern();
}
