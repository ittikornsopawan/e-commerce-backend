using System.Text.Json;

namespace Ecommerce.Api.Contracts.Author;

/// <summary>
/// The shape of the POST v1/author/permission/upsert body (spec 04): known keys only, no repeated key, string values, and
/// <c>scopes</c> an array (never null) of objects with only id, scopeId and rowStatus. Link errors start with <c>scopes[i]</c>.
/// </summary>
public static class PermissionBody
{
    private static readonly IReadOnlyDictionary<string, JsonValueKind[]> Fields = new Dictionary<string, JsonValueKind[]>
    {
        ["id"] = JsonBody.StringOrNull,
        ["name"] = JsonBody.StringOrNull,
        ["description"] = JsonBody.StringOrNull,
        ["scopes"] = JsonBody.ArrayOnly,
        ["rowStatus"] = JsonBody.StringOrNull
    };

    private static readonly IReadOnlyDictionary<string, JsonValueKind[]> ScopeFields = new Dictionary<string, JsonValueKind[]>
    {
        ["id"] = JsonBody.StringOrNull,
        ["scopeId"] = JsonBody.StringOrNull,
        ["rowStatus"] = JsonBody.StringOrNull
    };

    /// <summary>The shape error message, or null with the permission and its links in request order (none when scopes is omitted).</summary>
    public static string? TryRead(JsonElement body, out JsonBody permission, out IReadOnlyList<JsonBody> scopes)
    {
        scopes = [];
        var shape = JsonBody.TryRead(body, Fields, out permission, uniqueKeys: true);
        if (shape is not null || permission.Element("scopes") is not { } array) return shape;

        return JsonBody.TryReadItems(array, "scopes", "scope", ScopeFields, out scopes);
    }
}
