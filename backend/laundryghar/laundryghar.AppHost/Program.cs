// Laundry Ghar — Aspire AppHost
// Single entry-point that starts all 9 services under the Aspire dashboard.
//
// Usage:
//   ASPNETCORE_ENVIRONMENT=Development dotnet run --project laundryghar.AppHost
//
// Dashboard: the URL (with login token) is printed to stdout on startup.
// DB: the existing laundry_ghar_db is referenced via config — NO container is spun up.
//     Set the connection string in appsettings.Development.json or via env var:
//       ConnectionStrings__Default=Host=localhost;Port=5432;Database=laundry_ghar_db;...

var builder = DistributedApplication.CreateBuilder(args);

// ── Resolve the shared Postgres connection string from AppHost config ───────────────────
// Source priority: env var (ConnectionStrings__Default) > appsettings.Development.json > appsettings.json > literal fallback
// The literal fallback is only used when no config value is present (should not happen in normal dev flow).
var connStr = builder.Configuration["ConnectionStrings:Default"]
    ?? "Host=localhost;Port=5432;Database=laundry_ghar_db;Username=postgres;Password=postgres";

// ── 9 microservices — ports are FIXED (clients + appsettings hard-reference them) ─────

builder
    .AddProject<Projects.laundryghar_Identity>("identity")
    .WithHttpEndpoint(port: 5050, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr);

builder
    .AddProject<Projects.laundryghar_Catalog>("catalog")
    .WithHttpEndpoint(port: 5001, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr);

builder
    .AddProject<Projects.laundryghar_Orders>("orders")
    .WithHttpEndpoint(port: 5002, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr);

builder
    .AddProject<Projects.laundryghar_Warehouse>("warehouse")
    .WithHttpEndpoint(port: 5003, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr);

builder
    .AddProject<Projects.laundryghar_Logistics>("logistics")
    .WithHttpEndpoint(port: 5004, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr);

builder
    .AddProject<Projects.laundryghar_Commerce>("commerce")
    .WithHttpEndpoint(port: 5005, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr);

builder
    .AddProject<Projects.laundryghar_Finance>("finance")
    .WithHttpEndpoint(port: 5006, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr);

builder
    .AddProject<Projects.laundryghar_Engagement>("engagement")
    .WithHttpEndpoint(port: 5007, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr);

builder
    .AddProject<Projects.laundryghar_Analytics>("analytics")
    .WithHttpEndpoint(port: 5008, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr);

// ── Worker — no HTTP endpoint; drains notifications_outbox + outbox_events ──────────
builder
    .AddProject<Projects.laundryghar_Worker>("worker")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr);

builder.Build().Run();
