using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Author;

/// <summary>EF Core LINQ for the scope SQL of spec 07-upsert-scope (Database Relations).</summary>
internal sealed class ScopeRepository(EcommerceDbContext db) : IScopeRepository
{
    private const string Deleted = MasterLifecycle.Deleted;

    public async Task<bool> LockAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Scopes
            .Where(s => s.Id == id && s.RowStatus != Deleted)
            .ForUpdate()
            .Select(s => s.Id)
            .ToListAsync(cancellationToken)).Count == 1;

    public Task<bool> CodeTakenAsync(string scope, Guid? excludeId, CancellationToken cancellationToken) =>
        db.Scopes.AnyAsync(s => s.Scope == scope && s.RowStatus != Deleted && (excludeId == null || s.Id != excludeId), cancellationToken);

    public Task<bool> InUseAsync(Guid id, CancellationToken cancellationToken) =>
        db.PermissionScopes.AnyAsync(ps => ps.ScopeId == id && ps.RowStatus != Deleted, cancellationToken);

    public async Task<Guid> InsertAsync(ScopeValues values, string by, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var row = new ScopeRecord
        {
            CreatedBy = by,
            CreatedAt = nowUtc,
            RowStatus = values.RowStatus,
            Scope = values.Scope,
            Name = values.Name,
            Description = values.Description
        };
        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, ScopeValues values, string by, DateTime nowUtc, CancellationToken cancellationToken) =>
        await db.Scopes
            .Where(s => s.Id == id && s.RowStatus != Deleted)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.UpdatedBy, by)
                .SetProperty(s => s.UpdatedAt, nowUtc)
                .SetProperty(s => s.RowStatus, values.RowStatus)
                .SetProperty(s => s.Scope, values.Scope)
                .SetProperty(s => s.Name, values.Name)
                .SetProperty(s => s.Description, values.Description), cancellationToken) == 1;
}
