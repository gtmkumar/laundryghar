using System.Threading.RateLimiting;
using FluentValidation;
using laundryghar.Core;
using laundryghar.Identity.Application.Common;
using laundryghar.Identity.Endpoints;
using laundryghar.Identity.Infrastructure.Auth;
using laundryghar.Identity.Infrastructure.BackgroundServices;
using laundryghar.Identity.Infrastructure.Seeders;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.Identity.Middleware;
using laundryghar.Engagement.Endpoints;
using laundryghar.Engagement.Application.Notifications.Abstractions;
using laundryghar.Engagement.Application.Notifications.Handlers;
using laundryghar.Engagement.Infrastructure.Seeders;
using laundryghar.Engagement.Infrastructure.Services;
using laundryghar.Mcp.Infrastructure.Auth;
using laundryghar.Mcp.Infrastructure.Http;
using laundryghar.Mcp.Tools;
using laundryghar.SharedDataModel;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Middlewares.ExceptionsMiddleware;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

// ════════════════════════════════════════════════════════════════════════════════════
//  laundryghar.Core  (port 5050)
//  Consolidation of three former services into one process:
//    • Identity   (was :5050) — OAuth/OIDC authorization server, RS256 token issuance,
//                  admin brands/users/tenancy/settings, customer + system auth.
//    • Engagement (was :5007) — admin CMS (notifications/onboarding/banners/app-config)
//                  + anonymous /api/v1/public/* brand-resolved endpoints.
//    • Mcp        (was :5009) — MCP Streamable HTTP resource server at /mcp (customer-token
//                  protected) + RFC 9728 protected-resource discovery.
//
//  Identity remains the in-process token issuer (private RS256 key). Because the MCP
//  resource server now lives in the SAME process as the issuer, its JWTs are validated
//  against the in-process signing key — no JWKS HTTP self-call. See JWT setup below.
// ════════════════════════════════════════════════════════════════════════════════════

var builder = WebApplication.CreateBuilder(args);

// ─── Aspire ServiceDefaults (OTel, service discovery, resilience, /health + /alive) ─────
builder.AddServiceDefaults();

// ─── Configuration ────────────────────────────────────────────────────────────────────

var connStr = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

var jwtSection = builder.Configuration.GetSection(
    laundryghar.Identity.Infrastructure.Auth.JwtSettings.SectionName);
var jwtSettings = jwtSection.Get<laundryghar.Identity.Infrastructure.Auth.JwtSettings>()
    ?? throw new InvalidOperationException("Jwt section is required.");

// RS256 signing key provider — eager, so the SAME key instance backs token issuance
// (JwtTokenService) AND in-process validation of every scheme below. Development
// auto-generates+persists a key; outside Development it FAILS CLOSED unless
// Jwt:PrivateKey / Jwt:PrivateKeyPath is supplied.
var keyProvider = new laundryghar.Identity.Infrastructure.Auth.RsaJwtKeyProvider(
    jwtSettings, builder.Environment);
builder.Services.AddSingleton<laundryghar.Identity.Infrastructure.Auth.IJwtKeyProvider>(keyProvider);

// ── Mcp: OAuth 2.1 protected-resource (RFC 9728) + downstream services config ──
var downstreamSection = builder.Configuration.GetSection(DownstreamServicesConfig.SectionName);
var downstream = downstreamSection.Get<DownstreamServicesConfig>() ?? new DownstreamServicesConfig();

var oauthResourceSection = builder.Configuration.GetSection(OAuthResourceSettings.SectionName);
var oauthResource = oauthResourceSection.Get<OAuthResourceSettings>() ?? new OAuthResourceSettings();
builder.Services.Configure<OAuthResourceSettings>(oauthResourceSection);
builder.Services.Configure<DownstreamServicesConfig>(downstreamSection);

// Fail-closed (mirrors Mcp): cleartext downstream/base URLs not permitted outside Development.
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

// ─── Data ───────────────────────────────────────────────────────────────────────────

builder.Services.AddSharedDataModel(connStr, builder.Configuration, builder.Environment);

// ─── HTTP context ─────────────────────────────────────────────────────────────────────

builder.Services.AddHttpContextAccessor();

// ─── ICurrentTenant (RLS) + ICurrentUser ──────────────────────────────────────────────
// Identity's implementations are registered as the canonical ICurrentTenant/ICurrentUser.
// Engagement's handlers consume Engagement.Infrastructure.Services.ICurrentUser (which adds
// RequireBrandId); register those concretely so Engagement's DI resolves its own contract.

builder.Services.AddScoped<ICurrentTenant, laundryghar.Identity.Infrastructure.Services.HttpContextCurrentTenant>();
builder.Services.AddScoped<laundryghar.Identity.Infrastructure.Services.ICurrentUser,
    laundryghar.Identity.Infrastructure.Services.HttpContextCurrentUser>();
builder.Services.AddScoped<laundryghar.Engagement.Infrastructure.Services.ICurrentUser,
    laundryghar.Engagement.Infrastructure.Services.HttpContextCurrentUser>();

// ─── Engagement: brand resolver for anonymous public endpoints + notification sender ──

builder.Services.AddScoped<IBrandResolver, BrandResolver>();
builder.Services.AddScoped<INotificationSender, DevNotificationSender>();

// ─── Auth infrastructure (Identity) ───────────────────────────────────────────────────

builder.Services.Configure<laundryghar.Identity.Infrastructure.Auth.JwtSettings>(jwtSection);
builder.Services.Configure<OtpSettings>(
    builder.Configuration.GetSection(OtpSettings.SectionName));

// Fail closed: the testing master OTP (Otp:TestCode) must never reach Production.
if (builder.Environment.IsProduction()
    && !string.IsNullOrEmpty(builder.Configuration[$"{OtpSettings.SectionName}:TestCode"]))
{
    throw new InvalidOperationException(
        "Otp:TestCode is set in a Production environment. The testing master OTP is " +
        "non-production only — remove Otp__TestCode from this environment's configuration.");
}

builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<laundryghar.Identity.Infrastructure.Email.ISettingsMailer,
    laundryghar.Identity.Infrastructure.Email.SettingsMailer>();

// OTP delivery: channel-routing sender (WhatsApp template + MSG91 SMS fallback).
builder.Services.AddHttpClient();
builder.Services.AddSingleton<DevLogOtpSender>();
builder.Services.AddSingleton<WhatsAppOtpDispatcher>();
builder.Services.AddSingleton<Msg91OtpDispatcher>();
builder.Services.AddScoped<IOtpSender, RoutingOtpSender>();

// ─── MediatR — single registration over the merged assembly (Identity + Engagement) ──

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    // Both Identity and Engagement shipped an identical ValidationPipelineBehavior; register
    // Identity's once. It is a generic FluentValidation pre-handler — namespace-agnostic.
    cfg.AddBehavior(typeof(IPipelineBehavior<,>),
        typeof(laundryghar.Identity.Application.Common.ValidationPipelineBehavior<,>));
});

// ─── FluentValidation — single scan over the merged assembly ──────────────────────────

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// ─── JWT Authentication — two named schemes, both in-process RS256 validation ─────────
//
// All three former services validated the SAME tokens (issuer=laundryghar-identity,
// audience=laundryghar-services, RS256). Identity validated in-process; Engagement+Mcp
// validated via JWKS-over-HTTP against Identity. In one process the in-process signing
// key is authoritative, so BOTH schemes use IssuerSigningKey = keyProvider.SigningKey —
// no HTTP round-trip to ourselves, no startup-order race.
//
// Why two schemes (not one): the MCP resource server must return an RFC 9728 challenge
// (WWW-Authenticate: Bearer resource_metadata="…") on 401 so MCP clients can discover the
// authorization server. Identity's and Engagement's endpoints return a PLAIN 401 challenge
// today and that response shape must not change. So:
//   • "Bearer" (default): plain challenge — used by every Identity + Engagement endpoint.
//   • "mcp": identical validation params + the RFC 9728 OnChallenge — bound only to /mcp.

const string McpScheme = "mcp";

var mcpBaseUrl = oauthResource.McpBaseUrl.TrimEnd('/');
var resourceMetadataUrl = $"{mcpBaseUrl}/.well-known/oauth-protected-resource";

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

// ─── Authorization — single composite policy provider + all handlers ──────────────────
// CoreAuthorizationPolicyProvider resolves permission:* / CustomerOnly (Identity reqs) and
// McpCustomerOnly (Mcp req). Handlers are type-dispatched, so registering both Identity's
// and Mcp's CustomerOnlyHandler keeps their distinct semantics isolated.

builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, AnyPermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler,
    laundryghar.Identity.Infrastructure.Auth.CustomerOnlyHandler>();
builder.Services.AddSingleton<IAuthorizationHandler,
    laundryghar.Mcp.Infrastructure.Auth.CustomerOnlyHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, CoreAuthorizationPolicyProvider>();
builder.Services.AddAuthorization();

// ─── Rate Limiting (Identity auth/oauth policies) ─────────────────────────────────────

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

// ─── Downstream HttpClients (Mcp — token-forwarding) ──────────────────────────────────

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

// ─── MCP Server — Streamable HTTP transport ───────────────────────────────────────────

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

// ─── Health Checks ────────────────────────────────────────────────────────────────────

builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString: connStr,
        name: "postgres",
        tags: ["ready"]);

// ─── OpenAPI ──────────────────────────────────────────────────────────────────────────

builder.Services.AddOpenApi();

// ─── CORS ─────────────────────────────────────────────────────────────────────────────
// Identity's policy is the strictest superset: it reflects the request origin WITH
// AllowCredentials (required for the HttpOnly lg_refresh cookie). This also satisfies the
// Engagement + Mcp endpoints (which previously used AllowAnyOrigin without credentials).

builder.Services.AddCors(opts =>
{
    if (builder.Environment.IsDevelopment())
    {
        opts.AddDefaultPolicy(p => p
            .SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
    }
    else
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        opts.AddDefaultPolicy(p =>
            p.WithOrigins(allowedOrigins)
             .AllowAnyHeader()
             .AllowAnyMethod()
             .AllowCredentials());
    }
});

// ─── Seeders ──────────────────────────────────────────────────────────────────────────

builder.Services.AddScoped<IdentitySeeder>();
builder.Services.AddScoped<EngagementSeeder>();

// ─── Background Services ──────────────────────────────────────────────────────────────
// Hourly sweep of expired oauth_authorization_codes + stale oauth_clients.
builder.Services.AddHostedService<OAuthCleanupService>();

// ───────────────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ─── Run seeders (Identity then Engagement) ───────────────────────────────────────────
// Development-only. Both run on the privileged Admin (postgres) connection — runtime uses
// app_user (RLS-enforced), which rejects cross-brand bootstrap INSERTs without tenant context.

var runSeed = args.Contains("--seed") || app.Environment.IsDevelopment();
if (runSeed)
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "--seed is not permitted outside Development. Use a controlled bootstrap process.");

    using var scope = app.Services.CreateScope();

    using (var seedDb = laundryghar.SharedDataModel.SeedingSupport.CreatePrivilegedContext(
        app.Configuration.GetConnectionString("Admin") ?? connStr))
    {
        var identitySeeder = ActivatorUtilities.CreateInstance<IdentitySeeder>(scope.ServiceProvider, seedDb);
        await identitySeeder.SeedAsync();
    }

    using (var seedDb = laundryghar.SharedDataModel.SeedingSupport.CreatePrivilegedContext(
        app.Configuration.GetConnectionString("Admin") ?? connStr))
    {
        var engagementSeeder = ActivatorUtilities.CreateInstance<EngagementSeeder>(scope.ServiceProvider, seedDb);
        await engagementSeeder.SeedAsync();
    }
}

// ─── Middleware pipeline ──────────────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// ForwardedHeaders: rewrites RemoteIpAddress to real client IP when enabled. MUST run
// before UseCors/UseRateLimiter so rate-limit partitioning uses the real IP.
app.UseForwardedHeadersIfEnabled();
// Security headers (no-op in Development). Must run before UseCors so they appear on preflight.
app.UseSecurityHeaders();
app.UseCors();
app.UseRateLimiter();
app.UseMiddleware<ExceptionHandler>();
app.UseAuthentication();
// TenantResolutionMiddleware AFTER authentication, BEFORE authorization.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

// ─── Health endpoints ─────────────────────────────────────────────────────────────────

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});

// SEC-4: a check's Description can carry Npgsql connection details (DB host / name / user)
// on failure. Only expose it in Development; in non-Dev return name + status only.
var exposeHealthDescriptions = app.Environment.IsDevelopment();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready"),
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        var status = report.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy
            ? "Healthy" : "Unhealthy";
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            status,
            duration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = exposeHealthDescriptions ? e.Value.Description : null
            })
        });
        await ctx.Response.WriteAsync(payload);
    }
});

// ─── RLS bypass for tenant-context-less paths (union of Identity + Engagement rules) ──
// These run as the non-superuser app_user where RLS is active, but legitimately need a
// per-request bypass because no usable token/tenant context exists yet. Each query is
// keyed to the requester's own id / membership / explicit brand predicate.
static bool IsScopeResolvingAuthPath(PathString path) =>
    path.StartsWithSegments("/api/v1/customer/auth")
    || path.StartsWithSegments("/api/v1/auth/password/login")
    || path.StartsWithSegments("/api/v1/auth/otp")
    || path.StartsWithSegments("/api/v1/auth/refresh")
    || path.StartsWithSegments("/oauth");

// SEC-3: narrowed allow-list for the anonymous public CMS endpoints.
//
// Why an exact-path allow-list and NOT a blanket "/api/v1/public" prefix bypass:
//   This process now also hosts Identity (users, credentials, secrets). A blanket prefix
//   bypass means any FUTURE /api/v1/public/* route that forgets an explicit brand predicate
//   would read across ALL tenants. We therefore only disable RLS for the 3 known CMS routes
//   that are individually proven to resolve a brand and pass it as an explicit .Where(brandId)
//   predicate (see PublicEngagementEndpoints + the public CMS queries).
//
// Why bypass at all (rather than GUC-enforce): each of these handlers must first resolve the
// brand by reading tenancy_org.brands by code/header — and that table's RLS policy
// (rls_admin_only = kernel.rls_bypass()) blocks an anonymous, brand-scoped read. The RLS
// interceptor also fixes the brand GUC at connection-open, so we cannot resolve-then-narrow on
// the same pooled connection. The CMS content tables (app_banners, mobile_app_config,
// onboarding_slides) are all guarded at the LINQ level by the resolved brandId, so isolation
// holds even with RLS disabled on these 3 routes.
//
// INVARIANT: any NEW anonymous public route MUST be added here explicitly AND must pass an
// explicit brand predicate in its query. Do NOT broaden this back to a prefix match.
static bool IsAllowlistedPublicCmsPath(PathString path) =>
    path.StartsWithSegments("/api/v1/public/banners")
    || path.StartsWithSegments("/api/v1/public/onboarding-slides")
    || path.StartsWithSegments("/api/v1/public/app-config");

app.Use(async (ctx, next) =>
{
    // Identity: pre-auth scope-resolving flows (only when unauthenticated).
    if (IsScopeResolvingAuthPath(ctx.Request.Path)
        && ctx.User.Identity?.IsAuthenticated != true)
    {
        ctx.Items["bypass_rls"] = true;
    }
    // Engagement: anonymous public CMS endpoints (explicit brand predicate is the guard).
    // Narrowed to the exact known routes — NOT the whole /api/v1/public prefix (SEC-3).
    if (IsAllowlistedPublicCmsPath(ctx.Request.Path))
    {
        ctx.Items["bypass_rls"] = true;
    }
    await next(ctx);
});

// ─── Well-known / OAuth (Identity) ────────────────────────────────────────────────────
// OIDC discovery + JWKS (anonymous). Byte-compatible issuer + discovery + JWKS preserved.
app.MapWellKnownEndpoints();
// OAuth 2.1 authorization-server facade (RFC 8414, 7591, 7636).
app.MapOAuthEndpoints();

// ─── MCP protected-resource metadata (RFC 9728, anonymous) ────────────────────────────
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

// ─── API route groups ─────────────────────────────────────────────────────────────────

var v1 = app.MapGroup("/api/v1");

// ── Identity: system + customer auth ──
v1.MapAuthEndpoints();
v1.MapCustomerAuthEndpoints();

// ── Identity admin CRUD (token_use=user + permission policies) ──
var identityAdmin = v1.MapGroup("/admin").RequireAuthorization();
identityAdmin.MapBrandEndpoints();
identityAdmin.MapTenancyEndpoints();
identityAdmin.MapUserEndpoints();
identityAdmin.MapSettingsEndpoints();

// ── Engagement admin CMS (token_use=user + permission policies) ──
var engagementAdmin = v1.MapGroup("/admin").RequireAuthorization();
engagementAdmin.MapAdminNotificationTemplateEndpoints();
engagementAdmin.MapAdminOnboardingSlideEndpoints();
engagementAdmin.MapAdminAppBannerEndpoints();
engagementAdmin.MapAdminMobileAppConfigEndpoints();
engagementAdmin.MapAdminNotificationLogEndpoints();

// ── Engagement public anonymous (brand via X-Brand-Id / brandCode) ──
v1.MapPublicEngagementEndpoints();

// ─── MCP endpoint — customer-token protected via the "mcp" scheme + McpCustomerOnly ──
// The "mcp" scheme returns the RFC 9728 challenge on 401; McpCustomerOnly enforces the
// customer_mcp+scope OR customer token rule before the MCP protocol layer sees any message.
app.MapMcp("/mcp")
   .RequireAuthorization(new AuthorizeAttribute
   {
       AuthenticationSchemes = McpScheme,
       Policy = CoreAuthorizationPolicyProvider.McpCustomerOnlyPolicy
   });

// ─── Aspire default health endpoints (/health + /alive, Development only) ──────────────
app.MapDefaultEndpoints();

app.Run();

// For integration testing
public partial class Program { }
