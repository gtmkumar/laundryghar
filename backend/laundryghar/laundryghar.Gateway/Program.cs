// LaundryGhar — API Gateway
//
// Listening port: http://localhost:8080 (dev)
//
// Responsibilities:
//   - Path-prefix routing via YARP. The 9 path prefixes (+ /mcp) fan in to 3 hosts:
//       /identity, /engagement, /mcp                 → core       @5050
//       /catalog, /orders, /warehouse, /logistics    → operations @5002
//       /commerce, /finance, /analytics              → commerce   @5005
//   - Central CORS (single point for all clients)
//   - Global per-IP rate limiting (fixed-window, 300 req/min)
//   - Security response headers (mirrors ServiceDefaults.UseSecurityHeaders)
//   - Forwarding: Authorization, X-Brand-Id, X-Forwarded-For/Proto/Host (YARP default)
//   - Aggregate health: GET /health/services fans out to each service's /health/ready
//
// ADDITIVE: the 3 consolidated direct ports (:5050, :5002, :5005) remain fully operational.
// Clients can switch from per-service base URLs to a single http://localhost:8080
// without any URL-path changes — the first path segment selects the upstream.

using System.IO.Compression;
using System.Net;
using System.Threading.RateLimiting;
using laundryghar.Gateway;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults (OTel, service discovery, /health + /alive) ────────
builder.AddServiceDefaults();

// ── YARP — load route/cluster config from our strongly-typed config section ───
//
// We build the YARP config programmatically from appsettings.json rather than
// using the built-in YARP config section so that:
//   a) We can strip path prefixes in a type-safe way alongside the routes.
//   b) Cluster destinations can be overridden per-environment without knowing
//      YARP's internal config key names (our keys are simpler).
//
// Routes are statically defined here; cluster destinations come from config so
// that production can point at real hosts via env-var overrides.

var gatewaySection = builder.Configuration.GetSection("Gateway:Clusters");

RouteConfig MakeRoute(string routeId, string pathPrefix, string clusterId) =>
    new()
    {
        RouteId   = routeId,
        ClusterId = clusterId,
        Match     = new RouteMatch { Path = $"/{pathPrefix}/{{**catch-all}}" },
        // Strip the leading prefix segment before forwarding.
        // E.g. /identity/api/v1/auth/login → /api/v1/auth/login at :5050
        Transforms = [new Dictionary<string, string> { ["PathPattern"] = "/{**catch-all}" }]
    };

// Pass-through route: forwards the request path verbatim (no prefix strip).
// Used for the MCP resource server which is mounted at the literal "/mcp" path on
// the core host (app.MapMcp("/mcp")), so the gateway must NOT strip the segment —
// it forwards /mcp and /mcp/... unchanged to http://localhost:5050/mcp.
RouteConfig MakeVerbatimRoute(string routeId, string path, string clusterId) =>
    new()
    {
        RouteId   = routeId,
        ClusterId = clusterId,
        Match     = new RouteMatch { Path = path }
        // No transforms — YARP forwards the matched path as-is.
    };

ClusterConfig MakeCluster(string clusterId)
{
    var address = gatewaySection[$"{clusterId}:Destinations:primary:Address"]
        ?? throw new InvalidOperationException(
            $"Gateway:Clusters:{clusterId}:Destinations:primary:Address is required.");

    return new ClusterConfig
    {
        ClusterId    = clusterId,
        Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["primary"] = new DestinationConfig { Address = address }
        }
    };
}

// Path prefixes are unchanged from the pre-consolidation gateway; only the upstream
// cluster addresses fan in to 3 services (identity/engagement/mcp→core,
// catalog/orders/warehouse/logistics→operations, commerce/finance/analytics→commerce).
var routes = new[]
{
    MakeRoute("identity-route",   "identity",   "identity"),
    MakeRoute("catalog-route",    "catalog",    "catalog"),
    MakeRoute("orders-route",     "orders",     "orders"),
    MakeRoute("warehouse-route",  "warehouse",  "warehouse"),
    MakeRoute("logistics-route",  "logistics",  "logistics"),
    MakeRoute("commerce-route",   "commerce",   "commerce"),
    MakeRoute("finance-route",    "finance",    "finance"),
    MakeRoute("engagement-route", "engagement", "engagement"),
    MakeRoute("analytics-route",  "analytics",  "analytics"),
    // MCP resource server lives at the literal /mcp path on core — forward verbatim.
    MakeVerbatimRoute("mcp-route",        "/mcp",       "mcp"),
    MakeVerbatimRoute("mcp-subpath-route", "/mcp/{**catch-all}", "mcp"),
};

var clusters = new[]
{
    MakeCluster("identity"),
    MakeCluster("catalog"),
    MakeCluster("orders"),
    MakeCluster("warehouse"),
    MakeCluster("logistics"),
    MakeCluster("commerce"),
    MakeCluster("finance"),
    MakeCluster("engagement"),
    MakeCluster("analytics"),
    MakeCluster("mcp"),
};

builder.Services
    .AddReverseProxy()
    .LoadFromMemory(routes, clusters);

// Per-cluster circuit breaker + timeout + concurrency limiter for every proxied request — see
// ResilientForwarderHttpClientFactory for why this is needed (YARP's default HTTP client never
// goes through IHttpClientFactory, so the resilience baseline in ServiceDefaults doesn't apply
// to proxied traffic). Registered after AddReverseProxy() so it overrides YARP's own default
// IForwarderHttpClientFactory registration.
builder.Services.AddSingleton<Yarp.ReverseProxy.Forwarder.IForwarderHttpClientFactory,
    laundryghar.Gateway.ResilientForwarderHttpClientFactory>();

// ── Response compression — single point for all gateway-routed responses ─────
//
// The gateway is the only public entry point (the 3 direct backend ports are
// dev/internal-only per the ADDITIVE note above), so compression is enabled
// here ONLY — not in ServiceDefaults — to avoid double-compressing responses
// that already pass through this proxy. Brotli is preferred (better ratio);
// Gzip is the fallback for clients that only send "Accept-Encoding: gzip".
// EnableForHttps is on: these are JSON API responses (no per-user secrets
// reflected alongside attacker-controlled input), so the BREACH-style risk
// that motivates the framework's HTTPS-off default does not apply here.
// MIME types: framework defaults already include application/json; RFC7807
// problem-details responses (application/problem+json, used by our shared
// ExceptionHandler middleware) are added explicitly since they're not in the
// default list. Already-compressed media (images, etc.) stays untouched —
// those content types aren't in the compressed set.

builder.Services.AddResponseCompression(opts =>
{
    opts.EnableForHttps = true;
    opts.Providers.Add<BrotliCompressionProvider>();
    opts.Providers.Add<GzipCompressionProvider>();
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
    [
        "application/problem+json",
        "application/problem+xml",
    ]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

// ── CORS — single central policy for all gateway-routed clients ───────────────
//
// Dev: allows the two Vite dev server origins (admin-web :5173, pos-web :5174)
//      with credentials so Authorization cookies/headers work.
// Non-dev: origins loaded from Cors:AllowedOrigins config section.
//
// When clients adopt the gateway as their single base URL this becomes the
// only CORS point — individual services keep their own CORS for direct access.

const string GatewayCorsPolicyName = "GatewayCors";

builder.Services.AddCors(opts =>
{
    if (builder.Environment.IsDevelopment())
    {
        opts.AddPolicy(GatewayCorsPolicyName, policy =>
            policy
                .WithOrigins("http://localhost:5173", "http://localhost:5174")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
    }
    else
    {
        var configuredOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        opts.AddPolicy(GatewayCorsPolicyName, policy =>
            policy
                .WithOrigins(configuredOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
    }
});

// ── Global rate limiter — per client-IP, fixed window ─────────────────────────
//
// 300 requests / 60 seconds per IP by default (config-driven).
// Client IP is resolved from X-Forwarded-For when that header is present
// (YARP forwards it by default; the real edge proxy sets it before us in prod).
// Auth paths (/identity/connect/*, /identity/api/v1/auth/*) also hit Identity's
// own stricter limiter — this global cap is an outer backstop only.
//
// On limit breach: HTTP 429 Too Many Requests (standard IETF status).

var rateLimitSection = builder.Configuration.GetSection("RateLimit");
var permitLimit       = rateLimitSection.GetValue<int>("PermitLimit",  300);
var windowSeconds     = rateLimitSection.GetValue<int>("WindowSeconds", 60);

const string GlobalRateLimiterPolicy = "GlobalPerIp";

builder.Services.AddRateLimiter(limiterOpts =>
{
    limiterOpts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    limiterOpts.AddFixedWindowLimiter(GlobalRateLimiterPolicy, opts =>
    {
        opts.PermitLimit         = permitLimit;
        opts.Window              = TimeSpan.FromSeconds(windowSeconds);
        opts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opts.QueueLimit          = 0; // reject immediately when window full
    });

    // Partition per client IP, honouring X-Forwarded-For so the correct IP
    // is used when requests pass through the Aspire / Docker network layer.
    limiterOpts.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        // Try X-Forwarded-For first (set by upstream proxy / YARP itself in prod)
        var forwardedFor = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var clientIp     = !string.IsNullOrWhiteSpace(forwardedFor)
            ? forwardedFor.Split(',')[0].Trim()          // take leftmost (real client)
            : ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit          = permitLimit,
                Window               = TimeSpan.FromSeconds(windowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0
            });
    });
});

// ── Aggregate health HttpClient — used by /health/services endpoint ────────────
//
// Named client "HealthCheck" with a short 3-second timeout per service.
// Services' /health/ready endpoints are probed in parallel.

builder.Services.AddHttpClient(HealthServicesEndpoint.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(3);
});

// ──────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ── Middleware pipeline ────────────────────────────────────────────────────────
//
// Ordering rationale:
//   1. Response compression — must wrap the response stream before anything
//      else writes to it.
//   2. Security headers — added to every response including preflight rejections.
//   3. CORS — must run before rate limiting so OPTIONS preflight is not counted
//      against the per-IP quota and returns correct headers even on rejection.
//   4. Rate limiting — after CORS so preflight never burns the caller's quota.
//   5. YARP proxy — terminal; routes matched requests to upstream services.
//      YARP by default forwards X-Forwarded-For, X-Forwarded-Proto,
//      X-Forwarded-Host and passes through Authorization and X-Brand-Id untouched.

// Rewrite RemoteIpAddress/scheme from X-Forwarded-* (prod/staging, behind the edge proxy).
// Must run first so the per-IP rate limiter and security headers see the real client.
// No-op unless ForwardedHeaders:Enabled = true.
app.UseForwardedHeadersIfEnabled();

// Response compression — must run early, before anything writes the response body.
app.UseResponseCompression();

// No-op in Development (mirrors ServiceDefaults.UseSecurityHeaders behaviour).
app.UseSecurityHeaders();

app.UseCors(GatewayCorsPolicyName);

app.UseRateLimiter();

// ── /health/services — aggregate fan-out endpoint ─────────────────────────────
app.MapHealthServicesEndpoint(builder.Configuration);

// ── Aspire default health endpoints (/health + /alive, Development only) ───────
app.MapDefaultEndpoints();

// ── YARP reverse proxy — terminal middleware ──────────────────────────────────
//
// YARP's default ForwardedHeadersTransform adds X-Forwarded-For, X-Forwarded-Proto,
// X-Forwarded-Host to upstream requests automatically (verify: Yarp.ReverseProxy
// src/ReverseProxy/Transforms/Builder/ForwardedTransformFactory.cs — enabled by default).
// Authorization and X-Brand-Id are passthrough headers (YARP does not strip them).
// Services validate RS256 tokens via Identity JWKS; the gateway never re-issues tokens.
app.MapReverseProxy();

app.Run();
