using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.InquiryUserPermission;

/// <summary>Spec 16 query rules (<see cref="GrantInquiryRules"/> with userId and permissionId), all before the JWT and SQL.</summary>
public sealed class InquiryUserPermissionQueryValidator : IValidator<InquiryUserPermissionQuery>
{
    public IReadOnlyList<Error> Validate(InquiryUserPermissionQuery q)
    {
        var messages = GrantInquiryRules.Messages(q.Id, ("userId", q.UserId), ("permissionId", q.PermissionId), q.PageIndex, q.PageSize, q.RowStatus);
        return messages.Count == 0 ? [] : [Error.Validation("UserPermission.Query", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
