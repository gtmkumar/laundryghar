using Microsoft.Extensions.DependencyInjection;

namespace commerce.Application;

/// <summary>
/// DI registration for the commerce Application layer.
/// Call from the host: <c>builder.Services.AddCommerceApplication();</c>
/// Repository abstractions live here (interfaces); implementations are registered in
/// commerce.Infrastructure. No mediator — handlers/services are invoked directly.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCommerceApplication(this IServiceCollection services)
    {
        // Register commerce application services here as they are added.
        return services;
    }
}
