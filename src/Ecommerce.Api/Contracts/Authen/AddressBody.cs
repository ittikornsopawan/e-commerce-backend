using System.Text.Json;

namespace Ecommerce.Api.Contracts.Authen;

/// <summary>The shape of the POST v1/user/address/upsert body (spec 09): known keys only, isDefault a boolean.</summary>
public static class AddressBody
{
    public static readonly IReadOnlyDictionary<string, JsonValueKind[]> Fields = new Dictionary<string, JsonValueKind[]>
    {
        ["id"] = JsonBody.StringOrNull,
        ["title"] = JsonBody.StringOrNull,
        ["isDefault"] = JsonBody.BooleanOnly,
        ["address"] = JsonBody.StringOrNull,
        ["city"] = JsonBody.StringOrNull,
        ["state"] = JsonBody.StringOrNull,
        ["postalCode"] = JsonBody.StringOrNull,
        ["country"] = JsonBody.StringOrNull,
        ["rowStatus"] = JsonBody.StringOrNull
    };
}
