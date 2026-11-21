using System.Text;

namespace Ecommerce.Shared.Validation;

/// <summary>
/// The 400 E10001 message forms of api-conventions.md "Validation messages" (Josh, SA Lead, 2026-11-18). Shared because
/// the API layer (headers, JSON shape) and the Application validators both write them.
/// </summary>
public static class ValidationMessages
{
    /// <summary><c>emailAddress</c> → <c>Email Address</c>; <c>referrerId</c> → <c>Referrer Id</c>.</summary>
    public static string Field(string requestField)
    {
        var words = new StringBuilder();
        foreach (var c in requestField)
        {
            if (char.IsUpper(c) && words.Length > 0) words.Append(' ');
            words.Append(words.Length == 0 ? char.ToUpperInvariant(c) : c);
        }

        return words.ToString();
    }

    public static string Required(params string[] requestFields) => requestFields.Length switch
    {
        0 => throw new ArgumentException("At least one field.", nameof(requestFields)),
        1 => $"{Field(requestFields[0])} is a required field.",
        _ => $"{string.Join(", ", requestFields[..^1].Select(Field))} and {Field(requestFields[^1])} are required fields."
    };

    public static string MaxLength(string requestField, int max) => $"{Field(requestField)} must be at most {max} characters.";

    public static string Uuid(string requestField) => $"{Field(requestField)} must be a valid UUID.";

    public static string Date(string requestField) => $"{Field(requestField)} must be a valid date (YYYY-MM-DD).";

    public static string OneOf(string requestField, params string[] values) =>
        $"{Field(requestField)} must be {string.Join(", ", values[..^1])} or {values[^1]}.";

    public static string Email(string requestField) => $"{Field(requestField)} must be a valid email address.";

    /// <summary><paramref name="jsonType"/> is "an object", "an array", "a boolean" or "a string".</summary>
    public static string JsonType(string requestField, string jsonType) => $"{Field(requestField)} must be {jsonType}.";

    public static string UnknownField(string requestField) => $"{Field(requestField)} is not a known field.";

    /// <summary>The batch size rule of api-conventions: <c>Roles must contain 1 to 100 items.</c></summary>
    public static string ItemCount(string requestField) => $"{Field(requestField)} must contain 1 to 100 items.";

    public static string Duplicated(string requestField) => $"{Field(requestField)} must not be duplicated.";

    /// <summary>Puts the position of an item or object first: <c>consents[1]: ...</c>, <c>addresses: ...</c>.</summary>
    public static string At(string position, string message) => $"{position}: {message}";

    /// <summary>Several failures: every message without its period, joined by ", ", one period at the end.</summary>
    public static string Combine(IEnumerable<string> messages) =>
        string.Join(", ", messages.Select(m => m.TrimEnd('.'))) + ".";
}
