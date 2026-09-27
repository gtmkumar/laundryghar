using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace laundryghar.Utilities.Authorization.Abac;

public static class AbacServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ABAC engine. Safe to call unconditionally: with <c>Abac:Enabled</c> false
    /// (the default) nothing is evaluated, nothing is logged, and no connection is opened — the
    /// handler short-circuits before it reaches any of this.
    /// </summary>
    /// <param name="connectionString">
    /// The canonical database. Passed explicitly rather than resolved from a DbContext because the
    /// <c>authz</c> schema is deliberately unmapped: policy reads must not be able to join an
    /// application transaction, or a long-running save would hold the policy cache's refresh open.
    /// </param>
    public static IServiceCollection AddAbac(
        this IServiceCollection services, IConfiguration configuration, string? connectionString)
    {
        services.Configure<AbacOptions>(configuration.GetSection(AbacOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        services.TryAddSingleton<IPolicyDecisionPoint, PolicyDecisionPoint>();

        // No connection string: register the engine's pure half only. Everything DB-backed stays
        // unregistered, so the attribute keys it would have supplied are absent and any policy over
        // them denies — the correct direction for a misconfiguration.
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.TryAddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
            services.TryAddSingleton<IAbacConnectionFactory, NpgsqlAbacConnectionFactory>();
            services.TryAddSingleton<IPolicySource, NpgsqlPolicySource>();
            services.TryAddSingleton<IPolicyRepository, PolicyCache>();

            services.TryAddScoped<IAbacSubjectDataSource, NpgsqlAbacSubjectDataSource>();
            services.TryAddScoped<IResourceAttributeResolver, NpgsqlResourceAttributeResolver>();

            services.TryAddScoped<IPolicyAdminService, PolicyAdminService>();

            services.AddSingleton<ChannelDecisionLogWriter>();
            services.AddSingleton<IDecisionLogWriter>(sp => sp.GetRequiredService<ChannelDecisionLogWriter>());
            services.AddHostedService(sp => sp.GetRequiredService<ChannelDecisionLogWriter>());
        }
        else
        {
            services.TryAddSingleton<IPolicyRepository, EmptyPolicyRepository>();
        }

        // Scoped: the attribute bag is memoised for the lifetime of one request.
        services.AddScoped<IAttributeResolver, SubjectAttributeResolver>();
        services.AddScoped<IAttributeResolver, EnvironmentAttributeResolver>();
        services.AddScoped<IAbacAttributeProvider, AbacAttributeProvider>();
        services.AddScoped<IAbacAuthorizationService, AbacAuthorizationService>();
        services.AddScoped<IPolicyFilterBuilder, PolicyFilterBuilder>();

        // Scoped because it resolves the scoped decision service. Registered alongside the existing
        // PermissionHandler, not in place of it: both requirements must pass.
        services.AddScoped<IAuthorizationHandler, AbacAuthorizationHandler>();

        return services;
    }

    /// <summary>Stands in when no connection string is configured. Always empty, which denies.</summary>
    private sealed class EmptyPolicyRepository : IPolicyRepository
    {
        public Task<IReadOnlyList<AbacPolicy>> GetAsync(string resourceType, string action, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AbacPolicy>>([]);

        public void Invalidate() { }
    }
}
