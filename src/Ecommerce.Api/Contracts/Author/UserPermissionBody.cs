using System.Text.Json;

namespace Ecommerce.Api.Contracts.Author;

/// <summary>The shape of the POST v1/author/user/permission/upsert body (spec 15): known keys only, every value a string or null.</summary>
public static class UserPermissionBody
{
    public static readonly IReadOnlyDictionary<string, JsonValueKind[]> Fields = new Dictionary<string, JsonValueKind[]>
    {
        ["id"] = JsonBody.StringOrNull,
        ["userId"] = JsonBody.StringOrNull,
        ["permissionId"] = JsonBody.StringOrNull,
        ["scopeId"] = JsonBody.StringOrNull,
        ["rowStatus"] = JsonBody.StringOrNull
    };
}
