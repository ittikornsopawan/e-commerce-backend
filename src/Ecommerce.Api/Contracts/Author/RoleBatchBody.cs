using System.Text.Json;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Api.Contracts.Author;

/// <summary>
/// The shape of the POST v1/author/role/upsert/batch body (spec 02): an object with only <c>roles</c>, an array of 1 to
/// 100 items, each an object of the <see cref="RoleBody"/> keys with no repeated key. Item errors start with <c>roles[i]</c>.
/// </summary>
public static class RoleBatchBody
{
    private static readonly IReadOnlyDictionary<string, JsonValueKind[]> Envelope =
        new Dictionary<string, JsonValueKind[]> { ["roles"] = JsonBody.ArrayOrNull };

    /// <summary>The shape error message, or null with each item read in request order.</summary>
    public static string? TryRead(JsonElement body, out IReadOnlyList<JsonBody> roles)
    {
        roles = [];
        var shape = JsonBody.TryRead(body, Envelope, out var envelope, uniqueKeys: true);
        if (shape is not null) return shape;

        if (envelope.Element("roles") is not { ValueKind: JsonValueKind.Array } array) return Required("roles");
        if (array.GetArrayLength() is < 1 or > 100) return ItemCount("roles");

        return ReadItems(array, out roles);
    }

    private static string? ReadItems(JsonElement array, out IReadOnlyList<JsonBody> roles)
    {
        var read = new List<JsonBody>();
        var messages = new List<string>();
        foreach (var (role, index) in array.EnumerateArray().Select((role, index) => (role, index)))
        {
            if (role.ValueKind != JsonValueKind.Object)
            {
                messages.Add(At($"roles[{index}]", JsonType("role", "an object")));
                continue;
            }

            var message = JsonBody.TryRead(role, RoleBody.Fields, out var item, uniqueKeys: true);
            if (message is null) read.Add(item);
            else messages.Add(At($"roles[{index}]", message));
        }

        roles = read;
        return messages.Count switch { 0 => null, 1 => messages[0], _ => Combine(messages) };
    }
}
