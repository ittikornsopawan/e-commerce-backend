using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Authen.InquiryUserAddress;

/// <summary>Spec 10 query rules, all before the JWT and SQL: id a UUID, checked pagination, rowStatus ACTIVE or INACTIVE.</summary>
public sealed class InquiryUserAddressQueryValidator : IValidator<InquiryUserAddressQuery>
{
    public const long MaxPageSize = 100;
    private static readonly string[] RowStatuses = ["ACTIVE", "INACTIVE"];

    public IReadOnlyList<Error> Validate(InquiryUserAddressQuery q)
    {
        var messages = new List<string>();
        if (q.Id is not null && !Guid.TryParse(q.Id, out _)) messages.Add(Uuid("id"));

        if (Page(PageRequest.TryParse(q.PageIndex, q.PageSize, MaxPageSize, out _), MaxPageSize) is { } page) messages.Add(page);
        if (q.RowStatus is not null && !RowStatuses.Contains(q.RowStatus)) messages.Add(RowStatusFilter(RowStatuses));
        return messages.Count == 0 ? [] : [Error.Validation("Address.Query", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
