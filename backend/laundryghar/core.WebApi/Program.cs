// ─────────────────────────────────────────────────────────────────────────────
// core.WebApi — consolidated host (Identity + Engagement + Mcp)
//
// Listening port: http://localhost:5050 (dev; fixed — gateway + clients hard-reference it)
//
// Composition root: wires the core bounded-context layers
//   • AddCoreApplication()    → use cases / handlers / validators (core.Application)
//   • AddCoreInfrastructure() → persistence / gateways / external services (core.Infrastructure)
// plus Aspire ServiceDefaults (OpenTelemetry, health checks, service discovery, resilience).
// ─────────────────────────────────────────────────────────────────────────────

using System.Reflection;
using core.Application;
using core.Infrastructure;
using laundryghar.SharedDataModel;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults (OTel, service discovery, /health + /alive) ─────────
builder.AddServiceDefaults();

// ── Current user (ICurrentUser from request principal) ─────────────────────────
builder.Services.AddCurrentUser();

// ── Shared data model: LaundryGharDbContext (+ generic repo wiring) ────────────
// NOTE: an ICurrentTenant implementation must also be registered for RLS at runtime.
builder.Services.AddSharedDataModel(
    builder.Configuration.GetConnectionString("Default") ?? string.Empty,
    builder.Configuration,
    builder.Environment);

// ── Core bounded-context composition ──────────────────────────────────────────
builder.Services
    .AddCoreApplication()      // validators + command/query handlers (no mediator)
    .AddCoreInfrastructure();  // feature repositories

var app = builder.Build();

// ── Forwarded headers (prod/staging, behind the gateway/edge proxy) ───────────
// Runs first so RemoteIpAddress/scheme reflect the real client. No-op unless
// ForwardedHeaders:Enabled = true.
app.UseForwardedHeadersIfEnabled();

// ── Aspire default health endpoints (/health + /alive) ────────────────────────
app.MapDefaultEndpoints();

// ── Feature endpoints — discovered from IEndpointGroup classes in this assembly ─
app.MapEndpoints(Assembly.GetExecutingAssembly());

app.Run();
