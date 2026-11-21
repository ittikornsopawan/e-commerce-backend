using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.InquiryPermission;

/// <summary>Spec 06 query rules (<see cref="MasterInquiryRules"/>), all before the JWT and SQL.</summary>
public sealed class InquiryPermissionQueryValidator : IValidator<InquiryPermissionQuery>
{
    public IReadOnlyList<Error> Validate(InquiryPermissionQuery q)
    {
        var messages = MasterInquiryRules.Messages(q.Id, q.PageIndex, q.PageSize, q.RowStatus);
        return messages.Count == 0 ? [] : [Error.Validation("Permission.Query", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
