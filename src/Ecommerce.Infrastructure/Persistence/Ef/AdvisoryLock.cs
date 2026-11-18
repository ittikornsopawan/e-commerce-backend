using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Ecommerce.Infrastructure.Persistence.Ef;

/// <summary>
/// PostgreSQL transaction advisory lock, which LINQ cannot express (owner decision 2026-11-18: one fixed, parameterized
/// statement here, like <see cref="ForUpdateExtensions"/>; no raw SQL anywhere else). Held until the transaction ends.
/// </summary>
public static class AdvisoryLock
{
    public static Task AcquireTransactionLockAsync(this DatabaseFacade database, long key, CancellationToken cancellationToken) =>
        database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
}
