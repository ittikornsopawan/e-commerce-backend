using System.Text.Json;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Api.Contracts.Author;

/// <summary>
/// The shape of the POST v1/author/scope/upsert/batch body (spec 08): an object with only <c>scopes</c>, an array of 1 to
/// 100 <see cref="ScopeBody"/> objects with no repeated key. Item errors start with <c>scopes[i]</c>.
/// </summary>
public static class ScopeBatchBody
{
    private static readonly IReadOnlyDictionary<string, JsonValueKind[]> Envelope =
        new Dictionary<string, JsonValueKind[]> { ["scopes"] = JsonBody.ArrayOrNull };

    /// <summary>The shape error message, or null with each item read in request order.</summary>
    public static string? TryRead(JsonElement body, out IReadOnlyList<JsonBody> scopes)
    {
        scopes = [];
        var shape = JsonBody.TryRead(body, Envelope, out var envelope, uniqueKeys: true);
        if (shape is not null) return shape;

        if (envelope.Element("scopes") is not { ValueKind: JsonValueKind.Array } array) return Required("scopes");
        if (array.GetArrayLength() is < 1 or > 100) return ItemCount("scopes");

        return JsonBody.TryReadItems(array, "scopes", "scope", ScopeBody.Fields, out scopes);
    }
}
