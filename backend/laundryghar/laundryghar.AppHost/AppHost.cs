// Laundry Ghar — Aspire AppHost
// Single entry-point that starts the 3 consolidated services + gateway under the Aspire dashboard.
//
//   core       @5050 = Identity + Engagement + Mcp
//   operations @5002 = Catalog + Orders + Warehouse + Logistics
//   commerce   @5005 = Commerce + Finance + Analytics + Worker (worker hosted services run in-process)
//   gateway    @8080 = single entry-point for all clients
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
// Default = non-superuser app_user (RLS ENFORCED at runtime).
var connStr = builder.Configuration["ConnectionStrings:Default"]
    ?? "Host=localhost;Port=5432;Database=laundry_ghar_db;Username=app_user;Password=app_user";

// Admin = postgres/superuser, injected for Development seeding only (bypasses RLS natively).
var adminConnStr = builder.Configuration["ConnectionStrings:Admin"]
    ?? "Host=localhost;Port=5432;Database=laundry_ghar_db;Username=postgres;Password=postgres";

// ── Shared dev PII key ──────────────────────────────────────────────────────────────────
// AppHost loads (or generates once) a single 32-byte key in its own keys/ directory and
// injects Pii__EncryptionKey into ALL services so the per-service auto-gen never diverges
// (one service encrypting with key-A while another decrypts with key-B).
// The key file is gitignored via **/keys/*.b64.
// Production: provide Pii__EncryptionKey via a secrets manager — the key file is never read.
var devPiiKeyBase64 = LoadOrGenerateDevPiiKey();

// ── 3 consolidated services — ports are FIXED (clients + appsettings hard-reference them) ──

// core = Identity + Engagement + Mcp.
builder
    .AddProject<Projects.core_WebApi>("core")
    .WithHttpEndpoint(port: 5050, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr)
    .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

// operations = Catalog + Orders + Warehouse + Logistics.
// Jwt__Authority overrides appsettings (which targets the standalone core port 5056) so that
// under the AppHost, JWKS validation points at core's fixed AppHost port 5050. Without this,
// operations fetches JWKS from the dead :5056 and rejects every token with 401.
builder
    .AddProject<Projects.operations_WebApi>("operations")
    .WithHttpEndpoint(port: 5002, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr)
    .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
    .WithEnvironment("Jwt__Authority", "http://localhost:5050")
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

// commerce = Commerce + Finance + Analytics + Worker.
// The Worker's hosted services run in-process here. They create scopes off the request
// pipeline (no HttpContext) and drain the outboxes with bypass_rls; the generic-host
// portion reads DOTNET_ENVIRONMENT, so inject it alongside ASPNETCORE_ENVIRONMENT or the
// PII cipher fails closed at startup (it would otherwise see "Production").
builder
    .AddProject<Projects.commerce_WebApi>("commerce")
    .WithHttpEndpoint(port: 5005, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr)
    .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
    .WithEnvironment("Jwt__Authority", "http://localhost:5050")
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

// ── API Gateway — single entry-point for all clients at :8080 ───────────────────────
// ADDITIVE: all 3 per-service direct ports remain active.
// Downstream cluster addresses are injected via env vars so YARP uses the same
// port-fixed addresses as the other resources.  No service-discovery magic — consistent
// with the "static ports" convention used throughout this AppHost.
//
// Path → cluster → consolidated service:
//   /identity,  /engagement, /mcp           → core       @5050
//   /catalog, /orders, /warehouse, /logistics → operations @5002
//   /commerce, /finance, /analytics         → commerce   @5005
builder
    .AddProject<Projects.laundryghar_Gateway>("gateway")
    .WithHttpEndpoint(port: 8080, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64)
    // Inject each downstream cluster address.  Overrides appsettings.json defaults;
    // double-underscore maps to Gateway:Clusters:{name}:Destinations:primary:Address.
    .WithEnvironment("Gateway__Clusters__identity__Destinations__primary__Address",   "http://localhost:5050")
    .WithEnvironment("Gateway__Clusters__engagement__Destinations__primary__Address", "http://localhost:5050")
    .WithEnvironment("Gateway__Clusters__mcp__Destinations__primary__Address",        "http://localhost:5050")
    .WithEnvironment("Gateway__Clusters__catalog__Destinations__primary__Address",    "http://localhost:5002")
    .WithEnvironment("Gateway__Clusters__orders__Destinations__primary__Address",     "http://localhost:5002")
    .WithEnvironment("Gateway__Clusters__warehouse__Destinations__primary__Address",  "http://localhost:5002")
    .WithEnvironment("Gateway__Clusters__logistics__Destinations__primary__Address",  "http://localhost:5002")
    .WithEnvironment("Gateway__Clusters__commerce__Destinations__primary__Address",   "http://localhost:5005")
    .WithEnvironment("Gateway__Clusters__finance__Destinations__primary__Address",    "http://localhost:5005")
    .WithEnvironment("Gateway__Clusters__analytics__Destinations__primary__Address",  "http://localhost:5005");

builder.Build().Run();

// ── Dev PII key bootstrap ─────────────────────────────────────────────────────────────
// Loads the shared dev key from the AppHost keys/ directory, generating it on first run.
// This file is gitignored — each dev machine has its own persistent key.
// If the key file is deleted all enc:v1 values in the dev DB become unreadable; the
// defensive catch in FromJson degrades to disabled defaults and a fresh key is generated
// on the next AppHost start.  Simply re-save any integration settings through the admin
// panel to re-encrypt with the new key.
static string LoadOrGenerateDevPiiKey()
{
    // Store the key next to the AppHost build output, so it is the same path regardless
    // of build configuration and survives between runs.
    var keyDir = Path.Combine(AppContext.BaseDirectory, "keys");
    var keyPath = Path.Combine(keyDir, "dev-pii-key.b64");

    Directory.CreateDirectory(keyDir);

    if (File.Exists(keyPath))
    {
        var existing = File.ReadAllText(keyPath).Trim();
        // Validate: must be 32 bytes when decoded.
        if (Convert.FromBase64String(existing).Length == 32)
            return existing;

        // File is corrupt — regenerate below.
        Console.WriteLine("[AppHost] WARNING: dev-pii-key.b64 was invalid; regenerating. " +
                          "Existing enc:v1 values in the dev DB will be unreadable until re-saved.");
    }
    else
    {
        Console.WriteLine("[AppHost] Generating new shared dev PII key → " + keyPath);
    }

    var keyBytes = new byte[32];
    System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
    var keyBase64 = Convert.ToBase64String(keyBytes);
    File.WriteAllText(keyPath, keyBase64);
    return keyBase64;
}
