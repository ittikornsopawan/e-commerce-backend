using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.InquiryRolePermission;

/// <summary>Spec 14 query rules (<see cref="GrantInquiryRules"/> with roleId and permissionId), all before the JWT and SQL.</summary>
public sealed class InquiryRolePermissionQueryValidator : IValidator<InquiryRolePermissionQuery>
{
    public IReadOnlyList<Error> Validate(InquiryRolePermissionQuery q)
    {
        var messages = GrantInquiryRules.Messages(q.Id, ("roleId", q.RoleId), ("permissionId", q.PermissionId), q.PageIndex, q.PageSize, q.RowStatus);
        return messages.Count == 0 ? [] : [Error.Validation("RolePermission.Query", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
