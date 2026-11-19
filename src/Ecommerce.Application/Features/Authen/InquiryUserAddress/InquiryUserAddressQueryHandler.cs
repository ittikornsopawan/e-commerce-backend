using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.InquiryUserAddress;

/// <summary>
/// Orchestrates spec 10 in one REPEATABLE READ snapshot with no lock and no write: the current credential, the count,
/// the page only when the count is positive, a row-count check against the count, then the mapping.
/// </summary>
public sealed class InquiryUserAddressQueryHandler(
    IUserAddressQueryRepository addresses,
    CallerCredential credential,
    ITransactionRunner transactions) : IQueryHandler<InquiryUserAddressQuery, Page<UserAddressItem>>
{
    public async Task<Result<Page<UserAddressItem>>> HandleAsync(InquiryUserAddressQuery request, CancellationToken cancellationToken)
    {
        if (request.Caller is not { } caller) return AuthenErrors.AccessTokenInvalid; // no SQL without a verified JWT
        PageRequest.TryParse(request.PageIndex, request.PageSize, InquiryUserAddressQueryValidator.MaxPageSize, out var page); // validated

        try
        {
            return await transactions.RunAsync(IsolationLevel.RepeatableRead, ct => ReadAsync(caller, Filter(caller, request), page, ct), cancellationToken);
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private async Task<Result<Page<UserAddressItem>>> ReadAsync(Caller caller, AddressFilter filter, PageRequest page, CancellationToken ct)
    {
        var checkedCredential = await credential.CheckAsync(caller, ct);
        if (checkedCredential.IsFailure) return Result.Failure<Page<UserAddressItem>>(checkedCredential.FirstError);

        var total = await addresses.CountAsync(filter, ct);
        var rows = total == 0 ? [] : await addresses.ListAsync(filter, page.Offset, page.PageSize, ct);
        // Count and page come from one snapshot, so a mismatch is a defect: throwing rolls back and answers 500.
        if (rows.Count != page.ExpectedRows(total))
            throw new InvalidOperationException($"The page holds {rows.Count} rows; the count of the same snapshot expects {page.ExpectedRows(total)}.");

        return new Page<UserAddressItem>(rows.Select(Map).ToList(), page.PageIndex, page.PageSize, total, page.TotalPages(total));
    }

    /// <summary>Omitted filters stay null; search becomes one bound ILIKE pattern that keeps the client's % and _.</summary>
    private static AddressFilter Filter(Caller caller, InquiryUserAddressQuery q) =>
        new(caller.UserId, q.Id is null ? null : Guid.Parse(q.Id), q.Search is null ? null : $"%{q.Search}%", q.RowStatus);

    private static UserAddressItem Map(AddressRow r) =>
        new(r.Id, r.Title, r.IsDefault, r.Address, r.City, r.State, r.PostalCode, r.Country, r.RowStatus);
}
