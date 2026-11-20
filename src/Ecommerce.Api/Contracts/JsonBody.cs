using System.Text.Json;
using Ecommerce.Shared;
using Ecommerce.Shared.Validation;

namespace Ecommerce.Api.Contracts;

/// <summary>
/// Reads a request body by JSON key presence (specs 07 and 09): it must be an object, every key must be known and each
/// value must have an allowed JSON kind; with <paramref name="uniqueKeys"/> (the author specs) a repeated key is refused. Returns the shape error message, or null with the present values.
/// </summary>
public sealed class JsonBody
{
    private readonly Dictionary<string, JsonElement> _values = [];

    public static string? TryRead(
        JsonElement body, IReadOnlyDictionary<string, JsonValueKind[]> fields, out JsonBody read, bool uniqueKeys = false)
    {
        read = new JsonBody();
        if (body.ValueKind != JsonValueKind.Object) return ValidationMessages.JsonType("requestBody", "an object");

        var messages = new List<string>();
        foreach (var p in body.EnumerateObject())
        {
            if (!fields.TryGetValue(p.Name, out var kinds)) messages.Add(ValidationMessages.UnknownField(p.Name));
            else if (uniqueKeys && read._values.ContainsKey(p.Name)) messages.Add(ValidationMessages.Duplicated(p.Name));
            else if (!kinds.Contains(p.Value.ValueKind)) messages.Add(ValidationMessages.JsonType(p.Name, kinds.Contains(JsonValueKind.True) ? "a boolean" : "a string"));
            else read._values[p.Name] = p.Value;
        }

        return messages.Count switch { 0 => null, 1 => messages[0], _ => ValidationMessages.Combine(messages) };
    }

    public bool Has(string field) => _values.ContainsKey(field);

    /// <summary>Unset when the key is absent; null when the value is JSON null.</summary>
    public Optional<string?> String(string field) =>
        _values.TryGetValue(field, out var v) ? Optional<string?>.Of(v.ValueKind == JsonValueKind.Null ? null : v.GetString()) : default;

    public bool? Boolean(string field) => _values.TryGetValue(field, out var v) ? v.GetBoolean() : null;

    public static readonly JsonValueKind[] StringOrNull = [JsonValueKind.String, JsonValueKind.Null];
    public static readonly JsonValueKind[] BooleanOnly = [JsonValueKind.True, JsonValueKind.False];
}
