using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.UpsertUserRole;

/// <summary>Spec 10 input rules (<see cref="GrantRules"/> with userId and roleId), all before the JWT and SQL.</summary>
public sealed class UpsertUserRoleCommandValidator : IValidator<UpsertUserRoleCommand>
{
    public const string NoDeletedInsert = "Row status must not be DELETED when inserting a user role.";

    public IReadOnlyList<Error> Validate(UpsertUserRoleCommand r)
    {
        var messages = GrantRules.Messages(r.Id, ("userId", r.UserId), ("roleId", r.RoleId), r.RowStatus, NoDeletedInsert);
        return messages.Count == 0 ? [] : [Error.Validation("UserRole.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
