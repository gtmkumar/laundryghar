using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using laundryghar.Identity.Application.Common;
using laundryghar.Identity.Endpoints;
using laundryghar.Identity.Infrastructure.Auth;
using laundryghar.Identity.Infrastructure.BackgroundServices;
using laundryghar.Identity.Infrastructure.Seeders;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.Identity.Middleware;
using laundryghar.SharedDataModel;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Middlewares.ExceptionsMiddleware;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ─── Aspire ServiceDefaults (OTel, service discovery, resilience, /health + /alive) ─────
builder.AddServiceDefaults();

// ─── Configuration ────────────────────────────────────────────────────────

var connStr = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

var jwtSection = builder.Configuration.GetSection(JwtSettings.SectionName);
var jwtSettings = jwtSection.Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt section is required.");

// RS256 signing key provider. Constructed eagerly so the same key instance backs
// both token issuance (JwtTokenService) and Identity's own local validation below.
// In Development it auto-generates+persists a key; outside Development it FAILS CLOSED
// unless Jwt:PrivateKey / Jwt:PrivateKeyPath is supplied.
var keyProvider = new laundryghar.Identity.Infrastructure.Auth.RsaJwtKeyProvider(
    jwtSettings, builder.Environment);
builder.Services.AddSingleton<laundryghar.Identity.Infrastructure.Auth.IJwtKeyProvider>(keyProvider);

// ─── Data ──────────────────────────────────────────────────────────────────

builder.Services.AddSharedDataModel(connStr, builder.Configuration, builder.Environment);

// ─── HTTP context ──────────────────────────────────────────────────────────

builder.Services.AddHttpContextAccessor();

// ─── ICurrentTenant (RLS) + ICurrentUser ──────────────────────────────────

builder.Services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

// ─── Auth infrastructure ───────────────────────────────────────────────────

builder.Services.Configure<JwtSettings>(jwtSection);
builder.Services.Configure<OtpSettings>(
    builder.Configuration.GetSection(OtpSettings.SectionName));  // L6

builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<laundryghar.Identity.Infrastructure.Email.ISettingsMailer, laundryghar.Identity.Infrastructure.Email.SettingsMailer>();

// C4: Register OTP sender conditionally — DevLogOtpSender only in Development.
// In all other environments the Msg91 stub throws NotImplemented at call-time, preventing
// silent OTP logging in staging/prod aggregated log pipelines.
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<IOtpSender, DevLogOtpSender>();
else
    builder.Services.AddSingleton<IOtpSender, Msg91OtpSender>();

// ─── MediatR ───────────────────────────────────────────────────────────────

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));
});

// ─── FluentValidation ──────────────────────────────────────────────────────

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// ─── JWT Authentication ────────────────────────────────────────────────────

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            // RS256: Identity validates its own tokens with the in-process public key.
            IssuerSigningKey = keyProvider.SigningKey,
            ClockSkew = TimeSpan.FromSeconds(30),
            // Pin to RS256 — reject "none" and HMAC algorithm-confusion attacks.
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
        };
    });

// ─── Authorization (RBAC policy provider + CustomerOnly) ──────────────────

builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, CustomerOnlyHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddAuthorization();

// ─── Rate Limiting (C5) ────────────────────────────────────────────────────
// "auth" policy: 10 requests per 60 s per client IP — applied to all /api/v1/auth/* endpoints.
// Queued responses with 429 on overflow; FixedWindowLimiter is cheap and sufficient for auth.

builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // "auth" policy: 10 requests per 60 s per client IP.
    // Applied to all /api/v1/auth/* endpoints and OAuth backing endpoints.
    opts.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(60),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0   // reject immediately when limit hit
            }));

    // "oauth_register" policy: 3 registrations per hour per IP.
    // Much tighter than "auth" — registration is a one-time client setup action,
    // not a per-user flow. Prevents client_id enumeration / DB spam.
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

// ─── Health Checks ─────────────────────────────────────────────────────────

builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString: connStr,
        name: "postgres",
        tags: ["ready"]);

// ─── OpenAPI ───────────────────────────────────────────────────────────────

builder.Services.AddOpenApi();

// ─── CORS ──────────────────────────────────────────────────────────────────
// C4: allow-all only in Development; non-dev reads Cors:AllowedOrigins from config.

builder.Services.AddCors(opts =>
{
    if (builder.Environment.IsDevelopment())
    {
        // Cookie auth (lg_refresh) requires AllowCredentials, which is incompatible with
        // AllowAnyOrigin() ("*"). Reflect the request origin instead so the Vite dev
        // origins (admin-web :5173, pos-web) can send/receive the HttpOnly refresh cookie.
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
             // Required so browsers send/accept the HttpOnly lg_refresh cookie. Safe here
             // because origins are an explicit allowlist (never "*" with credentials).
             .AllowCredentials());
    }
});

// ─── Seeders ───────────────────────────────────────────────────────────────

builder.Services.AddScoped<IdentitySeeder>();

// ─── Background Services ────────────────────────────────────────────────────
// Hourly sweep: delete expired oauth_authorization_codes (grace 1 day) and
// stale oauth_clients that never completed a token exchange (> 7 days old).
builder.Services.AddHostedService<OAuthCleanupService>();

// ─────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ─── Run seeders ───────────────────────────────────────────────────────────
// C4: Auto-seed only in Development. The seeder itself also enforces this, but
// we add an outer guard here so --seed in prod fails fast before DI scope opens.

var runSeed = args.Contains("--seed") || app.Environment.IsDevelopment();
if (runSeed)
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "--seed is not permitted outside Development. Use a controlled bootstrap process.");

    using var scope = app.Services.CreateScope();
    // Seed on the privileged Admin (postgres) connection — runtime uses app_user (RLS-enforced),
    // which would reject cross-brand bootstrap INSERTs that carry no request tenant context.
    using var seedDb = laundryghar.SharedDataModel.SeedingSupport.CreatePrivilegedContext(
        app.Configuration.GetConnectionString("Admin") ?? connStr);
    var seeder = ActivatorUtilities.CreateInstance<IdentitySeeder>(scope.ServiceProvider, seedDb);
    await seeder.SeedAsync();
}

// ─── Middleware pipeline ────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// ForwardedHeaders: rewrites RemoteIpAddress from socket IP to real client IP
// when ForwardedHeaders:Enabled=true (off by default in Development).
// MUST run before UseCors/UseRateLimiter so rate-limit partitioning uses the real IP.
app.UseForwardedHeadersIfEnabled();
// No-op in Development. Must run before UseCors so headers appear on preflight responses.
app.UseSecurityHeaders();
app.UseCors();
app.UseRateLimiter();                                      // C5
app.UseMiddleware<ExceptionHandler>();                     // Utilities exception handler → JSON errors
app.UseAuthentication();
// L4: TenantResolutionMiddleware AFTER authentication, BEFORE authorization.
// Authentication populates User principal; tenant middleware reads claims; authorization consumes them.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

// ─── Health endpoints ──────────────────────────────────────────────────────

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false   // liveness: no checks — just "I'm up"
});

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
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), description = e.Value.Description })
        });
        await ctx.Response.WriteAsync(payload);
    }
});

// ─── API route groups ──────────────────────────────────────────────────────

// Pre-auth / token-issuance flows resolve a user's tenant scope BEFORE a usable
// token exists. The service runs as the non-superuser app_user where RLS is active,
// so these tenant-context-less reads would return zero rows:
//   • Customer OTP flows resolve the brand by code and look up the customer.
//   • System (staff/rider/admin) login + refresh run ScopeResolver, which reads the
//     user's own membership and then the referenced store/franchise/warehouse to
//     derive brand_id. stores/franchises/warehouses are RLS-protected, so without a
//     bypass a Store/Franchise/Warehouse-scoped user (rider, store staff) would get
//     NO brand_id claim and every brand-scoped call afterwards would fail.
// These legitimately bypass RLS for the request; the isolation guard is that each
// query is keyed to the requesting user's own id / their own membership's scope.
static bool IsScopeResolvingAuthPath(PathString path) =>
    path.StartsWithSegments("/api/v1/customer/auth")
    || path.StartsWithSegments("/api/v1/auth/password/login")
    || path.StartsWithSegments("/api/v1/auth/otp")
    || path.StartsWithSegments("/api/v1/auth/refresh")
    // OAuth 2.1 endpoints resolve brand from config (no token) — need RLS bypass
    || path.StartsWithSegments("/oauth");

app.Use(async (ctx, next) =>
{
    if (IsScopeResolvingAuthPath(ctx.Request.Path)
        && ctx.User.Identity?.IsAuthenticated != true)
    {
        ctx.Items["bypass_rls"] = true;
    }
    await next(ctx);
});

// OIDC discovery + JWKS (anonymous, app root) — services fetch the RS256 public key here.
app.MapWellKnownEndpoints();

// OAuth 2.1 authorization-server facade (RFC 8414, RFC 7591, RFC 7636).
// Includes /.well-known/oauth-authorization-server, /oauth/register, /oauth/authorize,
// /oauth/authorize/otp/send, /oauth/authorize/approve, /oauth/token.
app.MapOAuthEndpoints();

var v1 = app.MapGroup("/api/v1");

// System user auth (staff / admin / rider)
v1.MapAuthEndpoints();

// Customer mobile auth (OTP-only, token_use=customer)
v1.MapCustomerAuthEndpoints();

// Admin CRUD (requires token_use=user + permission policies)
var admin = v1.MapGroup("/admin").RequireAuthorization();
admin.MapBrandEndpoints();
admin.MapTenancyEndpoints();
admin.MapUserEndpoints();
admin.MapSettingsEndpoints();

// ─── Aspire default health endpoints (/health + /alive, Development only) ─────────────
app.MapDefaultEndpoints();

app.Run();

// For integration testing
public partial class Program { }
