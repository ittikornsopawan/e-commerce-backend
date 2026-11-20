using System.Data;
using Ecommerce.Infrastructure.Persistence;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ecommerce.Infrastructure.Tests;

// The READ ONLY transaction of specs 08 and 10 (Josh, 2026-11-20): reads run, any write fails with 25006.
[Collection(PostgresCollection.Name)]
public sealed class EfTransactionRunnerTests(PostgresFixture postgres)
{
    private static Task<Result<int>> Run(EcommerceDbContext db, bool readOnly, string sql) =>
        new EfTransactionRunner(db).RunAsync(IsolationLevel.RepeatableRead,
            async ct => Result.Success(await db.Database.ExecuteSqlRawAsync(sql, ct)), CancellationToken.None, readOnly);

    [Fact]
    public async Task A_read_only_run_reads_and_refuses_any_write()
    {
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        Assert.True((await Run(db, readOnly: true, "SELECT 1")).IsSuccess);

        var write = "UPDATE authen.m_user SET updated_at = updated_at WHERE FALSE";
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => Run(db, readOnly: true, write));
        Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, Find(refused)?.SqlState); // 25006

        Assert.True((await Run(db, readOnly: false, write)).IsSuccess);
    }

    private static PostgresException? Find(Exception? e) =>
        e switch { null => null, PostgresException p => p, _ => Find(e.InnerException) };
}
