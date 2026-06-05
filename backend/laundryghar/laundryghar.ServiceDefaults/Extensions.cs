using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Aspire service defaults: OpenTelemetry, service discovery, standard resilience, and
/// health-check endpoints. Call <see cref="AddServiceDefaults{TBuilder}"/> immediately after
/// <c>WebApplication.CreateBuilder</c> and <see cref="MapDefaultEndpoints"/> after <c>builder.Build()</c>.
/// These additions are strictly additive — existing health endpoints and middleware are unaffected.
/// </summary>
public static class Extensions
{
    // Aspire dashboard health paths (used to filter these out of OTel traces)
    private const string HealthPath  = "/health";
    private const string AlivePath   = "/alive";

    /// <summary>
    /// Registers OpenTelemetry, service discovery, standard HTTP resilience, and a baseline
    /// liveness health check. Safe to call on any <see cref="IHostApplicationBuilder"/>.
    /// </summary>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Standard resilience pipeline (retry, circuit-breaker, timeout, hedging)
            http.AddStandardResilienceHandler();
            // Resolve http+serviceName:// URIs via Aspire service discovery
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>Configures OpenTelemetry tracing, metrics, and logging with OTLP export.</summary>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes           = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                       .AddAspNetCoreInstrumentation(otel =>
                           // Exclude health / liveness pings from traces to reduce noise
                           otel.Filter = ctx =>
                               !ctx.Request.Path.StartsWithSegments(HealthPath)
                               && !ctx.Request.Path.StartsWithSegments(AlivePath))
                       .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        // OTLP exporter is activated only when the endpoint env var is set (Aspire sets it automatically)
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// Adds a baseline liveness health check ("self") so the Aspire dashboard can detect the
    /// process is alive, without requiring any additional infrastructure dependencies.
    /// </summary>
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps <c>/health</c> (all checks) and <c>/alive</c> (liveness-tagged checks only) in
    /// Development. These are additive to the services' existing <c>/health/ready</c> and
    /// <c>/health/live</c> endpoints — nothing is removed or replaced.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Only expose in Development — health endpoints without auth are a security risk in prod.
        // See https://aka.ms/dotnet/aspire/healthchecks for production guidance.
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthPath);

            app.MapHealthChecks(AlivePath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}
