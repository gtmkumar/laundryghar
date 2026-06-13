// ─────────────────────────────────────────────────────────────────────────────
// laundryghar.Commerce — consolidated host (Commerce + Finance + Analytics + Worker)
//
// HTTP lanes (port 5005):
//   • Commerce  → /api/v1/admin/commerce/*, /api/v1/customer/commerce/*,
//                 /api/v1/webhooks/razorpay (anonymous, HMAC-verified)
//   • Finance   → /api/v1/admin/* (cash books, expenses, royalty, settlements; admin-only)
//   • Analytics → /api/v1/admin/analytics/* (dashboards) + MatviewRefreshService
//
// In-process Worker lane (no HTTP): all background hosted services from laundryghar.Worker.
//
// Worker-vs-HTTP tenant/connection design (see HostTenant/CommerceHostCurrentTenant.cs):
//   A single LaundryGharDbContext is registered (AddSharedDataModel) on
//   ConnectionStrings:Default (app_user, RLS-enforced). The Scoped RlsConnectionInterceptor
//   resolves ONE Scoped ICurrentTenant per scope. CommerceHostCurrentTenant dispatches on the
//   presence of an ambient HttpContext: HTTP request scopes resolve tenant from JWT claims
//   (RLS enforced); worker hosted-service scopes (no HttpContext) get BypassRls = true so the
//   interceptor emits SET app.bypass_rls = 'true' — identical to the standalone Worker. RLS is
//   never weakened for HTTP lanes. (The standalone Worker also used an app_user Default
//   connection and relied on the bypass_rls flag, so no second connection string is required.)
// ─────────────────────────────────────────────────────────────────────────────

using System.Net.Http.Headers;
using FluentValidation;
using laundryghar.Commerce.HostTenant;
using laundryghar.SharedDataModel;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Middlewares.ExceptionsMiddleware;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// Commerce lane
using laundryghar.Commerce.Endpoints;
using laundryghar.Commerce.Infrastructure.Gateway;
using laundryghar.Commerce.Infrastructure.Seeders;
// Finance lane
using laundryghar.Finance.Endpoints;
using laundryghar.Finance.Infrastructure.Seeders;
// Analytics lane
using laundryghar.Analytics.Endpoints;
using laundryghar.Analytics.Infrastructure.Services;
// Worker lane
using laundryghar.Worker.Abstractions;
using laundryghar.Worker.Infrastructure;
using laundryghar.Worker.Infrastructure.Channels;
using laundryghar.Worker.Infrastructure.Stubs;
using laundryghar.Worker.Options;
using laundryghar.Worker.Services;

// Disambiguate the per-lane Auth/Services types (identically named across namespaces).
using CommerceJwt        = laundryghar.Commerce.Infrastructure.Auth.JwtSettings;
using CommercePermPolicy = laundryghar.Commerce.Infrastructure.Auth.PermissionPolicyProvider;
using CommercePermHandler = laundryghar.Commerce.Infrastructure.Auth.PermissionHandler;
using CommerceCustomerOnlyHandler = laundryghar.Commerce.Infrastructure.Auth.CustomerOnlyHandler;

var builder = WebApplication.CreateBuilder(args);

// ─── Aspire ServiceDefaults (OTel, service discovery, resilience, /health + /alive) ─────
builder.AddServiceDefaults();

// ─── Configuration ───────────────────────────────────────────────────────────
var connStr = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

var jwtSection  = builder.Configuration.GetSection(CommerceJwt.SectionName);
var jwtSettings = jwtSection.Get<CommerceJwt>()
    ?? throw new InvalidOperationException("Jwt section is required.");

if (string.IsNullOrWhiteSpace(jwtSettings.Authority))
    throw new InvalidOperationException(
        "Jwt:Authority (the Identity issuer base URL whose JWKS publishes the RS256 public key) is required.");

// ─── Data (single shared DbContext on app_user; RLS enforced for HTTP) ────────
builder.Services.AddSharedDataModel(connStr, builder.Configuration, builder.Environment);

// ─── HTTP context + tenant/user ───────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();

// ONE ICurrentTenant: dispatches HTTP-claims vs worker-bypass on HttpContext presence.
builder.Services.AddScoped<ICurrentTenant, CommerceHostCurrentTenant>();

// Each HTTP lane has its OWN ICurrentUser interface + HttpContextCurrentUser concrete
// (distinct types in distinct namespaces). Register all three so handlers in each lane
// resolve their own. They share identical claim-reading semantics.
builder.Services.AddScoped<laundryghar.Commerce.Infrastructure.Services.ICurrentUser,
                           laundryghar.Commerce.Infrastructure.Services.HttpContextCurrentUser>();
builder.Services.AddScoped<laundryghar.Finance.Infrastructure.Services.ICurrentUser,
                           laundryghar.Finance.Infrastructure.Services.HttpContextCurrentUser>();
builder.Services.AddScoped<laundryghar.Analytics.Infrastructure.Services.ICurrentUser,
                           laundryghar.Analytics.Infrastructure.Services.HttpContextCurrentUser>();

// ─── Auth config ───────────────────────────────────────────────────────────────
builder.Services.Configure<CommerceJwt>(jwtSection);

// ─── Payment gateway (Commerce) ───────────────────────────────────────────────
// Development → DevPaymentGateway (stub). Else → SettingsFirstPaymentGateway
// (DB settings → env config → fail-closed), with the named "razorpay" HttpClient.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IPaymentGateway, DevPaymentGateway>();
}
else
{
    var rzpSection = builder.Configuration.GetSection(RazorpaySettings.SectionName);
    builder.Services.Configure<RazorpaySettings>(rzpSection);

    builder.Services.AddHttpClient("razorpay", http =>
    {
        http.BaseAddress = new Uri("https://api.razorpay.com/");
        http.Timeout     = TimeSpan.FromSeconds(30);
    });

    builder.Services.AddSingleton<GatewaySettingsCache>();
    builder.Services.AddScoped<IPaymentGateway, SettingsFirstPaymentGateway>();
}

// ─── MediatR + FluentValidation (single registration over the merged assembly) ─
// Commerce + Finance handlers/validators all live in this assembly now. One scan covers
// both. A single ValidationPipelineBehavior (Commerce's) is registered as the open-generic
// IPipelineBehavior — Commerce's and Finance's behaviors were byte-for-byte equivalent, so
// registering both would double-validate. (Reported in handover.)
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>),
        typeof(laundryghar.Commerce.Application.Common.ValidationPipelineBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// ─── JWT Authentication (validate-only; tokens issued by Identity via RS256/JWKS) ─
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
            ValidAlgorithms          = new[] { SecurityAlgorithms.RsaSha256 }
        };
    });

// ─── Authorization ─────────────────────────────────────────────────────────────
// Commerce's PermissionPolicyProvider is the strict SUPERSET: it serves both
// "permission:<code>" (used by all three lanes) AND "CustomerOnly" (Commerce only).
// Finance/Analytics providers handled only "permission:<code>" — dropped as redundant.
// PermissionHandler is byte-equivalent across lanes; Commerce's is registered once.
builder.Services.AddSingleton<IAuthorizationHandler, CommercePermHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, CommerceCustomerOnlyHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, CommercePermPolicy>();
builder.Services.AddAuthorization();

// ─── Analytics: periodic materialized-view refresh ────────────────────────────
builder.Services.AddHostedService<MatviewRefreshService>();

// ─── Worker: notification provider options ────────────────────────────────────
builder.Services.Configure<WorkerOptions>(
    builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.Configure<WhatsAppOptions>(
    builder.Configuration.GetSection(WhatsAppOptions.SectionName));
builder.Services.Configure<SmsOptions>(
    builder.Configuration.GetSection(SmsOptions.SectionName));
builder.Services.Configure<PushOptions>(
    builder.Configuration.GetSection(PushOptions.SectionName));

// ─── Worker: notification provider HTTP clients ───────────────────────────────
builder.Services.AddHttpClient("whatsapp", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    var accessToken = builder.Configuration["Notifications:WhatsApp:AccessToken"];
    if (!string.IsNullOrWhiteSpace(accessToken))
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
});
builder.Services.AddHttpClient("sms", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    var authKey = builder.Configuration["Notifications:Sms:AuthKey"];
    if (!string.IsNullOrWhiteSpace(authKey))
        client.DefaultRequestHeaders.Add("authkey", authKey);
});
builder.Services.AddHttpClient("push", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    var accessToken = builder.Configuration["Notifications:Push:AccessToken"];
    if (!string.IsNullOrWhiteSpace(accessToken))
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
});

// ─── Worker: settings-first notification cache (TTL 60 s, singleton) ──────────
builder.Services.AddSingleton<NotificationSettingsCache>();

// ─── Worker: channel senders (fail-safe conditional registration) ─────────────
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

// ─── Worker: event publisher (dev stub) ───────────────────────────────────────
builder.Services.AddScoped<IEventPublisher, LoggingEventPublisher>();

// ─── Worker: subscription charger (fail-closed seam; dev stub in Development) ──
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<ISubscriptionCharger, DevSubscriptionCharger>();

// ─── Worker: background hosted services (opt-in flags unchanged) ──────────────
builder.Services.AddHostedService<NotificationDispatcherService>();
builder.Services.AddHostedService<OutboxEventRelayService>();
builder.Services.AddHostedService<NotificationMappingService>();
builder.Services.AddHostedService<CustomerErasureService>();
builder.Services.AddHostedService<RetentionSweepService>();
builder.Services.AddHostedService<AutoDispatchService>();         // opt-in: AutoDispatch:Enabled=true
builder.Services.AddHostedService<RoyaltyGenerationService>();    // opt-in: Worker:RoyaltyGenerationEnabled=true
builder.Services.AddHostedService<DailyReconService>();           // opt-in: Worker:DailyReconEnabled=true
builder.Services.AddHostedService<SubscriptionBillingService>();  // opt-in: Worker:SubscriptionBillingEnabled=true
builder.Services.AddHostedService<LoyaltyEarnService>();          // mandatory
builder.Services.AddHostedService<PartitionMaintenanceService>(); // DEFECT 5b: on by default (Worker:PartitionMaintenanceEnabled=false to opt out)

// ─── Seeders (Commerce + Finance; Development only) ───────────────────────────
builder.Services.AddScoped<CommerceSeeder>();
builder.Services.AddScoped<FinanceSeeder>();

// ─── Health / OpenAPI / CORS ───────────────────────────────────────────────────
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

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ─── Development seeding (Commerce + Finance, privileged Admin connection) ─────
var runSeed = args.Contains("--seed") || app.Environment.IsDevelopment();
if (runSeed)
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("--seed is not permitted outside Development.");

    using var scope = app.Services.CreateScope();
    var adminConn = app.Configuration.GetConnectionString("Admin") ?? connStr;

    using (var seedDb = laundryghar.SharedDataModel.SeedingSupport.CreatePrivilegedContext(adminConn))
    {
        var commerceSeeder = ActivatorUtilities.CreateInstance<CommerceSeeder>(scope.ServiceProvider, seedDb);
        await commerceSeeder.SeedAsync();
    }
    using (var seedDb = laundryghar.SharedDataModel.SeedingSupport.CreatePrivilegedContext(adminConn))
    {
        var financeSeeder = ActivatorUtilities.CreateInstance<FinanceSeeder>(scope.ServiceProvider, seedDb);
        await financeSeeder.SeedAsync();
    }
}

// ─── Middleware pipeline (union; ordering identical to the source services) ────
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// No-op in Development. Must run before UseCors so headers appear on preflight responses.
app.UseSecurityHeaders();
app.UseCors();
app.UseMiddleware<ExceptionHandler>();
app.UseAuthentication();
// One shared tenant-resolution middleware (Commerce's — byte-equivalent across lanes):
// runs AFTER authentication, BEFORE authorization; sets bypass_rls + brand_id_override
// for platform admins.
app.UseMiddleware<laundryghar.Commerce.Middleware.TenantResolutionMiddleware>();
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
            checks   = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() })
        });
        await ctx.Response.WriteAsync(payload);
    }
});

// ─── API route groups ──────────────────────────────────────────────────────────

// Commerce: /api/v1/admin/commerce/* and /api/v1/customer/commerce/*
var v1 = app.MapGroup("/api/v1");

var commerceAdmin = v1.MapGroup("/admin").RequireAuthorization();
commerceAdmin.MapAdminCommerceEndpoints();

var commerceCustomer = v1.MapGroup("/customer").RequireAuthorization("CustomerOnly");
commerceCustomer.MapCustomerCommerceEndpoints();

// Commerce: anonymous Razorpay webhook — auth IS the HMAC signature.
// Reads the RAW body BEFORE any model binding (needed for HMAC) and sets bypass_rls so the
// RLS interceptor skips brand filtering on this anonymous path (handler filters by gateway ids).
v1.MapPost("/webhooks/razorpay", async (HttpContext http, ISender sender, CancellationToken ct) =>
{
    http.Request.EnableBuffering();
    using var ms = new System.IO.MemoryStream();
    await http.Request.Body.CopyToAsync(ms, ct);
    var rawBody = ms.ToArray();

    var signature = http.Request.Headers["X-Razorpay-Signature"].FirstOrDefault();

    http.Items["bypass_rls"] = true;

    var result = await sender.Send(
        new laundryghar.Commerce.Application.Webhooks.ProcessRazorpayWebhookCommand(rawBody, signature), ct);

    return result.Accepted ? Results.Ok() : Results.BadRequest(result.Reason);
}).AllowAnonymous();

// Finance: self-groups under /api/v1/admin with RequireAuthorization().
app.MapFinanceEndpoints();

// Analytics: self-groups under /api/v1/admin/analytics with RequireAuthorization().
app.MapAnalyticsEndpoints();

// ─── Aspire default health endpoints (/health + /alive, Development only) ──────
app.MapDefaultEndpoints();

app.Run();

public partial class Program { }
