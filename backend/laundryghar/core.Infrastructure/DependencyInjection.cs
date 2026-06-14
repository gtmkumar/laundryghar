using core.Application.Common.Interfaces;
using core.Application.Repositories;
using core.Infrastructure.Persistence;
using core.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace core.Infrastructure;

/// <summary>
/// DI registration for the core Infrastructure layer. Registers the core data-access surface
/// (<see cref="ICoreDbContext"/>) and any remaining feature repositories.
/// Call from the host: <c>builder.Services.AddCoreInfrastructure();</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCoreInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ICoreDbContext, CoreDbContext>();
        services.AddScoped<INotificationTemplateRepository, NotificationTemplateRepository>();

        return services;
    }
}
