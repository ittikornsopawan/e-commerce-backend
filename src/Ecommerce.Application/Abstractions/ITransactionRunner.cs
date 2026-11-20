using System.Data;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Abstractions;

/// <summary>
/// Runs one use case in one database transaction at the spec's isolation level. A success result commits; a failure
/// result rolls back. A serialization conflict reruns the whole work with a new snapshot (bounded); the work must
/// therefore read and write only through the transaction.
/// </summary>
public interface ITransactionRunner
{
    /// <param name="readOnly">The spec's READ ONLY transaction: any write inside it fails (PostgreSQL 25006).</param>
    /// <exception cref="TimeoutException">The database did not answer in time; everything was rolled back.</exception>
    Task<Result<T>> RunAsync<T>(
        IsolationLevel isolation, Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken, bool readOnly = false);
}
