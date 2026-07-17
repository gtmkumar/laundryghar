// ─────────────────────────────────────────────────────────────────────────────
// commerce.WebApi — consolidated host (Commerce + Finance + Analytics)
//
// Listening port: http://localhost:5242 (dev; see launchSettings.json)
//
// Composition root: wires the commerce bounded-context layers
//   • AddCommerceApplication()    → use cases / handlers / validators (commerce.Application)
//   • AddCommerceInfrastructure() → persistence (ICommerceDbContext over the shared context)
// plus Aspire ServiceDefaults (OpenTelemetry, health checks, service discovery, resilience).
//
// Auth: validate-only. This host does NOT issue tokens — it validates Identity-issued RS256
// JWTs via the Identity JWKS endpoint (Jwt:Authority). JWKS is fetched lazily on first auth
// request, so the host boots even when Identity is not yet running.
// ─────────────────────────────────────────────────────────────────────────────

using System.Net.Http.Headers;
using System.Reflection;
using commerce.Application;
using commerce.Application.Common.Interfaces;
using commerce.Infrastructure;
using commerce.Infrastructure.Gateway;
using commerce.Infrastructure.Worker;
using commerce.Infrastructure.Worker.Abstractions;
using commerce.Infrastructure.Worker.Channels;
using commerce.Infrastructure.Worker.Options;
using commerce.Infrastructure.Worker.Services;
using commerce.Infrastructure.Worker.Stubs;
using laundryghar.SharedDataModel;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Auth;
using laundryghar.Utilities.Auth.Audit;
using laundryghar.Utilities.Caching;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Middlewares.ExceptionsMiddleware;
using laundryghar.Utilities.OpenApi;
using laundryghar.Utilities.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults (OTel, service discovery, /health + /alive) ─────────
builder.AddServiceDefaults();

// ── Current user + current tenant (from request principal / JWT claims) ────────
// AddCurrentUser registers IHttpContextAccessor + ICurrentUser.
//
// ICurrentTenant: this host runs BOTH the HTTP request lanes AND the in-process Worker
// hosted services in one container, so it uses the DISPATCHING CommerceHostCurrentTenant
// (commerce.Infrastructure.Worker) rather than the plain HttpContextCurrentTenant. It serves
// HTTP scopes from JWT claims (RLS enforced) and grants BypassRls only inside a positively
// marked worker scope (WorkerScope.CreateWorkerAsyncScope). One Scoped ICurrentTenant backs
// the RLS interceptor; without it DI scope validation fails at builder.Build().
builder.Services.AddCurrentUser();
builder.Services.AddAuditTrail(); // RBAC audit trail: interceptor + IAuditWriter
builder.Services.AddScoped<ICurrentTenant, CommerceHostCurrentTenant>();

// ── Shared data model: LaundryGharDbContext (+ RLS interceptor wiring) ─────────
// connStr may be empty — the host must still boot with no ConnectionStrings:Default set.
var connStr = builder.Configuration.GetConnectionString("Default") ?? string.Empty;
builder.Services.AddSharedDataModel(
    connStr,
    builder.Configuration,
    builder.Environment);

// ── Commerce bounded-context composition ──────────────────────────────────────
builder.Services
    .AddCommerceApplication()        // validators + command/query handlers (no mediator)
    .AddCommerceInfrastructure();    // ICommerceDbContext + GatewaySettingsCache (per-brand, SEC-2)

// ── Payment gateway (IPaymentGateway) ──────────────────────────────────────────
// Development short-circuits to a stub (no real Razorpay calls, signatures auto-pass).
// Non-Development uses the settings-first wrapper: it resolves per-brand creds from
// kernel.system_settings (via GatewaySettingsCache) and falls back to env config, then
// builds a RazorpayPaymentGateway around the named "razorpay" HttpClient. Fail-closed when
// neither source has credentials. Mirrors the legacy laundryghar.Commerce Program.cs block.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IPaymentGateway, DevPaymentGateway>();
}
else
{
    builder.Services.Configure<RazorpaySettings>(
        builder.Configuration.GetSection(RazorpaySettings.SectionName));

    builder.Services.AddHttpClient("razorpay", http =>
    {
        http.BaseAddress = new Uri("https://api.razorpay.com/");
        http.Timeout     = TimeSpan.FromSeconds(30);
    })
    // Bounded concurrency + a circuit breaker sized for real payment-call volume (not the
    // library's ~100-sample default, which would rarely engage here) — a Razorpay outage fails
    // fast instead of piling up threads/connections across concurrent checkout requests.
    .AddExternalDependencyResilience(
        attemptTimeout: TimeSpan.FromSeconds(8),
        totalRequestTimeout: TimeSpan.FromSeconds(20),
        concurrencyLimit: 15,
        circuitBreakerMinimumThroughput: 6,
        circuitBreakerBreakDuration: TimeSpan.FromSeconds(20));

    // Scoped: each request gets a fresh ICommerceDbContext scope for the per-brand settings read.
    builder.Services.AddScoped<IPaymentGateway, SettingsFirstPaymentGateway>();
}

// ── RaaS partner-billing Razorpay Payment Links (invoices + wallet top-ups, FULL-10) ──
// Unconditional (no DB access at startup; mirrors core's razorpay-core registration). Resolves the
// PLATFORM gateway credentials settings-first (payment/platform_gateway) → env Razorpay:KeyId/KeySecret.
// Consumed by the partner invoice-pay / wallet top-up-via-link handlers AND the partner paylink webhook.
builder.Services.AddHttpClient("razorpay-partner", c => c.BaseAddress = new Uri("https://api.razorpay.com/"))
    .AddExternalDependencyResilience(
        attemptTimeout: TimeSpan.FromSeconds(8),
        totalRequestTimeout: TimeSpan.FromSeconds(20),
        concurrencyLimit: 15,
        circuitBreakerMinimumThroughput: 6,
        circuitBreakerBreakDuration: TimeSpan.FromSeconds(20));
builder.Services.AddScoped<IPartnerPaymentLinkClient, PartnerRazorpayLinkClient>();

// ── OpenAPI document (+ Bearer scheme & standard error responses) ──────────────
builder.Services.AddDefaultOpenApi();

// ── JWT Authentication (validate-only; Identity-issued RS256 via JWKS) ─────────
// Read the three Jwt values directly (commerce cannot reference core.Application's JwtSettings).
// Authority is REQUIRED — it is the Identity base URL whose JWKS publishes the RS256 public key.
// Pin to RS256 to reject "none"/HMAC algorithm-confusion attacks.
var jwtAuthority = builder.Configuration["Jwt:Authority"];
if (string.IsNullOrWhiteSpace(jwtAuthority))
    throw new InvalidOperationException(
        "Jwt:Authority (the Identity issuer base URL whose JWKS publishes the RS256 public key) is required.");

var jwtIssuer   = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.Authority            = jwtAuthority;
        // Default: require https for the JWKS fetch outside Development. Overridable via
        // Jwt:RequireHttpsMetadata=false for private-network deployments (docker compose)
        // where the Identity host is reached over plain http on an internal network.
        opts.RequireHttpsMetadata = builder.Configuration.GetValue(
            "Jwt:RequireHttpsMetadata", !builder.Environment.IsDevelopment());

        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = jwtIssuer,
            ValidateAudience         = true,
            ValidAudience            = jwtAudience,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ClockSkew                = TimeSpan.FromSeconds(30),
            // Pin to RS256 — reject "none" and HMAC algorithm-confusion attacks.
            ValidAlgorithms          = [SecurityAlgorithms.RsaSha256]
        };
    });

// ── Authorization — shared permission policy provider + handlers ───────────────
// PermissionPolicyProvider resolves dynamic "permission:<code>", "permission:<a>|<b>" (any-of),
// and "CustomerOnly" policies; handlers evaluate them against the JWT claims. Commerce serves
// both admin (permission:*) and customer (CustomerOnly) lanes; no rider lane here.
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, AnyPermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, CustomerOnlyHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, PartnerOnlyHandler>(); // RaaS partner lane (wallet/invoices)
builder.Services.AddSingleton<IAuthorizationHandler, PartnerAdminHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
// §8 step-up: convert a step-up policy denial into a structured 403 step_up_required.
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, StepUpAuthorizationResultHandler>();
builder.Services.AddAuthorization();

// ── Output caching (customer plan/package listings; tenant-keyed, tag-evicted on admin writes) ─
builder.Services.AddSharedOutputCache();

// ─────────────────────────────────────────────────────────────────────────────
// Worker lane (in-process background services migrated from laundryghar.Worker).
//
// The channel-sender / options / charger DI below is UNCONDITIONAL — none of it
// touches the database at startup. Only the AddHostedService registrations (which
// open a DbContext per tick) are gated on a configured ConnectionStrings:Default,
// mirroring the IdentitySeeder gate in core: with no DB the host still boots clean.
// HostOptions.BackgroundServiceExceptionBehavior = Ignore is defence-in-depth so a
// transient worker failure can never take the host down.
// ─────────────────────────────────────────────────────────────────────────────

// ── Worker: defence-in-depth — a faulting BackgroundService must NOT stop the host.
builder.Services.Configure<HostOptions>(o =>
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

// ── Worker: notification provider options ──────────────────────────────────────
builder.Services.Configure<WorkerOptions>(
    builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.Configure<WhatsAppOptions>(
    builder.Configuration.GetSection(WhatsAppOptions.SectionName));
builder.Services.Configure<SmsOptions>(
    builder.Configuration.GetSection(SmsOptions.SectionName));
builder.Services.Configure<PushOptions>(
    builder.Configuration.GetSection(PushOptions.SectionName));

// ── Worker: notification provider HTTP clients ─────────────────────────────────
// Tuned resilience per channel: bounded concurrency + a circuit breaker sized for real
// per-poll-cycle volume (WorkerOptions batch sizes are ~20, not the library default's
// ~100-sample window) so a degraded provider fails fast — each failed send is already
// retried with backoff by NotificationDispatcherService, so a fast failure here just lets
// that existing retry/dead-letter logic kick in sooner instead of hanging the poll cycle.
builder.Services.AddHttpClient("whatsapp", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    var accessToken = builder.Configuration["Notifications:WhatsApp:AccessToken"];
    if (!string.IsNullOrWhiteSpace(accessToken))
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
})
.AddExternalDependencyResilience(
    attemptTimeout: TimeSpan.FromSeconds(6),
    totalRequestTimeout: TimeSpan.FromSeconds(12),
    concurrencyLimit: 10,
    circuitBreakerMinimumThroughput: 5,
    circuitBreakerBreakDuration: TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("sms", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    var authKey = builder.Configuration["Notifications:Sms:AuthKey"];
    if (!string.IsNullOrWhiteSpace(authKey))
        client.DefaultRequestHeaders.Add("authkey", authKey);
})
.AddExternalDependencyResilience(
    attemptTimeout: TimeSpan.FromSeconds(6),
    totalRequestTimeout: TimeSpan.FromSeconds(12),
    concurrencyLimit: 10,
    circuitBreakerMinimumThroughput: 5,
    circuitBreakerBreakDuration: TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("push", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    var accessToken = builder.Configuration["Notifications:Push:AccessToken"];
    if (!string.IsNullOrWhiteSpace(accessToken))
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
})
.AddExternalDependencyResilience(
    attemptTimeout: TimeSpan.FromSeconds(6),
    totalRequestTimeout: TimeSpan.FromSeconds(12),
    concurrencyLimit: 10,
    circuitBreakerMinimumThroughput: 5,
    circuitBreakerBreakDuration: TimeSpan.FromSeconds(15));

// ── Worker: settings-first notification cache (TTL 60 s, singleton) ────────────
builder.Services.AddSingleton<NotificationSettingsCache>();

// ── Worker: channel senders (fail-safe conditional registration) ───────────────
builder.Services.AddScoped<LoggingChannelSender>();

var whatsAppEnabled = builder.Configuration.GetValue<bool>("Notifications:WhatsApp:Enabled")
    && !string.IsNullOrWhiteSpace(builder.Configuration["Notifications:WhatsApp:AccessToken"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["Notifications:WhatsApp:PhoneNumberId"]);
var smsEnabled = builder.Configuration.GetValue<bool>("Notifications:Sms:Enabled")
    && !string.IsNullOrWhiteSpace(builder.Configuration["Notifications:Sms:AuthKey"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["Notifications:Sms:SenderId"]);
var pushEnabled = builder.Configuration.GetValue<bool>("Notifications:Push:Enabled");

if (whatsAppEnabled)
    builder.Services.AddScoped<WhatsAppCloudChannelSender>();
else
    builder.Services.AddScoped<WhatsAppCloudChannelSender>(_ => null!);

if (smsEnabled)
    builder.Services.AddScoped<Msg91SmsChannelSender>();
else
    builder.Services.AddScoped<Msg91SmsChannelSender>(_ => null!);

if (pushEnabled)
    builder.Services.AddScoped<ExpoPushChannelSender>();
else
    builder.Services.AddScoped<ExpoPushChannelSender>(_ => null!);

builder.Services.AddScoped<IChannelSender, RoutingChannelSender>();

// ── Worker: event publisher (dev stub) ─────────────────────────────────────────
builder.Services.AddScoped<IEventPublisher, LoggingEventPublisher>();

// ── Worker: subscription charger (dev stub in Development; real gateway otherwise) ────
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<ISubscriptionCharger, DevSubscriptionCharger>();
else
    builder.Services.AddSingleton<ISubscriptionCharger, GatewaySubscriptionCharger>();

// ── Worker: background hosted services — gated on a configured DB connection ────
// CRITICAL no-DB gate: each hosted worker opens a DbContext per tick. With no
// ConnectionStrings:Default we register NONE of them so the host boots without a DB.
if (!string.IsNullOrWhiteSpace(connStr))
{
    // Analytics matview refresh (was in the Analytics block of the legacy host).
    builder.Services.AddHostedService<MatviewRefreshService>();

    // Notification + outbox lanes (always on).
    builder.Services.AddHostedService<NotificationDispatcherService>();
    builder.Services.AddHostedService<OutboxEventRelayService>();
    builder.Services.AddHostedService<NotificationMappingService>();
    builder.Services.AddHostedService<CustomerErasureService>();
    builder.Services.AddHostedService<RetentionSweepService>();

    // Opt-in jobs (gated by their own WorkerOptions flags inside ExecuteAsync).
    builder.Services.AddHostedService<AutoDispatchService>();         // opt-in: AutoDispatch:Enabled=true
    builder.Services.AddHostedService<RoyaltyGenerationService>();    // opt-in: Worker:RoyaltyGenerationEnabled=true
    builder.Services.AddHostedService<DailyReconService>();           // opt-in: Worker:DailyReconEnabled=true
    builder.Services.AddHostedService<SubscriptionBillingService>();  // opt-in: Worker:SubscriptionBillingEnabled=true
    builder.Services.AddHostedService<BrandPlatformBillingService>(); // opt-in: Worker:BrandPlatformBillingEnabled=true
    builder.Services.AddHostedService<LoyaltyEarnService>();          // mandatory
    builder.Services.AddHostedService<PartnerBookingDebitService>();  // mandatory: RaaS prepaid booking→wallet debit
    builder.Services.AddHostedService<PartitionMaintenanceService>(); // on by default (Worker:PartitionMaintenanceEnabled=false to opt out)
}

var app = builder.Build();

// No DB configured → the hosted workers above were not registered. Emit a clear,
// structured warning now that a logger is available (logged once at startup).
if (string.IsNullOrWhiteSpace(connStr))
{
    app.Logger.LogWarning(
        "Commerce workers skipped: no ConnectionStrings:Default configured. " +
        "The host is up but no background services (notifications, outbox relay, billing, " +
        "auto-dispatch, retention/erasure, matview refresh, partition maintenance) are running.");
}

// ── Forwarded headers (prod/staging, behind the gateway/edge proxy) ───────────
// Runs first so RemoteIpAddress/scheme reflect the real client. No-op unless
// ForwardedHeaders:Enabled = true.
app.UseForwardedHeadersIfEnabled();

// ── Aspire default health endpoints (/health + /alive) ────────────────────────
app.MapDefaultEndpoints();

// ── Global exception → response-envelope middleware ───────────────────────────
// Maps ValidationException/BusinessRuleException → 422, UnauthorizedAccessException → 401, etc.
// Runs before auth so handler/validator exceptions surface as clean envelopes.
app.UseMiddleware<ExceptionHandler>();

// ── Anonymous Razorpay webhook RLS bypass ──────────────────────────────────────
// The webhook (POST /api/v1/webhooks/razorpay) is unauthenticated, so it carries no brand
// claim. Set Items["bypass_rls"]=true for that exact route BEFORE auth so the RLS interceptor
// sees it when it fixes the brand GUC at connection open — the handler can then resolve the
// payment by gateway_order_id and re-scope the HMAC secret to that payment's brand (SEC-2).
// Exact-route match (not a prefix) so no other anonymous path can inherit the bypass (SEC-3).
app.Use(async (ctx, next) =>
{
    if (HttpMethods.IsPost(ctx.Request.Method)
        && (ctx.Request.Path.Equals("/api/v1/webhooks/razorpay", StringComparison.OrdinalIgnoreCase)
            // Partner paylink webhook: also anonymous + no partner claim → bypass RLS so the handler
            // can resolve the partner invoice by link id / credit any partner's wallet (FULL-10).
            || ctx.Request.Path.Equals("/api/v1/webhooks/razorpay-partner-paylink", StringComparison.OrdinalIgnoreCase)))
    {
        ctx.Items["bypass_rls"] = true;
    }
    await next();
});

// ── Auth pipeline ──────────────────────────────────────────────────────────────
// Shared TenantResolutionMiddleware (laundryghar.Utilities) runs after authentication:
// platform admins get RLS bypass + X-Brand-Id → brand_id_override so RequireBrandId() resolves.
app.UseAuthentication();
app.UseMiddleware<laundryghar.Utilities.Middlewares.TenantResolutionMiddleware>();
app.UseAuthorization();

// ── Output cache — after auth so cached authorized responses still require a valid
// token on every request; only endpoint execution is skipped on a hit.
app.UseOutputCache();

// ── OpenAPI doc (/openapi/v1.json) + Scalar UI (/scalar), dev only ────────────
if (app.Environment.IsDevelopment())
{
    app.MapDefaultOpenApi();
}

// ── Feature endpoints — discovered from IEndpointGroup classes in this assembly ─
// None yet; MapEndpoints finds nothing until Commerce slices are migrated.
app.MapEndpoints(Assembly.GetExecutingAssembly());

// Root liveness.
app.MapGet("/", () => "commerce.WebApi (Commerce + Finance + Analytics) — up");

app.Run();
