using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Authen;

/// <summary>EF Core LINQ for the two address queries of spec 10; both use the same predicates, no lock, no write.</summary>
internal sealed class UserAddressQueryRepository(EcommerceDbContext db, FieldProtector protector) : IUserAddressQueryRepository
{
    public Task<long> CountAsync(AddressFilter filter, CancellationToken cancellationToken) =>
        Matching(filter).LongCountAsync(cancellationToken);

    public async Task<IReadOnlyList<AddressRow>> ListAsync(AddressFilter filter, long offset, long limit, CancellationToken cancellationToken)
    {
        // shortcut: EF Skip/Take take int; an offset past int.MaxValue is past every row (at most 10 addresses per user).
        var rows = await Matching(filter)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .Skip((int)Math.Min(offset, int.MaxValue))
            .Take((int)limit)
            .Select(a => new { a.Id, a.Title, a.IsDefault, a.Address, a.City, a.State, a.PostalCode, a.Country, a.RowStatus })
            .ToListAsync(cancellationToken);

        return rows.Select(a => new AddressRow(a.Id, a.Title, a.IsDefault, protector.Unprotect(a.Address), protector.Unprotect(a.City),
            protector.Unprotect(a.State), protector.Unprotect(a.PostalCode), protector.Unprotect(a.Country), a.RowStatus)).ToList();
    }

    /// <summary>The caller's non-DELETED addresses; each null filter is disabled, search is one bound ILIKE pattern.</summary>
    private IQueryable<UserAddressRecord> Matching(AddressFilter f) =>
        db.UserAddresses.Where(a => a.UserId == f.UserId
                                    && a.RowStatus != "DELETED"
                                    && (f.Id == null || a.Id == f.Id)
                                    && (f.SearchPattern == null || EF.Functions.ILike(a.Title, f.SearchPattern))
                                    && (f.RowStatus == null || a.RowStatus == f.RowStatus));
}
