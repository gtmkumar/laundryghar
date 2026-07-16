# Production environment variables

> **Consolidation note (service topology):** The 11 services have been consolidated
> into **3 deployable hosts** behind the gateway:
> - **core** (`core.WebApi`, port 5050) = Identity + Engagement + Mcp
> - **operations** (`operations.WebApi`, port 5002) = Catalog + Orders + Warehouse + Logistics
> - **commerce** (`commerce.WebApi`, port 5005) = Commerce + Finance + Analytics + Worker
>   (the Worker background jobs run as in-process hosted services inside this host)
>
> All env-var keys and config sections below (`Jwt__*`, `Worker__*`, `Storage__*`, etc.)
> are **unchanged** — only the host that reads them differs. Where the text below says
> "9 APIs + Worker" or names an old per-service project path (e.g.
> `laundryghar.Worker/appsettings.Development.json`), read it as the corresponding
> consolidated host. The per-service rows still indicate *which lane* requires the value
> (e.g. "Identity only" → core; "Warehouse, Logistics" → operations; "Worker" → commerce).

The base `appsettings.json` of every service is intentionally **secret-free**.
Development values live in `appsettings.Development.json` (loaded only when
`ASPNETCORE_ENVIRONMENT=Development`). In Production these files provide no
credentials, so the following MUST be injected via environment variables (or a
secrets manager / Key Vault). Services **fail closed** — they throw on startup if
a required value is missing.

## Required for every service (9 APIs + Worker)

**Every service (9 APIs + Worker):**

| Env var | Purpose | Notes |
|---|---|---|
| `ConnectionStrings__Default` | Runtime DB connection | MUST be a **non-superuser** role (e.g. `app_user`) so PostgreSQL RLS is enforced. Never `postgres`. |

**JWT auth is RS256 + JWKS** (no shared secret). Identity signs with a private key; the
other services verify by fetching Identity's public key from its JWKS endpoint.

| Env var | Set on | Purpose |
|---|---|---|
| `Jwt__PrivateKey` | **Identity only** | RSA private key **PEM** used to sign RS256 tokens. Inject from Key Vault / secret store. (`Jwt__PrivateKeyPath` to a mounted PEM file is the alternative.) Identity throws on startup if neither is set outside Development. |
| `Jwt__Authority` | **the 8 verifying services** | Base URL of Identity (e.g. `https://identity.internal`); its `/.well-known/openid-configuration` → JWKS supplies the RS256 public key. Required in all environments (services throw if unset). Use HTTPS in production (`RequireHttpsMetadata` is on outside Development). |
| `Jwt__RequireHttpsMetadata` | verifying services (optional) | Set `false` ONLY for private-network deployments (docker compose) where the JWKS fetch goes to `http://core:8080` on an internal network. Default: `true` outside Development. |
| `HealthChecks__Expose` | any service (optional) | Set `true` to map `/health` + `/alive` outside Development (needed for Docker HEALTHCHECK / k8s probes). Endpoints are unauthenticated — only enable when service ports are not internet-reachable. Set automatically by the Dockerfile. |

The public key is served at `GET {Identity}/.well-known/jwks.json`; rotating the private key
is picked up automatically by each service's cached JWKS (no redeploy of verifiers needed).

## Error tracking (Sentry — optional, recommended in Production)

Wired centrally in `laundryghar.ServiceDefaults` (`AddSentryIfConfigured`), so every host
(core, operations, commerce, gateway) picks it up. **Complete no-op when no DSN is set** —
nothing to configure for local dev.

| Env var | Purpose | Default |
|---|---|---|
| `SENTRY_DSN` (or `Sentry__Dsn`) | Enables Sentry when set | unset → disabled |
| `Sentry__Environment` | Sentry environment tag | `ASPNETCORE_ENVIRONMENT` |
| `Sentry__Release` | Release tag for regression tracking | assembly informational version |
| `Sentry__TracesSampleRate` | Performance-trace sampling (0–1) | `0.1` |

Capture path: the shared `ExceptionHandler` middleware logs every unhandled exception at
Error level; Sentry's logging integration turns those into events. Expected business
exceptions (validation / business-rule / auth / not-found / cancellation) are filtered out
before send so 4xx noise never reaches Sentry. `SendDefaultPii` is off — no user PII leaves
the platform.

## Do NOT set in Production

| Env var | Why |
|---|---|
| `ConnectionStrings__Admin` | Privileged (superuser) connection used **only** by Development data seeding, which is hard-disabled outside Development (seeders throw). Omit it in Production. |
| `Jwt__SigningKey` | Removed — the old shared HS256 secret no longer exists. |

## Example (Production)

```bash
# All services
export ConnectionStrings__Default="Host=db.internal;Port=5432;Database=laundry_ghar_db;Username=app_user;Password=<secret>"
export ASPNETCORE_ENVIRONMENT=Production

# Identity (issuer) — RSA private signing key
export Jwt__PrivateKey="$(cat /run/secrets/jwt-signing-key.pem)"

# The 8 verifying services — where to fetch Identity's JWKS
export Jwt__Authority="https://identity.internal"
```

## Secrets abstraction (`Secrets:Provider`)

All services use the cloud-agnostic `ISecretsProvider` abstraction wired into
`AddServiceDefaults()`. The active provider is chosen by a single config key.

### `Secrets:Provider` values

| Value | Behaviour | When to use |
|---|---|---|
| `env` **(default)** | No-op — all secrets come from environment variables or `appsettings.*.json`. Dev is unchanged. | Local dev; Aspire; any env where secrets are already injected as env vars. |
| `file` | Reads secrets from a mounted directory. Each file's **name** is the config key (using `__` as the hierarchy separator); its **content** is the value. | Docker / Kubernetes secret-volume mounts. |
| `azure-keyvault` | Not yet wired — throws `NotSupportedException` with instructions. | Azure: add `Azure.Extensions.AspNetCore.Configuration.Secrets` + `Azure.Identity`, then implement the seam in `SecretsProviderFactory`. |
| `aws-secretsmanager` | Not yet wired — throws `NotSupportedException` with instructions. | AWS: add `AWSSDK.SecretsManager`, implement `AwsSecretsManagerProvider`. |
| `vault` | Not yet wired — throws `NotSupportedException` with instructions. | HashiCorp Vault: add `VaultSharp`, implement `VaultSecretsProvider`. |

### File-mount convention (`Secrets:Provider = file`)

Set `Secrets:FilePath` to the absolute path of the mounted directory.

```bash
export Secrets__Provider=file
export Secrets__FilePath=/run/secrets
```

Each file under that directory is mapped to a config key:

| File name | Maps to config key |
|---|---|
| `ConnectionStrings__Default` | `ConnectionStrings:Default` |
| `Jwt__PrivateKey` | `Jwt:PrivateKey` |

File content is trimmed of leading/trailing whitespace. Missing or unreadable files
produce a warning on `stderr`; the service then fails closed at first access of the
missing key (e.g. `GetConnectionString("Default") ?? throw`).

### Precedence

The secrets layer sits above `appsettings.json` / `appsettings.{env}.json` in config
resolution but below environment variables. Env vars (including those injected by
Aspire's `WithEnvironment(...)`) always win, which means the `env` (default) provider
is a guaranteed no-op in Development.

### Wiring cloud providers

Each unsupported provider has a documented seam in
`laundryghar.ServiceDefaults/Secrets/SecretsProviderFactory.cs`. To activate one:

1. Add the required NuGet package (comments in the source list each package).
2. Replace the `throw new NotSupportedException(...)` arm with the real implementation.
3. No per-service changes are needed — all 10 services inherit the change via `AddServiceDefaults()`.

## Reverse proxy / load balancer (ForwardedHeaders)

All services use `UseForwardedHeadersIfEnabled()` from `laundryghar.ServiceDefaults`.
The middleware is **off by default** (Development uses the socket IP directly).

Enable it in Production/Staging by setting:

```bash
export ForwardedHeaders__Enabled=true
```

When enabled the middleware:
- Rewrites `HttpContext.Connection.RemoteIpAddress` from the proxy socket IP to the
  real client IP in `X-Forwarded-For`.
- Rewrites `Request.Scheme` to the value in `X-Forwarded-Proto` (so HTTPS redirects
  and JWT audience checks work correctly behind TLS-terminating proxies).

**Security requirement**: `KnownNetworks` and `KnownProxies` are cleared, meaning the
service trusts the entire `X-Forwarded-For` chain supplied by the first hop.  This is
correct **only when your edge proxy (ALB, nginx, Application Gateway) rewrites or
sanitises the header** before forwarding.  Do **not** enable this flag if the service
is directly internet-exposed without a trusted proxy — an attacker could forge the
header and bypass IP rate limiting.

## File storage (`Storage:Provider`)

All services that handle photo uploads (Warehouse, Logistics) use the `IFileStorageProvider`
abstraction wired into `AddServiceDefaults()`. The active provider is selected by a single key.

### `Storage:Provider` values

| Value | Behaviour | When to use |
|---|---|---|
| `local` **(default)** | Writes files to disk under `Storage:Local:RootPath`. | Local dev only. Not suitable for Production (no shared volume, no redundancy). |
| `s3` | Not yet wired — throws `NotSupportedException` with instructions. | AWS: add `AWSSDK.S3`, implement `S3FileStorageProvider`, register in `StorageExtensions`. |
| `azure-blob` | Not yet wired — throws `NotSupportedException` with instructions. | Azure: add `Azure.Storage.Blobs`, implement `AzureBlobStorageProvider`. |

### Configuration keys

| Env var | Service | Purpose |
|---|---|---|
| `Storage__Provider` | Warehouse, Logistics | `local` (dev default), `s3`, or `azure-blob`. |
| `Storage__Local__RootPath` | Warehouse, Logistics | Absolute path for local file storage. Default: `/tmp/laundryghar-uploads`. Only used when `Storage__Provider=local`. |

### Local Development default

```json
{
  "Storage": {
    "Provider": "local",
    "Local": {
      "RootPath": "/tmp/laundryghar-uploads"
    }
  }
}
```

Override in `appsettings.Development.json` per service if a shared path is needed.

### Storage key scheme

All keys follow the format:

```
{brandId:N}/{area}/{uuid:N}.{ext}
```

Examples:
- `a1b2c3d4.../inspections/7f8a9b0c....jpg` — garment inspection photo
- `a1b2c3d4.../proof/3e4f5a6b....jpg` — rider proof-of-delivery photo

Keys are **always server-generated** (`FileStorageKeyGenerator`). Client-supplied file names are never used. This prevents path-traversal and filename injection.

### Wiring cloud providers

Each unsupported provider has a documented seam in
`laundryghar.ServiceDefaults/Storage/FileStorageProviderFactory.cs`. To activate one:

1. Add the required NuGet package (comments in the source list each package).
2. Create the provider implementation: `IFileStorageProvider`.
3. Register it in `StorageExtensions.AddFileStorage()`.
4. No per-service changes needed — all services inherit via `AddServiceDefaults()`.

## DPDP Erasure Pipeline (Worker service — Task #14)

The Worker now runs two additional hosted services: `CustomerErasureService` (anonymizes
customers after DPDP grace period) and `RetentionSweepService` (hard-deletes expired
transient rows).  All settings live under the `Worker:` section and have safe defaults.
Override in Production via environment variables if the defaults need tuning.

### Erasure settings

| Env var | Default | Purpose |
|---|---|---|
| `Worker__ErasurePollIntervalSeconds` | `3600` | How often the erasure job wakes up (seconds). |
| `Worker__ErasureGracePeriodDays` | `30` | DPDP grace period before anonymization (days). Must be ≥ 30 in Production per DPDP rules. |
| `Worker__ErasureGracePeriodMinutesOverride` | `0` | **Development only.** When > 0, overrides the grace period to this many minutes for fast testing. MUST be 0 in Production. |
| `Worker__ErasureBatchSize` | `10` | Max requests processed per cycle. |

### Retention sweep settings

| Env var | Default | Purpose |
|---|---|---|
| `Worker__RetentionSweepIntervalSeconds` | `86400` | How often the sweep runs (seconds; default = daily). |
| `Worker__NotificationOutboxRetentionDays` | `180` | Delete terminal-status `notifications_outbox` rows older than this (days). |
| `Worker__OtpCodeRetentionDays` | `30` | Delete expired `otp_codes` rows whose `expires_at` is older than this (days). |
| `Worker__RefreshTokenRetentionDays` | `90` | Delete revoked/expired `refresh_tokens` rows older than this (days). |

### Grievance Officer config (`engagement_cms.mobile_app_config`)

The `grievance_officer` config key is seeded by `db/patches/seed_grievance_config.sql`
with placeholder values.  **Before going to Production**, update the `config_value` JSONB
for each brand row to contain real officer details:

```json
{
  "name":    "Full Name of Grievance Officer",
  "email":   "grievance@your-domain.in",
  "phone":   "+91XXXXXXXXXX",
  "address": "Company Name, Full Address, City, State, PIN, India",
  "hours":   "Mon-Fri 10:00-18:00 IST"
}
```

Update via SQL:
```sql
UPDATE engagement_cms.mobile_app_config
SET config_value = '{ ... real JSON ... }'::jsonb, updated_at = now()
WHERE config_key = 'grievance_officer';
```

Or inject as a migration in your deployment pipeline.  The customer app fetches this
key on the settings screen to render a "Contact Grievance Officer" entry as required
by DPDP Act Clause 13.

## Auto-Dispatch Worker (`AutoDispatch:*`)

The `laundryghar.Worker` hosts an auto-dispatch background service that assigns unattended
pickup requests to the best eligible rider. It is **disabled by default** — you must
explicitly opt in.

| Env var | Default | Purpose |
|---|---|---|
| `Worker__AutoDispatchEnabled` | `false` | Set to `true` to enable the service. Keep `false` in environments where dispatch is always manual. |
| `Worker__AutoDispatchPollSeconds` | `30` | How often (seconds) the job polls for unassigned pickups. 30 s is appropriate for production. |
| `Worker__AutoDispatchMinAgeMinutes` | `2` | A pickup request must be at least this many minutes old before auto-dispatch will claim it. Gives operators a window to manually assign fresh requests first. Set to `0` in dev/staging to assign immediately. |
| `Worker__AutoDispatchMaxPerCycle` | `10` | Maximum assignments created per poll cycle. Prevents runaway load when a large backlog accumulates. |

### Local development: enable auto-dispatch

In `laundryghar.Worker/appsettings.Development.json` set:
```json
"Worker": {
  "AutoDispatchEnabled": true,
  "AutoDispatchPollSeconds": 10,
  "AutoDispatchMinAgeMinutes": 0,
  "AutoDispatchMaxPerCycle": 5
}
```

Then restart the Worker (full AppHost restart required for `.cs` changes; appsettings
hot-reload applies without restart). Confirm in logs:
```
AutoDispatchService starting (pollSeconds=10, minAgeMinutes=0, maxPerCycle=5).
```

### Eligibility and ranking

For each unassigned pending pickup, the job selects riders in the same brand that are:
- `is_on_duty = true`
- `status = 'active'`
- `current_load < daily_delivery_capacity`

Candidates are ranked: **CurrentLoad ascending**, then **Haversine distance ascending**
(when both the pickup address and the rider's `last_known_location` have coordinates).
Riders without recent GPS pings rank last within the same load bucket.

The ranking logic lives in `RiderRanker.PickBest` (pure static, unit-tested in `laundryghar.Worker.Tests`).

Each assignment emits an `assignment.auto_assigned` outbox event (type, payload,
`kernel.outbox_events`) for downstream consumers (analytics, notifications).

## Subscription Billing + Dunning Worker (`Worker__Subscription*`)

The `laundryghar.Worker` hosts a daily subscription billing and dunning background
service (ADR-010). It is **disabled by default** — you must explicitly opt in.

| Env var | Default | Purpose |
|---|---|---|
| `Worker__SubscriptionBillingEnabled` | `false` | Set to `true` to enable daily subscription billing and dunning. Keep `false` until mandate webhook integration is live and tested end-to-end. |
| `Worker__SubscriptionBillingPollIntervalSeconds` | `86400` | How often (seconds) the job polls. Default 86400 = once per day. In staging, set to a small value (e.g. `300`) to test without waiting 24 hours. |
| `Worker__SubscriptionMaxDunningAttempts` | `3` | How many consecutive failed charge attempts before a customer subscription is moved to `suspended`. Must be >= 1. |
| `Worker__SubscriptionDunningBackoffMinutes` | `1440` | Base backoff per retry (minutes). Retry N is scheduled at `now + N * backoffMinutes`. Default = 1440 = 24 h, so retries fall on day 1, day 2, day 3 (then suspend). |

### Dunning state machine summary

```
active
  └─ charge fails (attempt 1)  → past_due   (retry at +1×backoff)
  └─ charge fails (attempt 2)  → past_due   (retry at +2×backoff)
  └─ charge fails (attempt N ≥ MaxDunningAttempts) → suspended
  └─ charge succeeds at any attempt             → active (dunning reset)
```

Outbox events emitted (type → `kernel.outbox_events`):
- `subscription.past_due` — first failure
- `subscription.suspended` — max attempts exhausted
- `subscription.renewed` — successful charge after one or more failures

### Local development: enable subscription billing

In `laundryghar.Worker/appsettings.Development.json` set:
```json
"Worker": {
  "SubscriptionBillingEnabled": true,
  "SubscriptionBillingPollIntervalSeconds": 60,
  "SubscriptionMaxDunningAttempts": 2,
  "SubscriptionDunningBackoffMinutes": 1
}
```

Confirm in logs:
```
SubscriptionBillingService starting (pollIntervalSeconds=60, maxDunning=2).
```

Note: the in-process charge stub always succeeds in the current Worker implementation.
Production billing requires the Commerce gateway seam to be wired through to the Worker
(tracked as a future cross-service task — see ADR-010 implementation notes).

## Still on the hardening backlog (tracked in orchestrator memory)

- DB password + `Jwt__PrivateKey` → a real secrets manager (Key Vault / SSM)
  via the `file` or `azure-keyvault` / `aws-secretsmanager` provider above
  (the RS256/JWKS migration is done; `ISecretsProvider` seams are ready).
- AutoMapper 13.0.1 CVE — remove (it is unused; inline projections everywhere).
