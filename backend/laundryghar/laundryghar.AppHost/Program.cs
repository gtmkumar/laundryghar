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
// Default = non-superuser app_user (RLS ENFORCED at runtime).
var connStr = builder.Configuration["ConnectionStrings:Default"]
    ?? "Host=localhost;Port=5432;Database=laundry_ghar_db;Username=app_user;Password=app_user";

// Admin = postgres/superuser, injected for Development seeding only (bypasses RLS natively).
var adminConnStr = builder.Configuration["ConnectionStrings:Admin"]
    ?? "Host=localhost;Port=5432;Database=laundry_ghar_db;Username=postgres;Password=postgres";

// ── Shared dev PII key ──────────────────────────────────────────────────────────────────
// Problem (DEF-39-A): when Pii__EncryptionKey is absent each service auto-generates its
// own key in bin/Debug/net10.0/keys/dev-pii-key.b64 at startup.  That means Identity
// encrypts integration-settings secrets with key-A while Worker decrypts with key-B →
// CryptographicException at runtime, which was escaping WhatsAppSettings/SmsSettings.FromJson
// and causing the Worker to retry-loop outbox rows instead of reaching the logging fallback.
//
// Fix: AppHost loads (or generates once) a single 32-byte key in its own keys/ directory
// and injects Pii__EncryptionKey into ALL services.  The per-service auto-gen in
// ConfigurePiiCipher never triggers because the config key is always present.
//
// The key file is gitignored via **/keys/*.b64  (added to root .gitignore).
// Production: provide Pii__EncryptionKey via a secrets manager — the key file is never read.
var devPiiKeyBase64 = LoadOrGenerateDevPiiKey();

// ── 9 microservices — ports are FIXED (clients + appsettings hard-reference them) ─────

builder
    .AddProject<Projects.laundryghar_Identity>("identity")
    .WithHttpEndpoint(port: 5050, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr)
    .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

builder
    .AddProject<Projects.laundryghar_Catalog>("catalog")
    .WithHttpEndpoint(port: 5001, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr)
    .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

builder
    .AddProject<Projects.laundryghar_Orders>("orders")
    .WithHttpEndpoint(port: 5002, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr)
    .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

builder
    .AddProject<Projects.laundryghar_Warehouse>("warehouse")
    .WithHttpEndpoint(port: 5003, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr)
    .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

builder
    .AddProject<Projects.laundryghar_Logistics>("logistics")
    .WithHttpEndpoint(port: 5004, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr)
    .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

builder
    .AddProject<Projects.laundryghar_Commerce>("commerce")
    .WithHttpEndpoint(port: 5005, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr)
    .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

builder
    .AddProject<Projects.laundryghar_Finance>("finance")
    .WithHttpEndpoint(port: 5006, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr)
    .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

builder
    .AddProject<Projects.laundryghar_Engagement>("engagement")
    .WithHttpEndpoint(port: 5007, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr)
    .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

builder
    .AddProject<Projects.laundryghar_Analytics>("analytics")
    .WithHttpEndpoint(port: 5008, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr)
    .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

builder
    .AddProject<Projects.laundryghar_Mcp>("mcp")
    .WithHttpEndpoint(port: 5009, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

// ── API Gateway — single entry-point for all 4 clients at :8080 ─────────────────────
// ADDITIVE: all 9 per-service direct ports remain active.
// Downstream cluster addresses are injected via env vars so YARP uses the same
// port-fixed addresses as the other resources.  No service-discovery magic — consistent
// with the "static ports" convention used throughout this AppHost.
builder
    .AddProject<Projects.laundryghar_Gateway>("gateway")
    .WithHttpEndpoint(port: 8080, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64)
    // Inject each downstream cluster address.  Overrides appsettings.json defaults;
    // double-underscore maps to Gateway:Clusters:{name}:Destinations:primary:Address.
    .WithEnvironment("Gateway__Clusters__identity__Destinations__primary__Address",   "http://localhost:5050")
    .WithEnvironment("Gateway__Clusters__catalog__Destinations__primary__Address",    "http://localhost:5001")
    .WithEnvironment("Gateway__Clusters__orders__Destinations__primary__Address",     "http://localhost:5002")
    .WithEnvironment("Gateway__Clusters__warehouse__Destinations__primary__Address",  "http://localhost:5003")
    .WithEnvironment("Gateway__Clusters__logistics__Destinations__primary__Address",  "http://localhost:5004")
    .WithEnvironment("Gateway__Clusters__commerce__Destinations__primary__Address",   "http://localhost:5005")
    .WithEnvironment("Gateway__Clusters__finance__Destinations__primary__Address",    "http://localhost:5006")
    .WithEnvironment("Gateway__Clusters__engagement__Destinations__primary__Address", "http://localhost:5007")
    .WithEnvironment("Gateway__Clusters__analytics__Destinations__primary__Address",  "http://localhost:5008");

// ── Worker — no HTTP endpoint; drains notifications_outbox + outbox_events ──────────
// Generic host: reads DOTNET_ENVIRONMENT (ASPNETCORE_ENVIRONMENT is web-host only).
// Without it the Worker sees "Production" and the PII cipher fails closed at startup.
builder
    .AddProject<Projects.laundryghar_Worker>("worker")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr)
    .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

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
    // Store the key next to the AppHost project source (not in bin/), so it survives
    // `dotnet clean` and is the same path regardless of build configuration.
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
