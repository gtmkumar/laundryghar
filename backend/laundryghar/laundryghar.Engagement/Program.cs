using System.Text;
using FluentValidation;
using laundryghar.Engagement.Application.Common;
using laundryghar.Engagement.Application.Notifications.Abstractions;
using laundryghar.Engagement.Application.Notifications.Handlers;
using laundryghar.Engagement.Endpoints;
using laundryghar.Engagement.Infrastructure.Auth;
using laundryghar.Engagement.Infrastructure.Seeders;
using laundryghar.Engagement.Infrastructure.Services;
using laundryghar.Engagement.Middleware;
using laundryghar.SharedDataModel;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Middlewares.ExceptionsMiddleware;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ─── Aspire ServiceDefaults (OTel, service discovery, resilience, /health + /alive) ─────
builder.AddServiceDefaults();

// ─── Configuration ─────────────────────────────────────────────────────────────

var connStr = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

var jwtSection = builder.Configuration.GetSection(JwtSettings.SectionName);
var jwtSettings = jwtSection.Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt section is required.");

// Fail-fast: signing key must be present in non-Development.
if (!builder.Environment.IsDevelopment()
    && string.IsNullOrWhiteSpace(jwtSettings.SigningKey))
{
    throw new InvalidOperationException(
        "Jwt:SigningKey must be set via environment variable or secrets manager in non-Development environments.");
}

// ─── Data ──────────────────────────────────────────────────────────────────────

builder.Services.AddSharedDataModel(connStr);

// ─── HTTP context ──────────────────────────────────────────────────────────────

builder.Services.AddHttpContextAccessor();

// ─── ICurrentTenant (RLS) + ICurrentUser ──────────────────────────────────────

builder.Services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
builder.Services.AddScoped<ICurrentUser,   HttpContextCurrentUser>();

// ─── Brand resolver for anonymous public endpoints ─────────────────────────────

builder.Services.AddScoped<IBrandResolver, BrandResolver>();

// ─── Notification sender (dev stub) ───────────────────────────────────────────

builder.Services.AddScoped<INotificationSender, DevNotificationSender>();

// ─── JWT auth config ───────────────────────────────────────────────────────────

builder.Services.Configure<JwtSettings>(jwtSection);

// ─── MediatR ───────────────────────────────────────────────────────────────────

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));
});

// ─── FluentValidation ──────────────────────────────────────────────────────────

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// ─── JWT Authentication ────────────────────────────────────────────────────────

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
            ValidAlgorithms          = new[] { SecurityAlgorithms.HmacSha256 }
        };
    });

// ─── Authorization ──────────────────────────────────────────────────────────────

builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, CustomerOnlyHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddAuthorization();

// ─── Health Checks ─────────────────────────────────────────────────────────────

builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString: connStr,
        name: "postgres",
        tags: ["ready"]);

// ─── OpenAPI ───────────────────────────────────────────────────────────────────

builder.Services.AddOpenApi();

// ─── CORS ──────────────────────────────────────────────────────────────────────

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

// ─── Seeders ───────────────────────────────────────────────────────────────────

builder.Services.AddScoped<EngagementSeeder>();

// ──────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ─── Run seeders ───────────────────────────────────────────────────────────────

var runSeed = args.Contains("--seed") || app.Environment.IsDevelopment();
if (runSeed)
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "--seed is not permitted outside Development. Use a controlled bootstrap process.");

    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<EngagementSeeder>();
    await seeder.SeedAsync();
}

// ─── Middleware pipeline ────────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors();
app.UseMiddleware<ExceptionHandler>();
app.UseAuthentication();
// TenantResolutionMiddleware AFTER authentication, BEFORE authorization.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

// ─── Health endpoints ──────────────────────────────────────────────────────────

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
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

// ─── API route groups ──────────────────────────────────────────────────────────

var v1 = app.MapGroup("/api/v1");

// Admin routes — require auth + permission policies
var admin = v1.MapGroup("/admin").RequireAuthorization();
admin.MapAdminNotificationTemplateEndpoints();
admin.MapAdminOnboardingSlideEndpoints();
admin.MapAdminAppBannerEndpoints();
admin.MapAdminMobileAppConfigEndpoints();
admin.MapAdminNotificationLogEndpoints();

// Public anonymous routes — brand resolved from X-Brand-Id / brandCode param
v1.MapPublicEngagementEndpoints();

// ─── Aspire default health endpoints (/health + /alive, Development only) ─────────────
app.MapDefaultEndpoints();

app.Run();

// For integration testing
public partial class Program { }
