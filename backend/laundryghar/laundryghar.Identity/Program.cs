using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using laundryghar.Identity.Application.Common;
using laundryghar.Identity.Endpoints;
using laundryghar.Identity.Infrastructure.Auth;
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

// H4 fail-fast: signing key must be present and non-empty in non-Development.
// In Development the appsettings.Development.json value is used; prod must inject it via env/secrets.
if (!builder.Environment.IsDevelopment()
    && string.IsNullOrWhiteSpace(jwtSettings.SigningKey))
{
    throw new InvalidOperationException(
        "Jwt:SigningKey must be set via environment variable or secrets manager in non-Development environments. " +
        "Never hard-code a production signing key.");
}

// ─── Data ──────────────────────────────────────────────────────────────────

builder.Services.AddSharedDataModel(connStr);

// ─── HTTP context ──────────────────────────────────────────────────────────

builder.Services.AddHttpContextAccessor();

// ─── ICurrentTenant (RLS) + ICurrentUser ──────────────────────────────────

builder.Services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
builder.Services.AddScoped<ICurrentUser,   HttpContextCurrentUser>();

// ─── Auth infrastructure ───────────────────────────────────────────────────

builder.Services.Configure<JwtSettings>(jwtSection);
builder.Services.Configure<OtpSettings>(
    builder.Configuration.GetSection(OtpSettings.SectionName));  // L6

builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

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

// ─── AutoMapper ─────────────────────────────────────────────────────────────

builder.Services.AddAutoMapper(typeof(Program).Assembly);

// ─── JWT Authentication ────────────────────────────────────────────────────

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtSettings.Issuer,
            ValidAudience            = jwtSettings.Audience,
            IssuerSigningKey         = new SymmetricSecurityKey(
                                           Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
            ClockSkew                = TimeSpan.FromSeconds(30),
            // M1: pin to HS256 — reject tokens signed with any other algorithm (e.g. "none").
            // Keeps Identity consistent with the Catalog service's validation parameters.
            ValidAlgorithms          = [SecurityAlgorithms.HmacSha256]
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

    opts.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = 10,
                Window               = TimeSpan.FromSeconds(60),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0   // reject immediately when limit hit
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
        opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
    }
    else
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        opts.AddDefaultPolicy(p =>
            p.WithOrigins(allowedOrigins)
             .AllowAnyHeader()
             .AllowAnyMethod());
    }
});

// ─── Seeders ───────────────────────────────────────────────────────────────

builder.Services.AddScoped<IdentitySeeder>();

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
    var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
    await seeder.SeedAsync();
}

// ─── Middleware pipeline ────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

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
            checks   = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), description = e.Value.Description })
        });
        await ctx.Response.WriteAsync(payload);
    }
});

// ─── API route groups ──────────────────────────────────────────────────────

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

// ─── Aspire default health endpoints (/health + /alive, Development only) ─────────────
app.MapDefaultEndpoints();

app.Run();

// For integration testing
public partial class Program { }
