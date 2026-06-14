using commerce.Application.Repositories;
using commerce.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace commerce.Infrastructure;

/// <summary>
/// DI registration for the commerce Infrastructure layer (persistence, gateways, external services).
/// Call from the host: <c>builder.Services.AddCommerceInfrastructure();</c>
/// The generic repository + unit of work come from AddSharedDataModel(); register only
/// feature-specific repositories here.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCommerceInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ICouponRepository, CouponRepository>();

        return services;
    }
}
