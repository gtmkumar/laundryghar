using core.Application.Common.Interfaces;
using core.Infrastructure.Auth;
using core.Infrastructure.Email;
using core.Infrastructure.Gateway;
using core.Infrastructure.Persistence;
using core.Infrastructure.Services;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Services;
using Microsoft.Extensions.DependencyInjection;

namespace core.Infrastructure;

/// <summary>
/// DI registration for the core Infrastructure layer. Registers the core data-access surface
/// (<see cref="ICoreDbContext"/>) over the shared context. Handlers depend on the interface; no repositories.
/// Call from the host: <c>builder.Services.AddCoreInfrastructure();</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCoreInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ICoreDbContext, CoreDbContext>();
        services.AddScoped<IBrandResolver, BrandResolver>();

        // ICurrentTenant (RLS) is now a cross-cutting registration via AddCurrentTenant() in the
        // host (laundryghar.Utilities.Services.HttpContextCurrentTenant) — shared with Operations.

        // F6: SMTP transport for the Admin Settings "test email" + provisioning flows.
        // Reads brand-scoped SMTP config from kernel.system_settings via ICoreDbContext.
        services.AddScoped<ISettingsMailer, SettingsMailer>();

        // E6: refresh-token root insert (raw SQL for the self-referential family_id FK).
        // Injects the concrete LaundryGharDbContext for Database.ExecuteSqlAsync.
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // Razorpay Payment Links — collect brand platform-tier invoices (reads Razorpay:KeyId/KeySecret).
        services.AddHttpClient("razorpay-core", c => c.BaseAddress = new Uri("https://api.razorpay.com/"));
        services.AddScoped<IRazorpayLinkClient, RazorpayLinkClient>();

        return services;
    }
}
