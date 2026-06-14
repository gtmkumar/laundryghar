// ─────────────────────────────────────────────────────────────────────────────
// commerce.WebApi — consolidated host (Commerce + Finance + Analytics + Worker)
//
// Listening port: http://localhost:5005 (dev; fixed — gateway + clients hard-reference it)
//
// Composition root: wires the commerce bounded-context layers
//   • AddCommerceApplication()    → use cases / handlers / validators (commerce.Application)
//   • AddCommerceInfrastructure() → persistence / gateways / external services (commerce.Infrastructure)
// plus Aspire ServiceDefaults (OpenTelemetry, health checks, service discovery, resilience).
// ─────────────────────────────────────────────────────────────────────────────

using commerce.Application;
using commerce.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults (OTel, service discovery, /health + /alive) ─────────
builder.AddServiceDefaults();

// ── Commerce bounded-context composition ──────────────────────────────────────────
builder.Services
    .AddCommerceApplication()
    .AddCommerceInfrastructure();

var app = builder.Build();

// ── Forwarded headers (prod/staging, behind the gateway/edge proxy) ───────────
// Runs first so RemoteIpAddress/scheme reflect the real client. No-op unless
// ForwardedHeaders:Enabled = true.
app.UseForwardedHeadersIfEnabled();

// ── Aspire default health endpoints (/health + /alive) ────────────────────────
app.MapDefaultEndpoints();

// Liveness placeholder until the commerce HTTP lanes are mounted on this host.
app.MapGet("/", () => "commerce.WebApi (Commerce + Finance + Analytics + Worker) — up");

app.Run();
