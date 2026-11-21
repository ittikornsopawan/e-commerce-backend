using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.InquiryUserRole;

/// <summary>Spec 11 query rules, all before the JWT and SQL: id, userId and roleId UUIDs, then the <see cref="MasterInquiryRules"/> paging and rowStatus.</summary>
public sealed class InquiryUserRoleQueryValidator : IValidator<InquiryUserRoleQuery>
{
    public IReadOnlyList<Error> Validate(InquiryUserRoleQuery q)
    {
        var messages = new[] { ("id", q.Id), ("userId", q.UserId), ("roleId", q.RoleId) }
            .Where(f => f.Item2 is not null && !Guid.TryParse(f.Item2, out _))
            .Select(f => Uuid(f.Item1))
            .ToList();
        messages.AddRange(MasterInquiryRules.Messages(null, q.PageIndex, q.PageSize, q.RowStatus));
        return messages.Count == 0 ? [] : [Error.Validation("UserRole.Query", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
