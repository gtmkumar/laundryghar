using System.Reflection;
using FluentValidation;
using LaundryGhar.Utilities.CQRS.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace commerce.Application;

/// <summary>
/// DI registration for the commerce Application layer. Registers the custom CQRS dispatcher +
/// all ICommandHandler/IQueryHandler implementations (via AddCustomCQRS) and FluentValidation validators.
/// Mirrors operations.Application / core.Application. No mediator — handlers are dispatched directly.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCommerceApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddCustomCQRS(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}
