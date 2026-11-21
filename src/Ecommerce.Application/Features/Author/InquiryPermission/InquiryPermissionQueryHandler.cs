using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Author;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.InquiryPermission;

/// <summary>
/// Orchestrates spec 06 in one REPEATABLE READ READ ONLY snapshot with no lock and no write: the current credential (401),
/// PLATFORM_ADMIN from the token (403) before any permission is read, <see cref="PagedRead"/>, then the page's links and
/// their masters (skipped when there is no link).
/// </summary>
public sealed class InquiryPermissionQueryHandler(
    IPermissionQueryRepository permissions,
    CallerCredential credential,
    ITransactionRunner transactions) : IQueryHandler<InquiryPermissionQuery, Page<PermissionItem>>
{
    public async Task<Result<Page<PermissionItem>>> HandleAsync(InquiryPermissionQuery request, CancellationToken cancellationToken)
    {
        // A missing or malformed roles claim is a bad token: 401 before any SQL.
        if (request.Caller is not { Roles: not null } caller) return AuthenErrors.AccessTokenInvalid;
        var page = MasterInquiryRules.PageOf(request.PageIndex, request.PageSize);
        var filter = MasterInquiryRules.Filter(request.Id, request.Search, request.RowStatus);

        try
        {
            return await transactions.RunAsync(IsolationLevel.RepeatableRead, ct => ReadAsync(caller, filter, page, ct), cancellationToken, readOnly: true);
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private async Task<Result<Page<PermissionItem>>> ReadAsync(Caller caller, MasterFilter filter, PageRequest page, CancellationToken ct)
    {
        var checkedCredential = await credential.CheckAsync(caller, ct);
        if (checkedCredential.IsFailure) return Result.Failure<Page<PermissionItem>>(checkedCredential.FirstError);

        // Permissions describe the security model: refused before the count, so the answer never shows whether one exists.
        if (!caller.IsPlatformAdmin) return Result.Failure<Page<PermissionItem>>(AuthorErrors.ReadPermissionsForbidden);

        var rows = await PagedRead.ReadAsync(
            page, () => permissions.CountAsync(filter, ct), (offset, size) => permissions.ListAsync(filter, offset, size, ct), r => r);
        return await WithScopesAsync(rows, ct);
    }

    private async Task<Page<PermissionItem>> WithScopesAsync(Page<PermissionRow> rows, CancellationToken ct)
    {
        var ids = rows.Items.Select(r => r.Id).ToList();
        var links = ids.Count == 0 ? [] : await permissions.ListLinksAsync(ids, ct);
        var masters = links.Count == 0 ? [] : await permissions.ListScopesAsync(links.Select(l => l.ScopeId).Distinct().ToList(), ct);
        var rank = masters.Select((master, index) => (master, index)).ToDictionary(m => m.master.Id);

        // Every link has a master (a foreign key); a missing one is a defect, never a silently dropped scope.
        if (links.FirstOrDefault(l => !rank.ContainsKey(l.ScopeId)) is { } orphan)
            throw new InvalidOperationException($"Scope {orphan.ScopeId} of link {orphan.Id} was not returned.");

        var items = rows.Items.Select(r => new PermissionItem(r.Id, r.Name, r.Description, ScopesOf(r.Id, links, rank), r.RowStatus)).ToList();
        return new Page<PermissionItem>(items, rows.PageIndex, rows.PageSize, rows.TotalItems, rows.TotalPages);
    }

    /// <summary>A permission's links by the master's code, then master id (the master query's order), then link id (the link query's order; a stable sort keeps it).</summary>
    private static List<PermissionScopeItem> ScopesOf(
        Guid permissionId, IReadOnlyList<PermissionLinkRow> links, Dictionary<Guid, (ScopeMasterRow Master, int Index)> rank) =>
        links.Where(l => l.PermissionId == permissionId)
            .OrderBy(l => rank[l.ScopeId].Index)
            .Select(l => new PermissionScopeItem(l.Id, l.ScopeId, rank[l.ScopeId].Master.Scope, rank[l.ScopeId].Master.Name, l.RowStatus))
            .ToList();
}
