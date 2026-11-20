using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.UpsertRole;

/// <summary>
/// Spec 01 input rules, all before the JWT check and SQL: name and description mandatory and non-blank even for delete
/// and archive, the role code, description at most 256, id a UUID, rowStatus one of the master statuses, no insert-DELETED.
/// </summary>
public sealed class UpsertRoleCommandValidator : IValidator<UpsertRoleCommand>
{
    public const string NameFormat = "Role name must be capital letters, digits and _, starting with a letter, at most 64 characters.";
    public const string NoDeletedInsert = "Row status must not be DELETED when inserting a role.";

    public IReadOnlyList<Error> Validate(UpsertRoleCommand r)
    {
        var messages = new List<string>();
        if (r.Id.IsSet && !Guid.TryParse(r.Id.Value, out _)) messages.Add(Uuid("id"));

        var missing = new[] { ("name", r.Name), ("description", r.Description) }.Where(f => string.IsNullOrWhiteSpace(f.Item2)).Select(f => f.Item1).ToArray();
        if (missing.Length > 0) messages.Add(Required(missing));
        if (!string.IsNullOrWhiteSpace(r.Name) && !RoleCode.IsValid(r.Name)) messages.Add(NameFormat);
        if (r.Description is { Length: > 256 }) messages.Add(MaxLength("description", 256));

        AddStatusMessages(r, messages);
        return messages.Count == 0 ? [] : [Error.Validation("Role.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }

    private static void AddStatusMessages(UpsertRoleCommand r, List<string> messages)
    {
        if (!r.RowStatus.IsSet) return;
        if (!MasterLifecycle.Statuses.Contains(r.RowStatus.Value)) messages.Add(OneOf("rowStatus", MasterLifecycle.Statuses));
        else if (!r.Id.IsSet && r.RowStatus.Value == MasterLifecycle.Deleted) messages.Add(NoDeletedInsert);
    }
}
