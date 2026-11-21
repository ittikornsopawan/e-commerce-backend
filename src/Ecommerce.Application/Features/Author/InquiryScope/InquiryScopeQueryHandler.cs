using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Domain.Author;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.InquiryScope;

/// <summary>Spec 09 in the <see cref="AdminInquiry"/> frame: the shared paged read of non-DELETED scopes, by code then id.</summary>
public sealed class InquiryScopeQueryHandler(IScopeQueryRepository scopes, AdminInquiry inquiry) : IQueryHandler<InquiryScopeQuery, Page<ScopeItem>>
{
    public Task<Result<Page<ScopeItem>>> HandleAsync(InquiryScopeQuery request, CancellationToken cancellationToken)
    {
        var page = MasterInquiryRules.PageOf(request.PageIndex, request.PageSize);
        var filter = MasterInquiryRules.Filter(request.Id, request.Search, request.RowStatus);
        return inquiry.RunAsync(request.Caller, AuthorErrors.ReadScopesForbidden, ct => PagedRead.ReadAsync(
            page, () => scopes.CountAsync(filter, ct), (offset, size) => scopes.ListAsync(filter, offset, size, ct), Map), cancellationToken);
    }

    private static ScopeItem Map(ScopeRow s) => new(s.Id, s.Scope, s.Name, s.Description, s.RowStatus);
}
