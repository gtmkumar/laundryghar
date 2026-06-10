// Laundry Ghar — Worker Service
// Drains two transactional outboxes (ADR-007):
//   1. engagement_cms.notifications_outbox  → NotificationDispatcherService
//   2. kernel.outbox_events                 → OutboxEventRelayService
//                                           + NotificationMappingService (lifecycle → outbox)
//
// Channel senders are config-driven and fail-safe:
//   - Zero Notifications:* config  → all channels fall back to LoggingChannelSender (dev stub)
//   - Notifications:WhatsApp:Enabled=true + creds → real WhatsApp Cloud API
//   - Notifications:Sms:Enabled=true + creds      → real MSG91 SMS
//   - Notifications:Push:Enabled=true             → real Expo Push API
//
// RLS / superuser note:
//   This process has NO tenant context (no HTTP request, no brand header).
//   It connects using the postgres superuser, which bypasses PostgreSQL RLS at the
//   database role level. WorkerCurrentTenant additionally sets BypassRls = true so
//   the RlsConnectionInterceptor emits SET app.bypass_rls = 'true', making the
//   intent explicit and providing a defence-in-depth layer.
//   Cross-brand queries work correctly; no RLS filter is applied.

using System.Net.Http.Headers;
using laundryghar.SharedDataModel;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Worker.Abstractions;
using laundryghar.Worker.Infrastructure;
using laundryghar.Worker.Infrastructure.Channels;
using laundryghar.Worker.Infrastructure.Stubs;
using laundryghar.Worker.Options;
using laundryghar.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

// ─── Aspire ServiceDefaults (OTel, service discovery, resilience, health check) ──────
builder.AddServiceDefaults();

// ─── Configuration ────────────────────────────────────────────────────────────────────
var connStr = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

builder.Services.Configure<WorkerOptions>(
    builder.Configuration.GetSection(WorkerOptions.SectionName));

// ─── Notification provider options ───────────────────────────────────────────────────
builder.Services.Configure<WhatsAppOptions>(
    builder.Configuration.GetSection(WhatsAppOptions.SectionName));
builder.Services.Configure<SmsOptions>(
    builder.Configuration.GetSection(SmsOptions.SectionName));
builder.Services.Configure<PushOptions>(
    builder.Configuration.GetSection(PushOptions.SectionName));

// ─── Tenant context (worker-scope: bypasses RLS, no brand context) ───────────────────
// Registered as Scoped so the RlsConnectionInterceptor (also Scoped) resolves a fresh
// instance per DI scope (one per poll cycle via IServiceScopeFactory.CreateAsyncScope).
builder.Services.AddScoped<ICurrentTenant, WorkerCurrentTenant>();

// ─── Data ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddSharedDataModel(connStr, builder.Configuration, builder.Environment);

// ─── HTTP clients for notification providers ─────────────────────────────────────────
// Named clients with appropriate default headers; timeout is kept short (10 s) so a
// slow provider doesn't block the poll loop indefinitely.
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

// ─── Channel senders (fail-safe conditional registration) ────────────────────────────
// LoggingChannelSender is ALWAYS registered — it is the universal fallback.
builder.Services.AddScoped<LoggingChannelSender>();

// Real senders are registered only when enabled; the RoutingChannelSender
// receives them as optional nullable constructor parameters.
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
    builder.Services.AddScoped<WhatsAppCloudChannelSender>(_ => null!); // Resolved as null in router

if (smsEnabled)
    builder.Services.AddScoped<Msg91SmsChannelSender>();
else
    builder.Services.AddScoped<Msg91SmsChannelSender>(_ => null!);

if (pushEnabled)
    builder.Services.AddScoped<ExpoPushChannelSender>();
else
    builder.Services.AddScoped<ExpoPushChannelSender>(_ => null!);

// The routing sender IS the IChannelSender resolved by the dispatcher.
builder.Services.AddScoped<IChannelSender, RoutingChannelSender>();

// ─── Event publisher (dev stub — outbox relay publishes to log only) ──────────────────
builder.Services.AddScoped<IEventPublisher, LoggingEventPublisher>();

// ─── Background services ──────────────────────────────────────────────────────────────
builder.Services.AddHostedService<NotificationDispatcherService>();
builder.Services.AddHostedService<OutboxEventRelayService>();
builder.Services.AddHostedService<NotificationMappingService>();
builder.Services.AddHostedService<CustomerErasureService>();
builder.Services.AddHostedService<RetentionSweepService>();
builder.Services.AddHostedService<AutoDispatchService>();         // opt-in: AutoDispatch:Enabled=true
builder.Services.AddHostedService<RoyaltyGenerationService>();    // opt-in: Worker:RoyaltyGenerationEnabled=true
builder.Services.AddHostedService<DailyReconService>();           // opt-in: Worker:DailyReconEnabled=true
builder.Services.AddHostedService<SubscriptionBillingService>(); // opt-in: Worker:SubscriptionBillingEnabled=true

// ─── Health checks ────────────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString: connStr,
        name: "postgres",
        tags: ["ready"]);

// ─── Build and run ────────────────────────────────────────────────────────────────────
var host = builder.Build();
await host.RunAsync();
