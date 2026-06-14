using Microsoft.Extensions.DependencyInjection;

namespace operations.Application;

/// <summary>
/// DI registration for the operations Application layer.
/// Call from the host: <c>builder.Services.AddOperationsApplication();</c>
/// Repository abstractions live here (interfaces); implementations are registered in
/// operations.Infrastructure. No mediator — handlers/services are invoked directly.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddOperationsApplication(this IServiceCollection services)
    {
        // Register operations application services here as they are added.
        return services;
    }
}
