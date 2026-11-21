using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Author;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.InquiryRole;

/// <summary>
/// Orchestrates spec 03 in one REPEATABLE READ READ ONLY snapshot with no lock and no write: the current credential (401),
/// then PLATFORM_ADMIN from the token (403) before any role is read, then <see cref="PagedRead"/>.
/// </summary>
public sealed class InquiryRoleQueryHandler(
    IRoleQueryRepository roles,
    CallerCredential credential,
    ITransactionRunner transactions) : IQueryHandler<InquiryRoleQuery, Page<RoleItem>>
{
    public async Task<Result<Page<RoleItem>>> HandleAsync(InquiryRoleQuery request, CancellationToken cancellationToken)
    {
        // A missing or malformed roles claim is a bad token: 401 before any SQL.
        if (request.Caller is not { Roles: not null } caller) return AuthenErrors.AccessTokenInvalid;
        PageRequest.TryParse(request.PageIndex, request.PageSize, InquiryRoleQueryValidator.MaxPageSize, out var page); // validated

        try
        {
            return await transactions.RunAsync(IsolationLevel.RepeatableRead, ct => ReadAsync(caller, Filter(request), page, ct), cancellationToken, readOnly: true);
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private async Task<Result<Page<RoleItem>>> ReadAsync(Caller caller, RoleFilter filter, PageRequest page, CancellationToken ct)
    {
        var checkedCredential = await credential.CheckAsync(caller, ct);
        if (checkedCredential.IsFailure) return Result.Failure<Page<RoleItem>>(checkedCredential.FirstError);

        // Roles describe the security model: refused before the count, so the answer never shows whether a role exists.
        if (!caller.IsPlatformAdmin) return Result.Failure<Page<RoleItem>>(AuthorErrors.ReadRolesForbidden);

        return await PagedRead.ReadAsync(
            page, () => roles.CountAsync(filter, ct), (offset, size) => roles.ListAsync(filter, offset, size, ct), Map);
    }

    private static RoleFilter Filter(InquiryRoleQuery q) =>
        new(q.Id is null ? null : Guid.Parse(q.Id), PagedRead.SearchPattern(q.Search), q.RowStatus);

    private static RoleItem Map(RoleRow r) => new(r.Id, r.Name, r.Description, r.RowStatus);
}
