using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ecommerce.Infrastructure.Persistence.Ef;

/// <summary>
/// Pessimistic row lock for a LINQ query: <c>query.ForUpdate()</c> makes the SELECT end in FOR UPDATE, the lock the
/// specs take (caller user first, then target rows by id). EF Core has no such operator, so the query is tagged and
/// <see cref="ForUpdateInterceptor"/> completes the statement; callers never write SQL.
/// Use it inside a transaction: outside one the lock is released at once.
/// </summary>
public static class ForUpdateExtensions
{
    internal const string Tag = "ecommerce:for-update";

    public static IQueryable<T> ForUpdate<T>(this IQueryable<T> query) => query.TagWith(Tag);
}

public sealed class ForUpdateInterceptor : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Complete(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Complete(command);
        return ValueTask.FromResult(result);
    }

    private static void Complete(DbCommand command)
    {
        if (command.CommandText.Contains(ForUpdateExtensions.Tag, StringComparison.Ordinal))
        {
            command.CommandText = command.CommandText.TrimEnd().TrimEnd(';') + " FOR UPDATE";
        }
    }
}
