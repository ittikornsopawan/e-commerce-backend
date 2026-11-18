using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Domain;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ecommerce.Infrastructure.Persistence;

/// <summary>
/// <see cref="ITransactionRunner"/> on the scoped <see cref="EcommerceDbContext"/>. Translates PostgreSQL failures into
/// the domain's exceptions: 23505 unique → <see cref="DuplicateKeyException"/>, 23503 foreign key →
/// <see cref="MissingReferenceException"/>, a timeout → <see cref="TimeoutException"/>; 40001 reruns the work, and a
/// 40001 on the last attempt is a <see cref="TimeoutException"/> ("exhaustion follows timeout").
/// </summary>
internal sealed class EfTransactionRunner(EcommerceDbContext db) : ITransactionRunner
{
    private const int MaxAttempts = 3;

    public async Task<Result<T>> RunAsync<T>(
        IsolationLevel isolation, Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(isolation, cancellationToken);
                try
                {
                    var result = await work(cancellationToken);
                    if (result.IsSuccess) await transaction.CommitAsync(cancellationToken);
                    else await transaction.RollbackAsync(cancellationToken);
                    return result;
                }
                catch
                {
                    await RollbackQuietly(transaction);
                    throw;
                }
            }
            catch (Exception exception) when (Find<PostgresException>(exception) is { SqlState: PostgresErrorCodes.SerializationFailure } && attempt < MaxAttempts)
            {
                db.ChangeTracker.Clear();
            }
            catch (Exception exception) when (Translate(exception) is { } translated)
            {
                db.ChangeTracker.Clear();
                throw translated;
            }
        }
    }

    private static Exception? Translate(Exception exception) => Find<PostgresException>(exception) switch
    {
        { SqlState: PostgresErrorCodes.UniqueViolation } e => new DuplicateKeyException(e.ConstraintName ?? "", $"{e.SchemaName}.{e.TableName}", exception),
        // Retries are used up (the catch above reruns earlier attempts): the spec answers it as a timeout.
        { SqlState: PostgresErrorCodes.SerializationFailure } => new TimeoutException("Serialization conflicts on every attempt.", exception),
        { SqlState: PostgresErrorCodes.ForeignKeyViolation } e => new MissingReferenceException(e.ConstraintName ?? "", exception),
        _ when Find<TimeoutException>(exception) is not null => new TimeoutException("The database did not answer in time.", exception),
        _ => null
    };

    private static TException? Find<TException>(Exception? exception) where TException : Exception
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is TException match) return match;
        }

        return null;
    }

    // After a timeout the connection may be broken; the database rolls the transaction back when it closes.
    private static async Task RollbackQuietly(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); }
        catch (Exception) { /* connection already gone */ }
    }
}
