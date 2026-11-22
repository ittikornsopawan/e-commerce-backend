using System.Text.Json;

namespace Ecommerce.Api.Contracts.Author;

/// <summary>
/// The shape of the POST v1/author/role/permission/upsert body (spec 13): known keys only, no repeated key, string values,
/// and <c>scopes</c> an array of link objects read as the permission links (<see cref="PermissionBody.TryReadLinks"/>).
/// </summary>
public static class RolePermissionBody
{
    private static readonly IReadOnlyDictionary<string, JsonValueKind[]> Fields = new Dictionary<string, JsonValueKind[]>
    {
        ["id"] = JsonBody.StringOrNull,
        ["roleId"] = JsonBody.StringOrNull,
        ["permissionId"] = JsonBody.StringOrNull,
        ["scopes"] = JsonBody.ArrayOnly,
        ["rowStatus"] = JsonBody.StringOrNull
    };

    /// <summary>The shape error message, or null with the grant and its scope items in request order.</summary>
    public static string? TryRead(JsonElement body, out JsonBody grant, out IReadOnlyList<JsonBody> scopes)
    {
        scopes = [];
        var shape = JsonBody.TryRead(body, Fields, out grant, uniqueKeys: true);
        return shape ?? PermissionBody.TryReadLinks(grant, out scopes);
    }
}
