using Dapper;
using Npgsql;

namespace Ecommerce.Infrastructure.Persistence;

/// <summary>One connection (and at most one open transaction) per request, shared by every repository.</summary>
internal sealed class DbSession(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;

    public bool InTransaction => _transaction is not null;

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        _transaction = await connection.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await _transaction!.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync()
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.RollbackAsync();
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(sql, parameters, _transaction, cancellationToken: cancellationToken));
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<T>(
            new CommandDefinition(sql, parameters, _transaction, cancellationToken: cancellationToken));
    }

    public async Task<T> ExecuteScalarAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return (await connection.ExecuteScalarAsync<T>(
            new CommandDefinition(sql, parameters, _transaction, cancellationToken: cancellationToken)))!;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    private async ValueTask<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken) =>
        _connection ??= await dataSource.OpenConnectionAsync(cancellationToken);
}
