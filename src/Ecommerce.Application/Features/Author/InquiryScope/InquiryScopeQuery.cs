using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;

namespace Ecommerce.Application.Features.Author.InquiryScope;

/// <summary>GET v1/author/scope (spec 09); query values stay raw strings (null when omitted); the caller is null when the JWT did not verify.</summary>
public sealed record InquiryScopeQuery(
    Caller? Caller, string? Id, string? PageIndex, string? PageSize, string? Search, string? RowStatus) : IQuery<Page<ScopeItem>>;

/// <summary>data.items[] of spec 09, in the spec's field order.</summary>
public sealed record ScopeItem(Guid ScopeId, string Scope, string Name, string Description, string RowStatus);
