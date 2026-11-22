using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.Common;

/// <summary>
/// The input rules of an author grant (user role 10, role permission 13, user permission 15), checked before the JWT and
/// SQL: id a UUID; the two keys of the grant required UUIDs even for delete and archive; rowStatus a master status, never
/// DELETED on an insert.
/// </summary>
public static class GrantRules
{
    /// <summary>Every broken rule in request field order; empty when the grant is valid.</summary>
    public static List<string> Messages(
        Optional<string?> id, (string Field, string? Value) first, (string Field, string? Value) second, Optional<string?> rowStatus, string noDeletedInsert)
    {
        var messages = new List<string>();
        if (id.IsSet && !Guid.TryParse(id.Value, out _)) messages.Add(Uuid("id"));

        var keys = new[] { first, second };
        var missing = keys.Where(k => string.IsNullOrWhiteSpace(k.Value)).Select(k => k.Field).ToArray();
        if (missing.Length > 0) messages.Add(Required(missing));
        messages.AddRange(keys.Where(k => !string.IsNullOrWhiteSpace(k.Value) && !Guid.TryParse(k.Value, out _)).Select(k => Uuid(k.Field)));

        if (rowStatus.IsSet && !MasterLifecycle.Statuses.Contains(rowStatus.Value)) messages.Add(OneOf("rowStatus", MasterLifecycle.Statuses));
        else if (!id.IsSet && rowStatus.IsSet && rowStatus.Value == MasterLifecycle.Deleted) messages.Add(noDeletedInsert);
        return messages;
    }
}
