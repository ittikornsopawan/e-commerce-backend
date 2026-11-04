using Ecommerce.Application.Abstractions;

namespace Ecommerce.Infrastructure.Persistence;

internal sealed class UnitOfWork(DbSession session) : IUnitOfWork
{
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        if (session.InTransaction)
        {
            await work(cancellationToken);
            return;
        }

        await session.BeginAsync(cancellationToken);
        try
        {
            await work(cancellationToken);
            await session.CommitAsync(cancellationToken);
        }
        catch
        {
            await session.RollbackAsync();
            throw;
        }
    }
}
