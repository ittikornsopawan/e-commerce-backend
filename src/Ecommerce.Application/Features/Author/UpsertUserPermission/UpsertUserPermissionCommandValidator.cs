using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.UpsertUserPermission;

/// <summary>Spec 15 input rules, all before the JWT and SQL: <see cref="GrantRules"/> with userId and permissionId, and scopeId a UUID when sent.</summary>
public sealed class UpsertUserPermissionCommandValidator : IValidator<UpsertUserPermissionCommand>
{
    public const string NoDeletedInsert = "Row status must not be DELETED when inserting a user permission.";

    public IReadOnlyList<Error> Validate(UpsertUserPermissionCommand r)
    {
        var messages = GrantRules.Messages(r.Id, ("userId", r.UserId), ("permissionId", r.PermissionId), r.RowStatus, NoDeletedInsert);
        if (r.ScopeId.IsSet && !Guid.TryParse(r.ScopeId.Value, out _)) messages.Add(Uuid("scopeId"));
        return messages.Count == 0 ? [] : [Error.Validation("UserPermission.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
