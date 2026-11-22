using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Domain.Author;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.InquiryUserPermission;

/// <summary>Spec 16 in the <see cref="AdminInquiry"/> frame: the shared paged read of non-DELETED direct grants by permission name, display name, scope, id.</summary>
public sealed class InquiryUserPermissionQueryHandler(IUserPermissionQueryRepository userPermissions, AdminInquiry inquiry)
    : IQueryHandler<InquiryUserPermissionQuery, Page<UserPermissionItem>>
{
    public Task<Result<Page<UserPermissionItem>>> HandleAsync(InquiryUserPermissionQuery request, CancellationToken cancellationToken)
    {
        var page = MasterInquiryRules.PageOf(request.PageIndex, request.PageSize);
        var filter = new UserPermissionFilter(GrantInquiryRules.ParseOrNull(request.Id), GrantInquiryRules.ParseOrNull(request.UserId), GrantInquiryRules.ParseOrNull(request.PermissionId), request.RowStatus);
        return inquiry.RunAsync(request.Caller, AuthorErrors.ReadUserPermissionsForbidden, ct => PagedRead.ReadAsync(
            page, () => userPermissions.CountAsync(filter, ct), (offset, size) => userPermissions.ListAsync(filter, offset, size, ct), Map), cancellationToken);
    }

    private static UserPermissionItem Map(UserPermissionListRow r) =>
        new(r.Id, r.UserId, r.UserDisplayName, r.PermissionId, r.PermissionName, r.PermissionScopeId, r.ScopeId, r.Scope, r.RowStatus);
}
