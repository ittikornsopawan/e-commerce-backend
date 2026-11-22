using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.InquiryScope;

/// <summary>Spec 09 query rules (<see cref="MasterInquiryRules"/>), all before the JWT and SQL.</summary>
public sealed class InquiryScopeQueryValidator : IValidator<InquiryScopeQuery>
{
    public IReadOnlyList<Error> Validate(InquiryScopeQuery q)
    {
        var messages = MasterInquiryRules.Messages(q.Id, q.PageIndex, q.PageSize, q.RowStatus);
        return messages.Count == 0 ? [] : [Error.Validation("Scope.Query", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
