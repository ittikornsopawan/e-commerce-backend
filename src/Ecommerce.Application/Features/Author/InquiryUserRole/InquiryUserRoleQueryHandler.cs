using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Domain.Author;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.InquiryUserRole;

/// <summary>Spec 11 in the <see cref="AdminInquiry"/> frame: the shared paged read of non-DELETED assignments by role name, display name, id.</summary>
public sealed class InquiryUserRoleQueryHandler(IUserRoleQueryRepository userRoles, AdminInquiry inquiry)
    : IQueryHandler<InquiryUserRoleQuery, Page<UserRoleItem>>
{
    public Task<Result<Page<UserRoleItem>>> HandleAsync(InquiryUserRoleQuery request, CancellationToken cancellationToken)
    {
        var page = MasterInquiryRules.PageOf(request.PageIndex, request.PageSize);
        var filter = new UserRoleFilter(ParseOrNull(request.Id), ParseOrNull(request.UserId), ParseOrNull(request.RoleId), request.RowStatus);
        return inquiry.RunAsync(request.Caller, AuthorErrors.ReadUserRolesForbidden, ct => PagedRead.ReadAsync(
            page, () => userRoles.CountAsync(filter, ct), (offset, size) => userRoles.ListAsync(filter, offset, size, ct), Map), cancellationToken);
    }

    private static Guid? ParseOrNull(string? id) => id is null ? null : Guid.Parse(id);

    private static UserRoleItem Map(UserRoleListRow r) => new(r.Id, r.UserId, r.UserDisplayName, r.RoleId, r.RoleName, r.RowStatus);
}
