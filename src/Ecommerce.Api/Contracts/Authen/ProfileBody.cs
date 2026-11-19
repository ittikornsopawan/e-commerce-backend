using System.Text.Json;
using Ecommerce.Shared;
using Ecommerce.Shared.Validation;

namespace Ecommerce.Api.Contracts.Authen;

/// <summary>
/// Reads the body of POST v1/user/profile/upsert by JSON key presence (spec 07): an absent key is unset, null clears.
/// Shape rules only: an object, no fixed or unknown key, string or null values.
/// </summary>
public sealed class ProfileBody
{
    public const string FixedFields = "Username, Email Address and Password cannot be changed here.";
    public static readonly string[] Fields = ["firstName", "lastName", "displayName", "profilePicturePath", "phoneNumber", "address", "dateOfBirth", "gender"];
    private static readonly string[] Fixed = ["username", "emailAddress", "password"];

    private readonly Dictionary<string, string?> _values = [];

    public Optional<string?> this[string field] => _values.TryGetValue(field, out var v) ? Optional<string?>.Of(v) : default;

    /// <summary>Returns the shape error message, or null when the body is readable.</summary>
    public static string? TryRead(JsonElement body, out ProfileBody profile)
    {
        profile = new ProfileBody();
        if (body.ValueKind != JsonValueKind.Object) return ValidationMessages.JsonType("requestBody", "an object");

        var properties = body.EnumerateObject().ToList();
        if (properties.Any(p => Fixed.Contains(p.Name))) return FixedFields;

        var messages = new List<string>();
        foreach (var p in properties)
        {
            if (!Fields.Contains(p.Name)) messages.Add($"{ValidationMessages.Field(p.Name)} is not a known field.");
            else if (p.Value.ValueKind is JsonValueKind.Null) profile._values[p.Name] = null;
            else if (p.Value.ValueKind is JsonValueKind.String) profile._values[p.Name] = p.Value.GetString();
            else messages.Add(ValidationMessages.JsonType(p.Name, "a string"));
        }

        return messages.Count switch { 0 => null, 1 => messages[0], _ => ValidationMessages.Combine(messages) };
    }
}
