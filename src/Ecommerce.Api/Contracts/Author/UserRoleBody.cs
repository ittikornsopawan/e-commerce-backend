using System.Text.Json;

namespace Ecommerce.Api.Contracts.Author;

/// <summary>The shape of the POST v1/author/user/role/upsert body (spec 10): known keys only, every value a string or null.</summary>
public static class UserRoleBody
{
    public static readonly IReadOnlyDictionary<string, JsonValueKind[]> Fields = new Dictionary<string, JsonValueKind[]>
    {
        ["id"] = JsonBody.StringOrNull,
        ["userId"] = JsonBody.StringOrNull,
        ["roleId"] = JsonBody.StringOrNull,
        ["rowStatus"] = JsonBody.StringOrNull
    };
}
