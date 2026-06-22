using System.Reflection;
using FluentValidation;
using LaundryGhar.Utilities.CQRS.Extensions;
using Microsoft.Extensions.DependencyInjection;
using operations.Application.Fulfillment;
using operations.Application.Fulfillment.Laundry;
using operations.Application.Fulfillment.Logistics;

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

        // Multi-vertical fulfilment seam (Phase 1). The order state machine now lives in
        // these strategies (keyed by FulfillmentMode); all OrderStateMachine call sites are
        // routed through IFulfillmentStrategyResolver. Add one IFulfillmentStrategy per mode
        // here as packs land (e.g. salon → SalonAppointmentStrategy for "appointment").
        services.AddSingleton<IFulfillmentStrategy, LaundryProcessStrategy>();
        services.AddSingleton<IFulfillmentStrategy, LogisticsPointToPointStrategy>();
        services.AddSingleton<IFulfillmentStrategyResolver, FulfillmentStrategyResolver>();

        return services;
    }
}
