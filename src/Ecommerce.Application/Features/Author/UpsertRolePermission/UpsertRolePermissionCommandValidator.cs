using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.UpsertRolePermission;

/// <summary>
/// Spec 13 input rules, all before the JWT and SQL: <see cref="GrantRules"/> with roleId and permissionId, then the scope
/// items as the permission links (<see cref="PermissionRules"/>).
/// </summary>
public sealed class UpsertRolePermissionCommandValidator : IValidator<UpsertRolePermissionCommand>
{
    public const string NoDeletedInsert = "Row status must not be DELETED when inserting a role permission.";

    public IReadOnlyList<Error> Validate(UpsertRolePermissionCommand r)
    {
        var messages = GrantRules.Messages(r.Id, ("roleId", r.RoleId), ("permissionId", r.PermissionId), r.RowStatus, NoDeletedInsert);
        messages.AddRange(PermissionRules.LinkMessages(r.Scopes));
        return messages.Count == 0 ? [] : [Error.Validation("RolePermission.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
