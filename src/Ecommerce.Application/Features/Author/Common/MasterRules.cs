using Ecommerce.Application.Common;
using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.Common;

/// <summary>
/// What differs between the author masters' input rules: the code field (<c>name</c> for role and permission,
/// <c>scope</c> for scope), its rule and two messages.
/// </summary>
public sealed record MasterCode(string Field, Func<string, bool> IsValid, string FormatMessage, string NoDeletedInsertMessage);

/// <summary>A required text field of a master and its maximum length, such as the description (256).</summary>
public sealed record MasterText(string Field, string? Value, int MaxLength);

/// <summary>
/// The input rules of one author master (role 01/02, permission 04/05, scope 07/08), checked before the JWT and SQL: id a
/// UUID; the code and every text field mandatory and non-blank even for delete and archive; the code rule; each text at
/// most its length; rowStatus one of the master statuses, never DELETED on an insert. Values are not trimmed or case-normalized.
/// </summary>
public static class MasterRules
{
    /// <summary>Every broken rule, in request field order; empty when the master is valid.</summary>
    public static List<string> Messages(
        MasterCode code, Optional<string?> id, string? codeValue, IReadOnlyList<MasterText> texts, Optional<string?> rowStatus)
    {
        var messages = new List<string>();
        if (id.IsSet && !Guid.TryParse(id.Value, out _)) messages.Add(Uuid("id"));

        var missing = texts.Select(t => (t.Field, t.Value))
            .Prepend((code.Field, codeValue))
            .Where(f => string.IsNullOrWhiteSpace(f.Item2))
            .Select(f => f.Item1)
            .ToArray();
        if (missing.Length > 0) messages.Add(Required(missing));
        if (!string.IsNullOrWhiteSpace(codeValue) && !code.IsValid(codeValue)) messages.Add(code.FormatMessage);
        messages.AddRange(texts.Where(t => t.Value is not null && t.Value.Length > t.MaxLength).Select(t => MaxLength(t.Field, t.MaxLength)));

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
