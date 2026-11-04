using Ecommerce.Shared.Results;

namespace Ecommerce.Shared.Cqrs;

public interface IRequest<TResponse> where TResponse : Result, IFailureFactory<TResponse>;

/// <summary>A state-changing request that returns no value.</summary>
public interface ICommand : IRequest<Result>;

/// <summary>A state-changing request that returns a value.</summary>
public interface ICommand<TValue> : IRequest<Result<TValue>>;

/// <summary>A read-only request. Queries never change state.</summary>
public interface IQuery<TValue> : IRequest<Result<TValue>>;

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result, IFailureFactory<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TValue> : IRequestHandler<TCommand, Result<TValue>>
    where TCommand : ICommand<TValue>;

public interface IQueryHandler<in TQuery, TValue> : IRequestHandler<TQuery, Result<TValue>>
    where TQuery : IQuery<TValue>;

/// <summary>Validates a request before its handler runs. Return an empty list when the request is valid.</summary>
public interface IValidator<in TRequest>
{
    IReadOnlyList<Error> Validate(TRequest request);
}

/// <summary>Single entry point controllers use to run a command or a query.</summary>
public interface IDispatcher
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        where TResponse : Result, IFailureFactory<TResponse>;
}
