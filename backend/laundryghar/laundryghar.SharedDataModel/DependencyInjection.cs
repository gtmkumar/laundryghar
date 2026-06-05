using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.SharedDataModel.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace laundryghar.SharedDataModel;

/// <summary>
/// DI registration for the shared data model library.
/// Call from your service's Program.cs:
///   builder.Services.AddSharedDataModel(connectionString);
/// An implementation of ICurrentTenant must be registered separately in the consuming service.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="LaundryGharDbContext"/> with Npgsql + NetTopologySuite
    /// and the <see cref="RlsConnectionInterceptor"/>.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    public static IServiceCollection AddSharedDataModel(
        this IServiceCollection services,
        string connectionString)
    {
        // LIFETIME RATIONALE (H1 security fix):
        // RlsConnectionInterceptor must be Scoped (per-request) for two reasons:
        //   1. It captures ICurrentTenant, which is itself Scoped (backed by HttpContext).
        //      A Singleton interceptor would hold a reference to the *first* tenant it resolved
        //      and bleed that tenant's brand_id/user_id into every subsequent request — a
        //      critical cross-tenant data leak under PostgreSQL RLS.
        //   2. Transient doesn't help: EF Core's internal service provider resolves the
        //      interceptor once when building the DbContext options snapshot and caches it
        //      within the context lifetime. Only a Scoped registration paired with
        //      AddDbContext((sp, opts) => ...) guarantees a fresh interceptor (and therefore
        //      a fresh ICurrentTenant snapshot) per DI scope (= per HTTP request).
        //
        // set_config('app.*', value, false) deliberately uses session-level (is_local=false):
        // Npgsql resets connection state on pool return, and ConnectionOpened fires on every
        // logical open, so the session var is always set to the current request's tenant before
        // any SQL executes. No leakage across pooled connections.
        services.AddScoped<RlsConnectionInterceptor>();

        services.AddDbContext<LaundryGharDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseNetTopologySuite();
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            });

            // Resolve the Scoped interceptor from the request-scoped IServiceProvider so
            // each request gets its own instance carrying its own ICurrentTenant.
            options.AddInterceptors(sp.GetRequiredService<RlsConnectionInterceptor>());
        });

        return services;
    }
}
