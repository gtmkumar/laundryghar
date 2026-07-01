// ─────────────────────────────────────────────────────────────────────────────
// core.WebApi — consolidated host (Identity + Engagement + Mcp)
//
// Listening port: http://localhost:5050 (dev; fixed — gateway + clients hard-reference it)
//
// Composition root: wires the core bounded-context layers
//   • AddCoreApplication()    → use cases / handlers / validators (core.Application)
//   • AddCoreInfrastructure() → persistence / gateways / external services (core.Infrastructure)
// plus Aspire ServiceDefaults (OpenTelemetry, health checks, service discovery, resilience).
// ─────────────────────────────────────────────────────────────────────────────

using System.Reflection;
using System.Threading.RateLimiting;
using core.Application;
using core.Application.Common;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Infrastructure;
using core.Infrastructure.Auth;
using core.Infrastructure.BackgroundServices;
using core.WebApi.Mcp.Infrastructure.Auth;
using core.WebApi.Mcp.Infrastructure.Http;
using core.WebApi.Mcp.Tools;
using laundryghar.SharedDataModel;
using laundryghar.Utilities.Auth;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Middlewares.ExceptionsMiddleware;
using laundryghar.Utilities.OpenApi;
using laundryghar.Utilities.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults (OTel, service discovery, /health + /alive) ─────────
builder.AddServiceDefaults();

// ── Current user (ICurrentUser from request principal) ─────────────────────────
builder.Services.AddCurrentUser();

// ── Current tenant (ICurrentTenant from JWT claims — backs the shared RLS interceptor) ─
// Cross-cutting registration (laundryghar.Utilities.Services.HttpContextCurrentTenant),
// shared with the Operations host. Without it the shared RlsConnectionInterceptor can't
// resolve ICurrentTenant and DI scope validation fails at builder.Build().
builder.Services.AddCurrentTenant();

// ── Shared data model: LaundryGharDbContext (+ generic repo wiring) ────────────
// NOTE: an ICurrentTenant implementation must also be registered for RLS at runtime.
// connStr is captured into a local so the dev IdentitySeeder block (below, after Build())
// can gate on whether a database is actually configured — the host must still boot with
// no ConnectionStrings:Default set.
var connStr = builder.Configuration.GetConnectionString("Default") ?? string.Empty;
builder.Services.AddSharedDataModel(
    connStr,
    builder.Configuration,
    builder.Environment);

// ── Core bounded-context composition ──────────────────────────────────────────
builder.Services
    .AddCoreApplication()      // validators + command/query handlers (no mediator)
    .AddCoreInfrastructure();  // feature repositories

// ── Auth foundation (F1-F4) ────────────────────────────────────────────────────
// JWT settings + RS256 signing key. The key provider is eager-constructed so the SAME
// instance backs token issuance (JwtTokenService) AND in-process JWT validation below.
// Development auto-generates+persists a key; outside Development it FAILS CLOSED unless
// Jwt:PrivateKey / Jwt:PrivateKeyPath is supplied.
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt section is required.");

var keyProvider = new RsaJwtKeyProvider(jwtSettings, builder.Environment);
builder.Services.AddSingleton<IJwtKeyProvider>(keyProvider);

// ── Mcp: OAuth 2.1 protected-resource (RFC 9728) + downstream services config ──
// The MCP resource server at /mcp validates the SAME RS256 tokens as the Bearer scheme,
// but must emit an RFC 9728 challenge on 401 (see the "mcp" scheme below) and proxies the
// caller's bearer token to external Catalog/Orders services (those services live elsewhere).
var oauthResourceSection = builder.Configuration.GetSection(OAuthResourceSettings.SectionName);
var oauthResource = oauthResourceSection.Get<OAuthResourceSettings>() ?? new OAuthResourceSettings();
builder.Services.Configure<OAuthResourceSettings>(oauthResourceSection);

var downstreamSection = builder.Configuration.GetSection(DownstreamServicesConfig.SectionName);
var downstream = downstreamSection.Get<DownstreamServicesConfig>() ?? new DownstreamServicesConfig();
builder.Services.Configure<DownstreamServicesConfig>(downstreamSection);

// Fail-closed: cleartext base URLs are not permitted outside Development.
if (!builder.Environment.IsDevelopment())
{
    static void AssertHttps(string url, string configKey)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Configuration error: {configKey} is set to a cleartext http:// URL ('{url}'). " +
                "All base URLs must use https:// outside Development.");
    }

    AssertHttps(downstream.CatalogBaseUrl, "DownstreamServices:CatalogBaseUrl");
    AssertHttps(downstream.OrdersBaseUrl, "DownstreamServices:OrdersBaseUrl");
    AssertHttps(oauthResource.McpBaseUrl, "OAuthResource:McpBaseUrl");
    AssertHttps(oauthResource.IdentityBaseUrl, "OAuthResource:IdentityBaseUrl");
}

// RFC 9728 resource_metadata URL placed in the WWW-Authenticate challenge on /mcp 401s.
const string McpScheme = "mcp";
var mcpBaseUrl = oauthResource.McpBaseUrl.TrimEnd('/');
var resourceMetadataUrl = $"{mcpBaseUrl}/.well-known/oauth-protected-resource";

builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();

// ── OTP delivery stack (F5) ─────────────────────────────────────────────────────
builder.Services.Configure<OtpSettings>(builder.Configuration.GetSection(OtpSettings.SectionName));

// Fail closed: the testing master OTP (Otp:TestCode) must never reach Production.
if (builder.Environment.IsProduction()
    && !string.IsNullOrEmpty(builder.Configuration[$"{OtpSettings.SectionName}:TestCode"]))
{
    throw new InvalidOperationException(
        "Otp:TestCode is set in a Production environment. The testing master OTP is " +
        "non-production only — remove Otp__TestCode from this environment's configuration.");
}

// OTP delivery: channel-routing sender (WhatsApp template + MSG91 SMS fallback).
builder.Services.AddHttpClient();
builder.Services.AddSingleton<DevLogOtpSender>();
builder.Services.AddSingleton<WhatsAppOtpDispatcher>();
builder.Services.AddSingleton<Msg91OtpDispatcher>();
builder.Services.AddScoped<IOtpSender, RoutingOtpSender>();

// ── OAuth (E8) hourly cleanup sweep ──────────────────────────────────────────────
// Removes expired authorization codes (>1 day past expiry) and abandoned client
// registrations (last_used_at IS NULL, >7 days old). Singleton BackgroundService that
// opens a scoped LaundryGharDbContext per sweep.
builder.Services.AddHostedService<OAuthCleanupService>();

// ── Seeders (F6) ─────────────────────────────────────────────────────────────────
// Dev-only idempotent identity bootstrap (permissions, system roles, role_permissions,
// platform + brand, platform_admin user). Runs after Build() — see the gated seed block.
builder.Services.AddScoped<core.Infrastructure.Seeders.IdentitySeeder>();

// ── Rate limiting (C5) ──────────────────────────────────────────────────────────
var authPermitLimit = builder.Configuration.GetValue<int?>("RateLimit:AuthPermitLimit") ?? 10;

builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // "auth": 10 req / 60 s per client IP — all /api/v1/auth/* + OAuth backing endpoints.
    opts.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromSeconds(60),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // "oauth_register": 3 registrations / hour per IP.
    opts.AddPolicy("oauth_register", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromHours(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// ── JWT Authentication — single in-process RS256 Bearer scheme ─────────────────
// The in-process signing key is authoritative — no HTTP round-trip to ourselves and
// no startup-order race. Pin to RS256 to reject "none"/HMAC algorithm-confusion attacks.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            // RS256: validate with the in-process public key.
            IssuerSigningKey = keyProvider.SigningKey,
            ClockSkew = TimeSpan.FromSeconds(30),
            // Pin to RS256 — reject "none" and HMAC algorithm-confusion attacks.
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
        };
    })
    // ── "mcp" scheme — identical RS256 validation + RFC 9728 challenge ─────────
    // Why a second scheme (not one): the MCP resource server must return an RFC 9728
    // challenge (WWW-Authenticate: Bearer resource_metadata="…") on 401 so MCP clients can
    // discover the authorization server. The default "Bearer" scheme returns a plain 401
    // that every Identity + Engagement endpoint relies on — that shape must not change. The
    // "mcp" scheme is bound only to /mcp.
    .AddJwtBearer(McpScheme, opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = keyProvider.SigningKey,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
        };

        // RFC 9728 §5 / MCP spec discovery handshake: 401s MUST carry
        // WWW-Authenticate: Bearer resource_metadata="<url>".
        opts.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                context.Response.Headers["WWW-Authenticate"] =
                    $"Bearer resource_metadata=\"{resourceMetadataUrl}\"";

                var body = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
                {
                    error = "unauthorized",
                    error_description = "Bearer token is required. Discover the authorization server via the WWW-Authenticate header."
                });
                return context.Response.Body.WriteAsync(body).AsTask();
            }
        };
    });

// Permission/CustomerOnly authorization handlers + dynamic policy provider.
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, AnyPermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, CustomerOnlyHandler>();
// MCP requirement handler — type-dispatched, so its semantics stay isolated from the
// Identity CustomerOnlyHandler above (distinct requirement types).
builder.Services.AddSingleton<IAuthorizationHandler, McpCustomerOnlyHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
// §8 step-up: convert a step-up policy denial into a structured 403 step_up_required.
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, StepUpAuthorizationResultHandler>();

// Single AddAuthorization. "McpCustomerOnly" is registered as an EXPLICIT named policy so
// the default provider resolves it — PermissionPolicyProvider.GetPolicyAsync falls back to
// DefaultAuthorizationPolicyProvider for names it doesn't recognize, so no second policy
// provider is needed. The policy is bound to the "mcp" scheme (RFC 9728 challenge on 401).
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("McpCustomerOnly", p =>
    {
        p.AddAuthenticationSchemes(McpScheme);
        p.RequireAuthenticatedUser();
        p.AddRequirements(new McpCustomerOnlyRequirement());
    });
});

// ── Mcp: downstream token-forwarding HttpClients ────────────────────────────────
// The MCP tools call EXTERNAL Catalog/Orders services (not hosted in this process). Each
// keyed client wraps a TokenForwardingHandler that forwards the inbound customer bearer
// token; downstream services enforce their own CustomerOnly authorization.
builder.Services.AddTransient<TokenForwardingHandler>();

builder.Services.AddKeyedSingleton<HttpClient>(
    DownstreamClientNames.Catalog,
    (sp, _) =>
    {
        var opts = sp.GetRequiredService<IOptions<DownstreamServicesConfig>>().Value;
        var handler = new TokenForwardingHandler(sp.GetRequiredService<IHttpContextAccessor>())
        {
            InnerHandler = new HttpClientHandler()
        };
        return new HttpClient(handler) { BaseAddress = new Uri(opts.CatalogBaseUrl) };
    });

builder.Services.AddKeyedSingleton<HttpClient>(
    DownstreamClientNames.Orders,
    (sp, _) =>
    {
        var opts = sp.GetRequiredService<IOptions<DownstreamServicesConfig>>().Value;
        var handler = new TokenForwardingHandler(sp.GetRequiredService<IHttpContextAccessor>())
        {
            InnerHandler = new HttpClientHandler()
        };
        return new HttpClient(handler) { BaseAddress = new Uri(opts.OrdersBaseUrl) };
    });

// ── Mcp: Streamable HTTP server (8 customer-facing LaundryGhar tools) ────────────
builder.Services
    .AddMcpServer(opts =>
    {
        opts.ServerInfo = new ModelContextProtocol.Protocol.Implementation
        {
            Name = "laundryghar-mcp",
            Version = "1.0.0"
        };
    })
    .WithHttpTransport()
    .WithTools<LaundryTools>();

// ── OpenAPI document (+ Bearer scheme & standard error responses) ──────────────
builder.Services.AddDefaultOpenApi();

var app = builder.Build();

// ── Run seeders (F6 — IdentitySeeder, dev-only idempotent bootstrap) ──────────────
// Seeding runs on the privileged Admin (postgres/superuser) connection — the runtime
// app_user is RLS-enforced and rejects cross-brand bootstrap INSERTs without a tenant
// context (see SeedingSupport.CreatePrivilegedContext).
//
// C4 production guard: --seed outside Development throws (no accidental prod seeding).
//
// BOOTABILITY GATE: this host must keep booting even when NO database is configured
// (ConnectionStrings:Default unset). The seeder actively connects to Postgres, so we only
// run it when a connection string is actually present. With a real connection string set,
// dev startup seeds automatically; with none, we log a warning and skip — never crash.
var runSeed = args.Contains("--seed") || app.Environment.IsDevelopment();
if (runSeed)
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "--seed is not permitted outside Development. Use a controlled bootstrap process.");

    if (string.IsNullOrWhiteSpace(connStr))
    {
        app.Logger.LogWarning(
            "IdentitySeeder skipped: no ConnectionStrings:Default configured. " +
            "Configure a database connection string to enable Development auto-seeding.");
    }
    else
    {
        using var scope = app.Services.CreateScope();

        // Privileged RLS-bypassing context: prefer ConnectionStrings:Admin (superuser),
        // falling back to the app connection string (Default) in Development.
        using var seedDb = laundryghar.SharedDataModel.SeedingSupport.CreatePrivilegedContext(
            app.Configuration.GetConnectionString("Admin") ?? connStr);

        var seeder = ActivatorUtilities.CreateInstance<core.Infrastructure.Seeders.IdentitySeeder>(
            scope.ServiceProvider, seedDb);
        await seeder.SeedAsync();

        // Read-only invariants check over the permission registry (logs drift/overlaps).
        var regLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("PermissionRegistry");
        await core.Infrastructure.Seeders.PermissionRegistryValidator.ValidateAndLogAsync(seedDb, regLogger);
    }
}

// ── Forwarded headers (prod/staging, behind the gateway/edge proxy) ───────────
// Runs first so RemoteIpAddress/scheme reflect the real client. No-op unless
// ForwardedHeaders:Enabled = true. MUST run before UseRateLimiter so rate-limit
// partitioning uses the real client IP.
app.UseForwardedHeadersIfEnabled();

// ── Rate limiting (C5) — after real-IP resolution, before endpoints ───────────
app.UseRateLimiter();

// ── Global exception → response-envelope middleware ───────────────────────────
// Maps ValidationException/BusinessRuleException → 422, UnauthorizedAccessException → 401,
// etc. Runs before auth so handler/validator exceptions surface as clean envelopes.
app.UseMiddleware<ExceptionHandler>();

// ── Aspire default health endpoints (/health + /alive) ────────────────────────
app.MapDefaultEndpoints();

// ── OpenAPI doc (/openapi/v1.json) + Scalar UI (/scalar), dev only ────────────
if (app.Environment.IsDevelopment())
{
    app.MapDefaultOpenApi();
    app.MapGet("/", () => Results.Redirect("/scalar"));
}

// ── Auth pipeline ──────────────────────────────────────────────────────────────
// Order matters: authenticate, then resolve tenant (reads JWT claims, sets RLS bypass
// for platform admins), then authorize.
app.UseAuthentication();

// ── RLS bypass for tenant-context-less pre-auth scope-resolving paths ─────────
// These run as the non-superuser app_user where RLS is active, but legitimately need a
// per-request bypass because no usable token/tenant context exists yet (login, OTP, refresh).
// Each underlying query is keyed to the requester's own id / membership, so isolation holds.
// Runs AFTER authentication (so we only bypass when still unauthenticated) and BEFORE the
// tenant middleware so the bypass flag is set before RLS scoping is decided.
app.Use(async (ctx, next) =>
{
    if (IsScopeResolvingAuthPath(ctx.Request.Path)
        && ctx.User.Identity?.IsAuthenticated != true)
    {
        ctx.Items["bypass_rls"] = true;
    }
    await next(ctx);
});

app.UseMiddleware<laundryghar.Utilities.Middlewares.TenantResolutionMiddleware>();
app.UseAuthorization();

// ── MCP protected-resource metadata (RFC 9728, anonymous) ──────────────────────
// MCP clients fetch this to discover the authorization server before presenting a token.
app.MapGet("/.well-known/oauth-protected-resource",
    (IOptions<OAuthResourceSettings> settings) =>
    {
        var s = settings.Value;
        var mcpBase = s.McpBaseUrl.TrimEnd('/');
        var identityBase = s.IdentityBaseUrl.TrimEnd('/');
        return Results.Json(new
        {
            resource = $"{mcpBase}/mcp",
            authorization_servers = new[] { identityBase },
            bearer_methods_supported = new[] { "header" }
        });
    })
    .AllowAnonymous()
    .WithTags("Well-Known");

// ── Feature endpoints — discovered from IEndpointGroup classes in this assembly ─
app.MapEndpoints(Assembly.GetExecutingAssembly());

// ── MCP endpoint — customer-token protected via the "mcp" scheme + McpCustomerOnly ──
// The "mcp" scheme returns the RFC 9728 challenge on 401; McpCustomerOnly enforces the
// customer_mcp+scope OR customer token rule before the MCP protocol layer sees any message.
app.MapMcp("/mcp")
   .RequireAuthorization(new AuthorizeAttribute
   {
       AuthenticationSchemes = McpScheme,
       Policy = "McpCustomerOnly"
   });

app.Run();

// ── Pre-auth scope-resolving paths that need an RLS bypass ────────────────────
// Identity (E6): password login, OTP send/verify, and refresh resolve a user's
// scope/memberships before any tenant context exists. Customer-auth (E7): the
// unauthenticated OTP send/verify and refresh paths resolve the brand via an explicit
// predicate (no token/tenant yet); logout + /me are authenticated (CustomerOnly) so they
// keep RLS and are NOT bypassed here. OAuth (E8): the unauthenticated /oauth/* flows
// (register, authorize page + backing OTP send/approve, token exchange) resolve the brand
// and client/customer rows via explicit predicates before any tenant context exists.
static bool IsScopeResolvingAuthPath(PathString path) =>
    path.StartsWithSegments("/api/v1/auth/password/login")
    || path.StartsWithSegments("/api/v1/auth/otp")
    || path.StartsWithSegments("/api/v1/auth/refresh")
    || path.StartsWithSegments("/api/v1/customer/auth/otp")
    || path.StartsWithSegments("/api/v1/customer/auth/refresh")
    || path.StartsWithSegments("/oauth");
