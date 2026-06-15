using commerce.Application.Common.Interfaces;
using commerce.Infrastructure.Gateway;
using commerce.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace commerce.Infrastructure;

/// <summary>
/// DI registration for the commerce Infrastructure layer. Registers the commerce data-access
/// surface (<see cref="ICommerceDbContext"/>) over the shared context. Handlers depend on
/// interfaces; no repositories. Mirrors operations.Infrastructure / core.Infrastructure.
/// Call from the host: <c>builder.Services.AddCommerceInfrastructure();</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCommerceInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ICommerceDbContext, CommerceDbContext>();

        // Per-brand gateway-settings TTL cache (SEC-2). Singleton so the cache lives across
        // requests. Registered under BOTH the concrete type (consumed by SettingsFirstPaymentGateway)
        // AND the IGatewaySettingsCache abstraction (consumed by the anonymous Razorpay webhook
        // handler in the Application layer, which cannot reference this Infrastructure type).
        services.AddSingleton<GatewaySettingsCache>();
        services.AddSingleton<IGatewaySettingsCache>(sp => sp.GetRequiredService<GatewaySettingsCache>());

        return services;
    }
}
