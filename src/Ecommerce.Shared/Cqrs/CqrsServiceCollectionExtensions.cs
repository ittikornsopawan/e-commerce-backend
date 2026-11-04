using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Ecommerce.Shared.Cqrs;

public static class CqrsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the dispatcher and every <see cref="IRequestHandler{TRequest,TResponse}"/> and
    /// <see cref="IValidator{TRequest}"/> found in the given assemblies.
    /// </summary>
    public static IServiceCollection AddCqrs(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.AddScoped<IDispatcher, Dispatcher>();

        var concreteTypes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var type in concreteTypes)
        {
            foreach (var contract in type.GetInterfaces().Where(IsHandlerOrValidator))
            {
                services.AddScoped(contract, type);
            }
        }

        return services;
    }

    private static bool IsHandlerOrValidator(Type contract) =>
        contract.IsGenericType &&
        (contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
         contract.GetGenericTypeDefinition() == typeof(IValidator<>));
}
