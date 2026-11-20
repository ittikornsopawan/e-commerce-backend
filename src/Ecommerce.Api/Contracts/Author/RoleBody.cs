using System.Text.Json;

namespace Ecommerce.Api.Contracts.Author;

/// <summary>The shape of the POST v1/author/role/upsert body (spec 01): known keys only, every value a string or null.</summary>
public static class RoleBody
{
    public static readonly IReadOnlyDictionary<string, JsonValueKind[]> Fields = new Dictionary<string, JsonValueKind[]>
    {
        ["id"] = JsonBody.StringOrNull,
        ["name"] = JsonBody.StringOrNull,
        ["description"] = JsonBody.StringOrNull,
        ["rowStatus"] = JsonBody.StringOrNull
    };
}
