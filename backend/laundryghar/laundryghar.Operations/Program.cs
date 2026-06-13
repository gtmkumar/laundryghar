using FluentValidation;
using laundryghar.Operations;
using laundryghar.SharedDataModel;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Middlewares.ExceptionsMiddleware;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// ─── Domain endpoint extensions ────────────────────────────────────────────
using laundryghar.Catalog.Endpoints;
using laundryghar.Orders.Endpoints;
using laundryghar.Warehouse.Endpoints;
using laundryghar.Logistics.Endpoints;

// ─── Shared infra types (sourced from one domain; identical across the four) ─
// JwtSettings + ICurrentTenant/User adapters + TenantResolutionMiddleware come from the Orders
// namespace; all four services define byte-equivalent copies, so any one is behaviour-preserving.
using JwtSettings                 = laundryghar.Orders.Infrastructure.Auth.JwtSettings;
using HttpContextCurrentTenant    = laundryghar.Orders.Infrastructure.Services.HttpContextCurrentTenant;
using HttpContextCurrentUserOrders = laundryghar.Orders.Infrastructure.Services.HttpContextCurrentUser;
using TenantResolutionMiddleware  = laundryghar.Orders.Middleware.TenantResolutionMiddleware;

// ─── Per-domain ICurrentUser adapters (interfaces differ per domain — register all four) ─
using CatalogCurrentUser     = laundryghar.Catalog.Infrastructure.Services.HttpContextCurrentUser;
using ICatalogCurrentUser    = laundryghar.Catalog.Infrastructure.Services.ICurrentUser;
using IOrdersCurrentUser     = laundryghar.Orders.Infrastructure.Services.ICurrentUser;
using WarehouseCurrentUser   = laundryghar.Warehouse.Infrastructure.Services.HttpContextCurrentUser;
using IWarehouseCurrentUser  = laundryghar.Warehouse.Infrastructure.Services.ICurrentUser;
using LogisticsCurrentUser   = laundryghar.Logistics.Infrastructure.Services.HttpContextCurrentUser;
using ILogisticsCurrentUser  = laundryghar.Logistics.Infrastructure.Services.ICurrentUser;

// ─── Authorization handlers (union across all four domains) ─────────────────
using PermissionHandler     = laundryghar.Orders.Infrastructure.Auth.PermissionHandler;
using AnyPermissionHandler  = laundryghar.Orders.Infrastructure.Auth.AnyPermissionHandler;
using CustomerOnlyHandler   = laundryghar.Orders.Infrastructure.Auth.CustomerOnlyHandler;
using RiderOnlyHandler      = laundryghar.Logistics.Infrastructure.Auth.RiderOnlyHandler;

// ─── Settings + seeders ─────────────────────────────────────────────────────
using OrdersSettings = laundryghar.Orders.Application.Common.OrdersSettings;
using CatalogSeeder  = laundryghar.Catalog.Infrastructure.Seeders.CatalogSeeder;
using OrdersSeeder   = laundryghar.Orders.Infrastructure.Seeders.OrdersSeeder;

var builder = WebApplication.CreateBuilder(args);

// ─── Aspire ServiceDefaults (OTel, service discovery, resilience, /health + /alive) ─────
builder.AddServiceDefaults();

// ─── Configuration ──────────────────────────────────────────────────────────

var connStr = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

var jwtSection  = builder.Configuration.GetSection(JwtSettings.SectionName);
var jwtSettings = jwtSection.Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt section is required.");

if (string.IsNullOrWhiteSpace(jwtSettings.Authority))
    throw new InvalidOperationException(
        "Jwt:Authority (the Identity issuer base URL whose JWKS publishes the RS256 public key) is required.");

// ─── Data ────────────────────────────────────────────────────────────────────

builder.Services.AddSharedDataModel(connStr, builder.Configuration, builder.Environment);

// ─── HTTP context ──────────────────────────────────────────────────────────

builder.Services.AddHttpContextAccessor();

// ─── ICurrentTenant (RLS) + per-domain ICurrentUser ────────────────────────
// One ICurrentTenant adapter (shared SharedDataModel.Contracts.ICurrentTenant) serves the whole
// service. Each domain keeps its own ICurrentUser interface (the member sets differ), so all four
// concrete HttpContextCurrentUser adapters are registered against their respective interfaces.

builder.Services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
builder.Services.AddScoped<ICatalogCurrentUser,   CatalogCurrentUser>();
builder.Services.AddScoped<IOrdersCurrentUser,    HttpContextCurrentUserOrders>();
builder.Services.AddScoped<IWarehouseCurrentUser, WarehouseCurrentUser>();
builder.Services.AddScoped<ILogisticsCurrentUser, LogisticsCurrentUser>();

// ─── Auth config ───────────────────────────────────────────────────────────

builder.Services.Configure<JwtSettings>(jwtSection);

// ─── Orders settings ───────────────────────────────────────────────────────

builder.Services.Configure<OrdersSettings>(
    builder.Configuration.GetSection(OrdersSettings.SectionName));

// ─── MediatR ───────────────────────────────────────────────────────────────
// One assembly scan covers handlers from all four domains. A single ValidationPipelineBehavior is
// registered (the Orders copy); the per-domain copies are behaviourally identical, so running one
// preserves validation exactly while avoiding duplicate execution.

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>),
        typeof(laundryghar.Orders.Application.Common.ValidationPipelineBehavior<,>));
});

// ─── FluentValidation ──────────────────────────────────────────────────────
// One scan registers every validator from all four domains.

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// ─── JWT Authentication (validate-only; this service does NOT issue tokens) ─
// RS256 — signing keys fetched from Identity JWKS. Identical config across all four source services.

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
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
            // Pin to RS256 — reject "none" and HMAC algorithm-confusion attacks.
            ValidAlgorithms          = new[] { SecurityAlgorithms.RsaSha256 }
        };
    });

// ─── Authorization ──────────────────────────────────────────────────────────
// Union of all four services' handlers. A single merged policy provider recognises every policy
// name the four services used: permission:<code>, permission:<a>|<b> (any-of), CustomerOnly, RiderOnly.

builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, AnyPermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, CustomerOnlyHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, RiderOnlyHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, OperationsPolicyProvider>();
builder.Services.AddAuthorization();

// ─── Health Checks ─────────────────────────────────────────────────────────

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString: connStr, name: "postgres", tags: ["ready"]);

// ─── OpenAPI ───────────────────────────────────────────────────────────────

builder.Services.AddOpenApi();

// ─── CORS ──────────────────────────────────────────────────────────────────

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

// ─── Seeders ───────────────────────────────────────────────────────────────

builder.Services.AddScoped<CatalogSeeder>();
builder.Services.AddScoped<OrdersSeeder>();

// ─────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ─── Run seeders ───────────────────────────────────────────────────────────

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

    var catalogSeeder = ActivatorUtilities.CreateInstance<CatalogSeeder>(scope.ServiceProvider, seedDb);
    await catalogSeeder.SeedAsync();

    var ordersSeeder = ActivatorUtilities.CreateInstance<OrdersSeeder>(scope.ServiceProvider, seedDb);
    await ordersSeeder.SeedAsync();
}

// ─── Middleware pipeline ────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// No-op in Development. Must run before UseCors so headers appear on preflight responses.
app.UseSecurityHeaders();
app.UseCors();
app.UseMiddleware<ExceptionHandler>();
app.UseAuthentication();
// TenantResolutionMiddleware AFTER authentication, BEFORE authorization.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

// ─── Health endpoints ──────────────────────────────────────────────────────

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
            checks   = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = exposeHealthDescriptions ? e.Value.Description : null
            })
        });
        await ctx.Response.WriteAsync(payload);
    }
});

// ─── API route groups ──────────────────────────────────────────────────────
// Catalog + Orders map onto shared /api/v1/admin and /api/v1/customer groups (RouteGroupBuilder
// extensions). Warehouse + Logistics self-group on the app at their full /api/v1/* prefixes.
// Every route path, verb, and policy below is unchanged from the source services.

var v1 = app.MapGroup("/api/v1");

// Admin routes — require authentication (per-endpoint permission policies applied inside).
var admin = v1.MapGroup("/admin").RequireAuthorization();
admin.MapAdminCatalogEndpoints();
admin.MapAdminPricingEndpoints();
admin.MapAdminCustomerEndpoints();
admin.MapAdminOrderEndpoints();
admin.MapAdminPickupEndpoints();
admin.MapAdminInvoiceEndpoints();

// Customer-facing routes — CustomerOnly policy.
var customer = v1.MapGroup("/customer").RequireAuthorization("CustomerOnly");
customer.MapCustomerEndpoints();          // Catalog: catalog browse, addresses, serviceability
customer.MapCustomerOrderEndpoints();     // Orders
customer.MapCustomerInvoiceEndpoints();   // Orders

// Warehouse — self-groups under /api/v1/admin with its own RequireAuthorization().
app.MapWarehouseEndpoints();

// Logistics — self-groups under /api/v1/admin (rider mgmt/dispatch) and /api/v1/rider (RiderOnly).
app.MapLogisticsEndpoints();

// ─── Aspire default health endpoints (/health + /alive, Development only) ─────────────
app.MapDefaultEndpoints();

app.Run();

// For integration testing
public partial class Program { }
