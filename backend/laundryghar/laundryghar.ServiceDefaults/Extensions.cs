using laundryghar.ServiceDefaults.Secrets;
using laundryghar.ServiceDefaults.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
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
        // ── Secrets abstraction ────────────────────────────────────────────────────────
        // Layer provider-sourced secrets into IConfiguration before the rest of the
        // pipeline reads them. The EnvironmentSecretsProvider (default) contributes
        // nothing, so Development config is byte-for-byte unchanged. File / cloud
        // providers are activated only when Secrets:Provider is explicitly set.
        //
        // Ordering: this config source is appended AFTER appsettings / appsettings.{env},
        // but the host builder re-adds environment variables AFTER Build() is called,
        // so env vars still win over any value this source supplies — which is the
        // correct precedence (env vars / Aspire-injected vars always take priority).
        builder.AddSecretsConfiguration();

        // ── File storage abstraction ───────────────────────────────────────────────────
        // Registers IFileStorageProvider. Defaults to LocalFileStorageProvider in
        // Development (writes to Storage:Local:RootPath). Cloud providers are activated
        // by setting Storage:Provider = s3 | azure-blob (see FileStorageProviderFactory seams).
        builder.AddFileStorage();

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

    /// <summary>
    /// Applies a baseline set of security-hardening response headers for non-Development
    /// environments.
    ///
    /// <para>
    /// Headers applied in non-Development:
    /// <list type="bullet">
    ///   <item><c>Strict-Transport-Security: max-age=31536000; includeSubDomains</c> — instructs
    ///     clients to use HTTPS exclusively for one year. Safe for all JSON API services that sit
    ///     behind a TLS-terminating load balancer in production.</item>
    ///   <item><c>X-Content-Type-Options: nosniff</c> — prevents browsers from MIME-sniffing a
    ///     response away from the declared content-type (mitigates drive-by-download XSS).</item>
    ///   <item><c>X-Frame-Options: DENY</c> — equivalent to <c>frame-ancestors 'none'</c>; blocks
    ///     clickjacking. APIs serving JSON to SPAs have no legitimate use for framing.</item>
    ///   <item><c>Referrer-Policy: strict-origin-when-cross-origin</c> — limits the Referer header
    ///     to the origin only on cross-origin requests, preventing path/query leakage.</item>
    /// </list>
    /// No headers are set in Development so that local tooling (Swagger, Aspire dashboard,
    /// browser DevTools) is unaffected.
    /// </para>
    ///
    /// <para>
    /// Placement: call <b>before</b> <c>UseCors</c> so that security headers are present even
    /// on CORS preflight responses. Must run <b>after</b> <c>UseForwardedHeadersIfEnabled</c>
    /// (pipeline ordering mirrors the Identity service convention).
    /// </para>
    ///
    /// <para>
    /// A Content-Security-Policy is intentionally omitted — these are pure JSON APIs consumed
    /// by SPA and mobile clients; the SPAs enforce their own CSP.
    /// </para>
    /// </summary>
    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        // No-op in Development — keep local tooling unaffected.
        if (app.Environment.IsDevelopment())
            return app;

        app.Use(async (ctx, next) =>
        {
            var headers = ctx.Response.Headers;

            // HSTS: force HTTPS for 1 year; applies to all sub-domains.
            headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";

            // Prevent MIME-type sniffing of API responses.
            headers.XContentTypeOptions = "nosniff";

            // Block framing — APIs have no legitimate embedding use-case.
            headers.XFrameOptions = "DENY";

            // Limit Referer leakage on cross-origin requests.
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            await next(ctx);
        });

        return app;
    }

    /// <summary>
    /// Conditionally applies <see cref="ForwardedHeadersMiddleware"/> based on the
    /// <c>ForwardedHeaders:Enabled</c> configuration flag.
    ///
    /// <para>
    /// When <c>ForwardedHeaders:Enabled = true</c> the middleware rewrites
    /// <c>HttpContext.Connection.RemoteIpAddress</c> from the socket IP to the real
    /// client IP carried in <c>X-Forwarded-For</c>, and <c>Request.Scheme</c> from the
    /// proxy-to-service transport to the value in <c>X-Forwarded-Proto</c>.  This is
    /// required for IP-based rate limiting and redirect-URI construction to behave
    /// correctly behind a load balancer or reverse proxy.
    /// </para>
    ///
    /// <para>
    /// Security note — <c>KnownNetworks</c> and <c>KnownProxies</c> are intentionally
    /// cleared only when the flag is set, so the middleware trusts the <em>first</em>
    /// hop in <c>X-Forwarded-For</c> completely.  This is correct when your edge
    /// proxy rewrites / sanitises the header before forwarding (e.g. AWS ALB, Azure
    /// Application Gateway, nginx with <c>proxy_set_header X-Forwarded-For</c> only).
    /// Do NOT enable this flag if the service is directly internet-exposed without a
    /// trusted proxy — an attacker could spoof the header and bypass IP rate limiting.
    /// </para>
    ///
    /// <para>
    /// Default: <c>false</c> (off in Development — loopback traffic needs no rewrite).
    /// Enable in Production/Staging by setting <c>ForwardedHeaders__Enabled=true</c>.
    /// See <c>PRODUCTION_ENV.md</c> for details.
    /// </para>
    /// </summary>
    public static WebApplication UseForwardedHeadersIfEnabled(this WebApplication app)
    {
        var enabled = app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled");
        if (!enabled)
            return app;

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };

        // Trust the entire X-Forwarded-For chain supplied by the proxy.
        // Only safe when the edge proxy sanitises the header (see XML doc above).
        // KnownIPNetworks / KnownProxies replace the deprecated KnownNetworks / KnownProxies in .NET 10.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        app.UseForwardedHeaders(options);

        return app;
    }

    /// <summary>
    /// Adds the secrets abstraction layer to <paramref name="builder"/>'s configuration
    /// pipeline. The active provider is selected by <c>Secrets:Provider</c>:
    /// <list type="bullet">
    ///   <item><c>env</c> (default) — no-op; existing config sources are unaffected.</item>
    ///   <item><c>file</c> — reads secret files from the directory at <c>Secrets:FilePath</c>.</item>
    ///   <item><c>azure-keyvault</c> / <c>aws-secretsmanager</c> / <c>vault</c> — reserved seams; throw <see cref="NotSupportedException"/> until wired.</item>
    /// </list>
    /// Called automatically by <see cref="AddServiceDefaults{TBuilder}"/>; no per-service
    /// call is needed.
    /// </summary>
    public static TBuilder AddSecretsConfiguration<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var provider = SecretsProviderFactory.Create(builder.Configuration);
        // IConfigurationManager implements IConfigurationBuilder; .Add() is the standard
        // method to append a new IConfigurationSource.
        ((IConfigurationBuilder)builder.Configuration).Add(new SecretsConfigurationSource(provider));
        return builder;
    }
}
