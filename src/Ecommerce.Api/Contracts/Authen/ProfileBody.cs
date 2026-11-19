using System.Text.Json;

namespace Ecommerce.Api.Contracts.Authen;

/// <summary>The shape of the POST v1/user/profile/upsert body (spec 07): no fixed key, the eight fields as string or null.</summary>
public static class ProfileBody
{
    public const string FixedFields = "Username, Email Address and Password cannot be changed here.";
    private static readonly string[] Fixed = ["username", "emailAddress", "password"];

    private static readonly Dictionary<string, JsonValueKind[]> Fields = new[]
    {
        "firstName", "lastName", "displayName", "profilePicturePath", "phoneNumber", "address", "dateOfBirth", "gender"
    }.ToDictionary(f => f, _ => JsonBody.StringOrNull);

    public static string? TryRead(JsonElement body, out JsonBody profile)
    {
        profile = new JsonBody();
        if (body.ValueKind == JsonValueKind.Object && body.EnumerateObject().Any(p => Fixed.Contains(p.Name))) return FixedFields;
        return JsonBody.TryRead(body, Fields, out profile);
    }
}
