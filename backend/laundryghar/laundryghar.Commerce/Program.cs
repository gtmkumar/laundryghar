using System.Text;
using FluentValidation;
using laundryghar.Commerce.Application.Common;
using laundryghar.Commerce.Endpoints;
using laundryghar.Commerce.Infrastructure.Auth;
using laundryghar.Commerce.Infrastructure.Gateway;
using laundryghar.Commerce.Infrastructure.Seeders;
using laundryghar.Commerce.Infrastructure.Services;
using laundryghar.Commerce.Middleware;
using laundryghar.SharedDataModel;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Middlewares.ExceptionsMiddleware;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

if (string.IsNullOrWhiteSpace(jwtSettings.Authority))
    throw new InvalidOperationException(
        "Jwt:Authority (the Identity issuer base URL whose JWKS publishes the RS256 public key) is required.");// ─── Data ──────────────────────────────────────────────────────────────────

builder.Services.AddSharedDataModel(connStr);

// ─── HTTP context + tenant/user ──────────────────────────────────────────────

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
builder.Services.AddScoped<ICurrentUser,   HttpContextCurrentUser>();

// ─── Auth config ───────────────────────────────────────────────────────────

builder.Services.Configure<JwtSettings>(jwtSection);

// ─── Payment gateway (dev stub; real Razorpay BSP swaps in here) ───────────

builder.Services.AddSingleton<IPaymentGateway, DevPaymentGateway>();

// ─── MediatR + FluentValidation + AutoMapper ────────────────────────────────

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);
builder.Services.AddAutoMapper(typeof(Program).Assembly);

// ─── JWT Authentication (validate-only; tokens issued by Identity) ──────────

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        // RS256: signing keys fetched from Identity JWKS (no shared secret).
        opts.Authority            = jwtSettings.Authority;
        opts.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtSettings.Issuer,
            ValidAudience            = jwtSettings.Audience,
            ClockSkew                = TimeSpan.FromSeconds(30),
            ValidAlgorithms          = new[] { SecurityAlgorithms.RsaSha256 }
        };
    });

// ─── Authorization (permission policies + CustomerOnly) ────────────────────

builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, CustomerOnlyHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddAuthorization();

// ─── Health / OpenAPI / CORS ────────────────────────────────────────────────

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString: connStr, name: "postgres", tags: ["ready"]);
builder.Services.AddOpenApi();
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

// ─── Seeder ──────────────────────────────────────────────────────────────────

builder.Services.AddScoped<CommerceSeeder>();

// ─────────────────────────────────────────────────────────────────────────

var app = builder.Build();

var runSeed = args.Contains("--seed") || app.Environment.IsDevelopment();
if (runSeed)
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("--seed is not permitted outside Development.");
    using var scope = app.Services.CreateScope();
    // Seed on the privileged Admin (postgres) connection — runtime uses app_user (RLS-enforced),
    // which would reject cross-brand bootstrap INSERTs that carry no request tenant context.
    using var seedDb = laundryghar.SharedDataModel.SeedingSupport.CreatePrivilegedContext(
        app.Configuration.GetConnectionString("Admin") ?? connStr);
    var seeder = ActivatorUtilities.CreateInstance<CommerceSeeder>(scope.ServiceProvider, seedDb);
    await seeder.SeedAsync();
}

// ─── Middleware pipeline ────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors();
app.UseMiddleware<ExceptionHandler>();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

// ─── Health endpoints ──────────────────────────────────────────────────────

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

// ─── API route groups ──────────────────────────────────────────────────────

var v1 = app.MapGroup("/api/v1");

var admin = v1.MapGroup("/admin").RequireAuthorization();
admin.MapAdminCommerceEndpoints();

var customer = v1.MapGroup("/customer").RequireAuthorization("CustomerOnly");
customer.MapCustomerCommerceEndpoints();

// ─── Aspire default health endpoints (/health + /alive, Development only) ─────────────
app.MapDefaultEndpoints();

app.Run();

public partial class Program { }
