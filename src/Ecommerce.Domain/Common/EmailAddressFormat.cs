namespace Ecommerce.Domain.Common;

/// <summary>A valid email address has exactly one @ with a non-empty local part and domain (api-conventions.md, 2026-11-18).</summary>
public static class EmailAddressFormat
{
    public static bool IsValid(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1;
    }
}
