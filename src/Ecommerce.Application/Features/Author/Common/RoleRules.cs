using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.Common;

/// <summary>
/// The input rules of one role (specs 01 and 02), checked before the JWT and SQL: id a UUID; name and description
/// mandatory and non-blank even for delete and archive; the role code; description at most 256; rowStatus one of the
/// master statuses, never DELETED on an insert. Values are not trimmed or case-normalized.
/// </summary>
public static class RoleRules
{
    public const string NameFormat = "Role name must be capital letters, digits and _, starting with a letter, at most 64 characters.";
    public const string NoDeletedInsert = "Row status must not be DELETED when inserting a role.";

    /// <summary>Every broken rule of one role, in request field order; empty when the role is valid.</summary>
    public static List<string> Messages(Optional<string?> id, string? name, string? description, Optional<string?> rowStatus)
    {
        var messages = new List<string>();
        if (id.IsSet && !Guid.TryParse(id.Value, out _)) messages.Add(Uuid("id"));

        var missing = new[] { ("name", name), ("description", description) }
            .Where(f => string.IsNullOrWhiteSpace(f.Item2))
            .Select(f => f.Item1)
            .ToArray();
        if (missing.Length > 0) messages.Add(Required(missing));
        if (!string.IsNullOrWhiteSpace(name) && !RoleCode.IsValid(name)) messages.Add(NameFormat);
        if (description is { Length: > 256 }) messages.Add(MaxLength("description", 256));

        AddStatusMessages(id.IsSet, rowStatus, messages);
        return messages;
    }

    private static void AddStatusMessages(bool hasId, Optional<string?> rowStatus, List<string> messages)
    {
        if (!rowStatus.IsSet) return;
        if (!MasterLifecycle.Statuses.Contains(rowStatus.Value)) messages.Add(OneOf("rowStatus", MasterLifecycle.Statuses));
        else if (!hasId && rowStatus.Value == MasterLifecycle.Deleted) messages.Add(NoDeletedInsert);
    }
}
