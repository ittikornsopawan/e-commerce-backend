using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Authen.InquiryUserAddress;

/// <summary>Spec 10 query rules, all before the JWT and SQL: id a UUID, checked pagination, rowStatus ACTIVE or INACTIVE.</summary>
public sealed class InquiryUserAddressQueryValidator : IValidator<InquiryUserAddressQuery>
{
    public const long MaxPageSize = 100;
    public const string PageRange = "Page Index and Page Size must be greater than 0, Page Size must not be greater than 100.";
    public const string PageOverflow = "Page Index is too large for Page Size.";
    public const string RowStatusFilter = "Row status must be one of ACTIVE, INACTIVE.";

    public IReadOnlyList<Error> Validate(InquiryUserAddressQuery q)
    {
        var messages = new List<string>();
        if (q.Id is not null && !Guid.TryParse(q.Id, out _)) messages.Add(Uuid("id"));

        var problem = PageRequest.TryParse(q.PageIndex, q.PageSize, MaxPageSize, out _);
        if (problem == PageProblem.OutOfRange) messages.Add(PageRange);
        if (problem == PageProblem.OffsetOverflow) messages.Add(PageOverflow);

        if (q.RowStatus is not null and not ("ACTIVE" or "INACTIVE")) messages.Add(RowStatusFilter);
        return messages.Count == 0 ? [] : [Error.Validation("Address.Query", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
