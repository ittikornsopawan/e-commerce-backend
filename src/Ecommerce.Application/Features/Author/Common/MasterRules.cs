using Ecommerce.Application.Common;
using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.Common;

/// <summary>What differs between the author masters' input rules: the code rule and its two messages.</summary>
public sealed record MasterCode(Func<string, bool> IsValid, string FormatMessage, string NoDeletedInsertMessage);

/// <summary>
/// The input rules of one author master (role 01/02, permission 04, scope 07), checked before the JWT and SQL: id a UUID;
/// name and description mandatory and non-blank even for delete and archive; the code rule; description at most 256;
/// rowStatus one of the master statuses, never DELETED on an insert. Values are not trimmed or case-normalized.
/// </summary>
public static class MasterRules
{
    /// <summary>Every broken rule, in request field order; empty when the master is valid.</summary>
    public static List<string> Messages(MasterCode code, Optional<string?> id, string? name, string? description, Optional<string?> rowStatus)
    {
        var messages = new List<string>();
        if (id.IsSet && !Guid.TryParse(id.Value, out _)) messages.Add(Uuid("id"));

        var missing = new[] { ("name", name), ("description", description) }
            .Where(f => string.IsNullOrWhiteSpace(f.Item2))
            .Select(f => f.Item1)
            .ToArray();
        if (missing.Length > 0) messages.Add(Required(missing));
        if (!string.IsNullOrWhiteSpace(name) && !code.IsValid(name)) messages.Add(code.FormatMessage);
        if (description is { Length: > 256 }) messages.Add(MaxLength("description", 256));

        AddStatusMessages(code, id.IsSet, rowStatus, messages);
        return messages;
    }

    /// <summary>What an upsert of an existing master did: DELETED deletes, ARCHIVED archives, any other status updates.</summary>
    public static UpsertAction UpdateAction(string rowStatus) => rowStatus switch
    {
        MasterLifecycle.Deleted => UpsertAction.Delete,
        MasterLifecycle.Archived => UpsertAction.Archive,
        _ => UpsertAction.Update
    };

    private static void AddStatusMessages(MasterCode code, bool hasId, Optional<string?> rowStatus, List<string> messages)
    {
        if (!rowStatus.IsSet) return;
        if (!MasterLifecycle.Statuses.Contains(rowStatus.Value)) messages.Add(OneOf("rowStatus", MasterLifecycle.Statuses));
        else if (!hasId && rowStatus.Value == MasterLifecycle.Deleted) messages.Add(code.NoDeletedInsertMessage);
    }
}
