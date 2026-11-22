using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.InquiryUserRole;

/// <summary>Spec 11 query rules (<see cref="GrantInquiryRules"/> with userId and roleId), all before the JWT and SQL.</summary>
public sealed class InquiryUserRoleQueryValidator : IValidator<InquiryUserRoleQuery>
{
    public IReadOnlyList<Error> Validate(InquiryUserRoleQuery q)
    {
        var messages = GrantInquiryRules.Messages(q.Id, ("userId", q.UserId), ("roleId", q.RoleId), q.PageIndex, q.PageSize, q.RowStatus);
        return messages.Count == 0 ? [] : [Error.Validation("UserRole.Query", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
