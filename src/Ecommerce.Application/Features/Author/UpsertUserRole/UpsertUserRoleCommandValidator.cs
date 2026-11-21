using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.UpsertUserRole;

/// <summary>
/// Spec 10 input rules, all before the JWT and SQL: id a UUID; userId and roleId required UUIDs even for delete and archive;
/// rowStatus one of the master statuses, never DELETED on an insert.
/// </summary>
public sealed class UpsertUserRoleCommandValidator : IValidator<UpsertUserRoleCommand>
{
    public const string NoDeletedInsert = "Row status must not be DELETED when inserting a user role.";

    public IReadOnlyList<Error> Validate(UpsertUserRoleCommand r)
    {
        var messages = new List<string>();
        if (r.Id.IsSet && !Guid.TryParse(r.Id.Value, out _)) messages.Add(Uuid("id"));

        var missing = new[] { ("userId", r.UserId), ("roleId", r.RoleId) }.Where(f => string.IsNullOrWhiteSpace(f.Item2)).Select(f => f.Item1).ToArray();
        if (missing.Length > 0) messages.Add(Required(missing));
        messages.AddRange(new[] { ("userId", r.UserId), ("roleId", r.RoleId) }
            .Where(f => !string.IsNullOrWhiteSpace(f.Item2) && !Guid.TryParse(f.Item2, out _))
            .Select(f => Uuid(f.Item1)));

        if (r.RowStatus.IsSet && !MasterLifecycle.Statuses.Contains(r.RowStatus.Value)) messages.Add(OneOf("rowStatus", MasterLifecycle.Statuses));
        else if (!r.Id.IsSet && r.RowStatus.IsSet && r.RowStatus.Value == MasterLifecycle.Deleted) messages.Add(NoDeletedInsert);
        return messages.Count == 0 ? [] : [Error.Validation("UserRole.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
