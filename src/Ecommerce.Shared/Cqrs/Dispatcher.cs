using System.Collections.Concurrent;
using Ecommerce.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Ecommerce.Shared.Cqrs;

internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> Executors = new();

    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        where TResponse : Result, IFailureFactory<TResponse>
    {
        ArgumentNullException.ThrowIfNull(request);

        var executor = (RequestExecutor<TResponse>)Executors.GetOrAdd(
            (request.GetType(), typeof(TResponse)),
            static key => Activator.CreateInstance(
                typeof(RequestExecutor<,>).MakeGenericType(key.Request, key.Response))!);

        return executor.ExecuteAsync(request, services, cancellationToken);
    }

    private abstract class RequestExecutor<TResponse>
        where TResponse : Result, IFailureFactory<TResponse>
    {
        public abstract Task<TResponse> ExecuteAsync(
            IRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken);
    }

    private sealed class RequestExecutor<TRequest, TResponse> : RequestExecutor<TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : Result, IFailureFactory<TResponse>
    {
        public override Task<TResponse> ExecuteAsync(
            IRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken)
        {
            var typed = (TRequest)request;

            var errors = services.GetServices<IValidator<TRequest>>()
                .SelectMany(validator => validator.Validate(typed))
                .ToList();

            if (errors.Count > 0)
            {
                return Task.FromResult(TResponse.Failure(errors));
            }

            var handler = services.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
            return handler.HandleAsync(typed, cancellationToken);
        }
    }
}
