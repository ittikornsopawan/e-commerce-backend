using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Domain.Author;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.InquiryRolePermission;

/// <summary>Spec 14 in the <see cref="AdminInquiry"/> frame: the shared paged read of non-DELETED grants, then the page's scope links.</summary>
public sealed class InquiryRolePermissionQueryHandler(IRolePermissionQueryRepository grants, AdminInquiry inquiry)
    : IQueryHandler<InquiryRolePermissionQuery, Page<RolePermissionItem>>
{
    public Task<Result<Page<RolePermissionItem>>> HandleAsync(InquiryRolePermissionQuery request, CancellationToken cancellationToken)
    {
        var page = MasterInquiryRules.PageOf(request.PageIndex, request.PageSize);
        var filter = new RolePermissionFilter(GrantInquiryRules.ParseOrNull(request.Id), GrantInquiryRules.ParseOrNull(request.RoleId),
            GrantInquiryRules.ParseOrNull(request.PermissionId), request.RowStatus);
        return inquiry.RunAsync(request.Caller, AuthorErrors.ReadRolePermissionsForbidden, ct => ReadAsync(filter, page, ct), cancellationToken);
    }

    private async Task<Page<RolePermissionItem>> ReadAsync(RolePermissionFilter filter, PageRequest page, CancellationToken ct)
    {
        var rows = await PagedRead.ReadAsync(page, () => grants.CountAsync(filter, ct), (offset, size) => grants.ListAsync(filter, offset, size, ct), r => r);
        var ids = rows.Items.Select(r => r.Id).ToList();
        var scopes = ids.Count == 0 ? [] : await grants.ListScopesAsync(ids, ct);

        // The scope query is ordered by grant, scope code and id, so each grant's links keep that order.
        var items = rows.Items.Select(r => new RolePermissionItem(r.Id, r.RoleId, r.RoleName, r.PermissionId, r.PermissionName,
            scopes.Where(s => s.RolePermissionId == r.Id).Select(s => new RolePermissionScopeItem(s.Id, s.PermissionScopeId, s.ScopeId, s.Scope, s.RowStatus)).ToList(),
            r.RowStatus)).ToList();
        return new Page<RolePermissionItem>(items, rows.PageIndex, rows.PageSize, rows.TotalItems, rows.TotalPages);
    }
}
