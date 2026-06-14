using Microsoft.Extensions.DependencyInjection;
using operations.Application.Repositories;
using operations.Infrastructure.Repositories;

namespace operations.Infrastructure;

/// <summary>
/// DI registration for the operations Infrastructure layer (persistence, gateways, external services).
/// Call from the host: <c>builder.Services.AddOperationsInfrastructure();</c>
/// The generic repository + unit of work come from AddSharedDataModel(); register only
/// feature-specific repositories here.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddOperationsInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IOrderRepository, OrderRepository>();

        return services;
    }
}
