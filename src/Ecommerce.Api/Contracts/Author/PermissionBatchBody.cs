using System.Text.Json;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Api.Contracts.Author;

/// <summary>One permission item of the batch body with its links, both read in request order.</summary>
public sealed record PermissionBatchJson(JsonBody Permission, IReadOnlyList<JsonBody> Scopes);

/// <summary>
/// The shape of the POST v1/author/permission/upsert/batch body (spec 05): an object with only <c>permissions</c>, an array
/// of 1 to 100 items, each a <see cref="PermissionBody"/> object. Item errors start with <c>permissions[i]</c>, link errors
/// with <c>permissions[i]: scopes[j]</c>.
/// </summary>
public static class PermissionBatchBody
{
    private static readonly IReadOnlyDictionary<string, JsonValueKind[]> Envelope =
        new Dictionary<string, JsonValueKind[]> { ["permissions"] = JsonBody.ArrayOrNull };

    /// <summary>The shape error message, or null with each item read in request order.</summary>
    public static string? TryRead(JsonElement body, out IReadOnlyList<PermissionBatchJson> permissions)
    {
        permissions = [];
        var shape = JsonBody.TryRead(body, Envelope, out var envelope, uniqueKeys: true);
        if (shape is not null) return shape;

        if (envelope.Element("permissions") is not { ValueKind: JsonValueKind.Array } array) return Required("permissions");
        if (array.GetArrayLength() is < 1 or > 100) return ItemCount("permissions");

        shape = JsonBody.TryReadItems(array, "permissions", "permission", PermissionBody.Fields, out var items);
        return shape ?? ReadLinks(items, out permissions);
    }

    private static string? ReadLinks(IReadOnlyList<JsonBody> items, out IReadOnlyList<PermissionBatchJson> permissions)
    {
        var read = new List<PermissionBatchJson>();
        var messages = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            if (PermissionBody.TryReadLinks(items[i], out var scopes) is { } message) messages.Add(At($"permissions[{i}]", message));
            else read.Add(new PermissionBatchJson(items[i], scopes));
        }

        permissions = read;
        return messages.Count switch { 0 => null, 1 => messages[0], _ => Combine(messages) };
    }
}
