using System.Text.Json;

namespace Ecommerce.Api.Contracts.Author;

/// <summary>The shape of the POST v1/author/scope/upsert body (spec 07): known keys only, every value a string or null.</summary>
public static class ScopeBody
{
    public static readonly IReadOnlyDictionary<string, JsonValueKind[]> Fields = new Dictionary<string, JsonValueKind[]>
    {
        ["id"] = JsonBody.StringOrNull,
        ["scope"] = JsonBody.StringOrNull,
        ["name"] = JsonBody.StringOrNull,
        ["description"] = JsonBody.StringOrNull,
        ["rowStatus"] = JsonBody.StringOrNull
    };
}
