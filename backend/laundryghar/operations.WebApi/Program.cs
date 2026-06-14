// ─────────────────────────────────────────────────────────────────────────────
// operations.WebApi — consolidated host (Catalog + Orders + Warehouse + Logistics)
//
// Listening port: http://localhost:5002 (dev; fixed — gateway + clients hard-reference it)
//
// Composition root: wires the operations bounded-context layers
//   • AddOperationsApplication()    → use cases / handlers / validators (operations.Application)
//   • AddOperationsInfrastructure() → persistence / gateways / external services (operations.Infrastructure)
// plus Aspire ServiceDefaults (OpenTelemetry, health checks, service discovery, resilience).
// ─────────────────────────────────────────────────────────────────────────────

using operations.Application;
using operations.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults (OTel, service discovery, /health + /alive) ─────────
builder.AddServiceDefaults();

// ── Operations bounded-context composition ──────────────────────────────────────────
builder.Services
    .AddOperationsApplication()
    .AddOperationsInfrastructure();

var app = builder.Build();

// ── Forwarded headers (prod/staging, behind the gateway/edge proxy) ───────────
// Runs first so RemoteIpAddress/scheme reflect the real client. No-op unless
// ForwardedHeaders:Enabled = true.
app.UseForwardedHeadersIfEnabled();

// ── Aspire default health endpoints (/health + /alive) ────────────────────────
app.MapDefaultEndpoints();

// Liveness placeholder until the operations HTTP lanes are mounted on this host.
app.MapGet("/", () => "operations.WebApi (Catalog + Orders + Warehouse + Logistics) — up");

app.Run();
