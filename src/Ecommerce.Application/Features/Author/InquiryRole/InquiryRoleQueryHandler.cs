using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Domain.Author;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.InquiryRole;

/// <summary>Spec 03 in the <see cref="AdminInquiry"/> frame: the shared paged read of non-DELETED roles, by name then id.</summary>
public sealed class InquiryRoleQueryHandler(IRoleQueryRepository roles, AdminInquiry inquiry) : IQueryHandler<InquiryRoleQuery, Page<RoleItem>>
{
    public Task<Result<Page<RoleItem>>> HandleAsync(InquiryRoleQuery request, CancellationToken cancellationToken)
    {
        var page = MasterInquiryRules.PageOf(request.PageIndex, request.PageSize);
        var filter = MasterInquiryRules.Filter(request.Id, request.Search, request.RowStatus);
        return inquiry.RunAsync(request.Caller, AuthorErrors.ReadRolesForbidden, ct => PagedRead.ReadAsync(
            page, () => roles.CountAsync(filter, ct), (offset, size) => roles.ListAsync(filter, offset, size, ct), Map), cancellationToken);
    }

    private static RoleItem Map(RoleRow r) => new(r.Id, r.Name, r.Description, r.RowStatus);
}
