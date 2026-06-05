using System.Text;
using FluentValidation;
using laundryghar.Finance.Application.Common;
using laundryghar.Finance.Endpoints;
using laundryghar.Finance.Infrastructure.Auth;
using laundryghar.Finance.Infrastructure.Seeders;
using laundryghar.Finance.Infrastructure.Services;
using laundryghar.Finance.Middleware;
using laundryghar.SharedDataModel;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Middlewares.ExceptionsMiddleware;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ─── Aspire ServiceDefaults (OTel, service discovery, resilience, /health + /alive) ─────
builder.AddServiceDefaults();

// ─── Configuration ─────────────────────────────────────────────────────────

var connStr = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

var jwtSection  = builder.Configuration.GetSection(JwtSettings.SectionName);
var jwtSettings = jwtSection.Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt section is required.");

if (!builder.Environment.IsDevelopment()
    && string.IsNullOrWhiteSpace(jwtSettings.SigningKey))
    throw new InvalidOperationException(
        "Jwt:SigningKey must be set via environment variable or secrets manager in non-Development.");

// ─── Data ───────────────────────────────────────────────────────────────────

builder.Services.AddSharedDataModel(connStr);

// ─── HTTP context ────────────────────────────────────────────────────────────

builder.Services.AddHttpContextAccessor();

// ─── ICurrentTenant (RLS) + ICurrentUser ────────────────────────────────────

builder.Services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
builder.Services.AddScoped<ICurrentUser,   HttpContextCurrentUser>();

// ─── Auth config ─────────────────────────────────────────────────────────────

builder.Services.Configure<JwtSettings>(jwtSection);

// ─── MediatR ─────────────────────────────────────────────────────────────────

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));
});

// ─── FluentValidation ────────────────────────────────────────────────────────

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// ─── AutoMapper ──────────────────────────────────────────────────────────────

builder.Services.AddAutoMapper(typeof(Program).Assembly);

// ─── Finance Seeder ──────────────────────────────────────────────────────────

builder.Services.AddScoped<FinanceSeeder>();

// ─── JWT Authentication (validate-only; this service does NOT issue tokens) ──
// Pinned algorithm: HmacSha256 — rejects algorithm-confusion attacks.

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

// ─── Authorization (permission policies; finance is admin-only — no CustomerOnly) ─

builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddAuthorization();

// ─── Health Checks ──────────────────────────────────────────────────────────

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString: connStr, name: "postgres", tags: ["ready"]);

// ─── OpenAPI ────────────────────────────────────────────────────────────────

builder.Services.AddOpenApi();

// ─── CORS ────────────────────────────────────────────────────────────────────

builder.Services.AddCors(opts =>
{
    if (builder.Environment.IsDevelopment())
        opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
    else
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        opts.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod());
    }
});

// ────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ─── Development seeding ─────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<FinanceSeeder>();
    await seeder.SeedAsync();
}

// ─── Middleware pipeline ──────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors();
app.UseMiddleware<ExceptionHandler>();
app.UseAuthentication();
// TenantResolutionMiddleware AFTER authentication, BEFORE authorization.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

// ─── Health endpoints ─────────────────────────────────────────────────────────

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
            checks   = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() })
        });
        await ctx.Response.WriteAsync(payload);
    }
});

// ─── API route groups ─────────────────────────────────────────────────────────
// FinanceEndpoints self-groups under /api/v1/admin with RequireAuthorization().

app.MapFinanceEndpoints();

// ─── Aspire default health endpoints (/health + /alive, Development only) ─────────────
app.MapDefaultEndpoints();

app.Run();

public partial class Program { }
