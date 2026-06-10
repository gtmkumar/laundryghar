// Laundry Ghar — Worker Service
// Drains two transactional outboxes (ADR-007):
//   1. engagement_cms.notifications_outbox  → NotificationDispatcherService
//   2. kernel.outbox_events                 → OutboxEventRelayService
//
// RLS / superuser note:
//   This process has NO tenant context (no HTTP request, no brand header).
//   It connects using the postgres superuser, which bypasses PostgreSQL RLS at the
//   database role level. WorkerCurrentTenant additionally sets BypassRls = true so
//   the RlsConnectionInterceptor emits SET app.bypass_rls = 'true', making the
//   intent explicit and providing a defence-in-depth layer.
//   Cross-brand queries work correctly; no RLS filter is applied.

using laundryghar.SharedDataModel;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Worker.Abstractions;
using laundryghar.Worker.Infrastructure;
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

// ─── Tenant context (worker-scope: bypasses RLS, no brand context) ───────────────────
// Registered as Scoped so the RlsConnectionInterceptor (also Scoped) resolves a fresh
// instance per DI scope (one per poll cycle via IServiceScopeFactory.CreateAsyncScope).
builder.Services.AddScoped<ICurrentTenant, WorkerCurrentTenant>();

// ─── Data ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddSharedDataModel(connStr, builder.Configuration, builder.Environment);

// ─── Channel sender and event publisher (dev stubs; swap for real providers in prod) ─
builder.Services.AddScoped<IChannelSender, LoggingChannelSender>();
builder.Services.AddScoped<IEventPublisher, LoggingEventPublisher>();

// ─── Background services ──────────────────────────────────────────────────────────────
builder.Services.AddHostedService<NotificationDispatcherService>();
builder.Services.AddHostedService<OutboxEventRelayService>();

// ─── Health checks ────────────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString: connStr,
        name: "postgres",
        tags: ["ready"]);

// ─── Build and run ────────────────────────────────────────────────────────────────────
var host = builder.Build();
await host.RunAsync();
