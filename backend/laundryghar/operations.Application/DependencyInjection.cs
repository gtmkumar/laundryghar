using System.Reflection;
using FluentValidation;
using LaundryGhar.Utilities.CQRS.Extensions;
using Microsoft.Extensions.DependencyInjection;
using operations.Application.Fulfillment;
using operations.Application.Fulfillment.Laundry;

namespace operations.Application;

/// <summary>
/// DI registration for the operations Application layer. Registers the custom CQRS dispatcher +
/// all ICommandHandler/IQueryHandler implementations (via AddCustomCQRS) and FluentValidation validators.
/// Mirrors core.Application. No mediator — handlers are dispatched directly.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddOperationsApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddCustomCQRS(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Multi-vertical fulfilment seam (Phase 0). Registered for resolution but not yet
        // wired into the live order path — Phase 1 routes callers through the resolver.
        // Add one IFulfillmentStrategy per vertical here as packs land (salon, logistics).
        services.AddSingleton<IFulfillmentStrategy, LaundryProcessStrategy>();
        services.AddSingleton<IFulfillmentStrategyResolver, FulfillmentStrategyResolver>();

        return services;
    }
}
