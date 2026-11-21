using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.InquiryRole;

/// <summary>Spec 03 query rules, all before the JWT and SQL: id a UUID, checked pagination, rowStatus any visible status.</summary>
public sealed class InquiryRoleQueryValidator : IValidator<InquiryRoleQuery>
{
    public const long MaxPageSize = 100;

    public IReadOnlyList<Error> Validate(InquiryRoleQuery q)
    {
        var messages = new List<string>();
        if (q.Id is not null && !Guid.TryParse(q.Id, out _)) messages.Add(Uuid("id"));
        if (Page(PageRequest.TryParse(q.PageIndex, q.PageSize, MaxPageSize, out _), MaxPageSize) is { } page) messages.Add(page);
        if (q.RowStatus is not null && !MasterLifecycle.Visible.Contains(q.RowStatus)) messages.Add(RowStatusFilter(MasterLifecycle.Visible));

        return messages.Count == 0 ? [] : [Error.Validation("Role.Query", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
