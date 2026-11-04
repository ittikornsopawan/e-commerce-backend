namespace Ecommerce.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>Runs <paramref name="work"/> in one database transaction: commit on success, roll back on exception.</summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}
