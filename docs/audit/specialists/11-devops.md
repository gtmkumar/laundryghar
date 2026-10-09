# 11 — DevOps, Deployment and Production Readiness Specialist

## Scope and method

**Inspected (read in full or in the cited ranges):**
- CI/CD: `.github/workflows/ci.yml`, `.github/workflows/release.yml`
- Containers: `backend/laundryghar/Dockerfile`, `backend/laundryghar/.dockerignore`, `admin-web/Dockerfile`, `admin-web/.dockerignore`, `admin-web/deploy/nginx.conf`, `deploy/docker-compose.yml`, `deploy/README.md`, `deploy/.env.example` (key names only)
- Hosting: `laundryghar.AppHost/AppHost.cs`, `laundryghar.ServiceDefaults/Extensions.cs`, `laundryghar.Gateway/{Program.cs,RateLimitPartitioning.cs,HealthServicesEndpoint.cs,ResilientForwarderHttpClientFactory.cs}`
- Config: every `appsettings*.json` under `backend/laundryghar/*` (6 hosts x 2), `backend/laundryghar/PRODUCTION_ENV.md`
- Host pipelines: `core.WebApi/Program.cs` (L195-300, L495-580), `commerce.WebApi/Program.cs` (L180-345), and the middleware lines of `operations.WebApi/Program.cs`
- Workers: `commerce.Infrastructure/Worker/Services/{OutboxEventRelayService,NotificationDispatcherService,SubscriptionBillingService,PartnerBookingDebitService,PartitionMaintenanceService}.cs`, `GatewaySubscriptionCharger.cs`, `Stubs/LoggingEventPublisher.cs`, `commerce.Infrastructure/Gateway/RazorpayPaymentGateway.cs` (L220-290)
- Storage: `operations.Infrastructure/Storage/*`, `operations.Infrastructure/DependencyInjection.cs`
- Data access: `laundryghar.SharedDataModel/DependencyInjection.cs`, `.../Interceptors/RlsConnectionInterceptor.cs` (L88-100), `core.Infrastructure/Services/BrandResolver.cs`, `core.Infrastructure/Auth/RsaJwtKeyProvider.cs`, `laundryghar.Utilities/Caching/OutputCaching.cs`
- DB ops: `db/tools/migrate.sh`, `db/tools/run_partman_maintenance.sh`, `db/tools/com.laundryghar.partman.plist`, `db/migrations/` (33 up/down pairs), `database_scripts/99_cross_cutting_schema_qualified.sql`, `db/migrations/0024_authz_abac_foundation.up.sql` (L140-150), `db/HANDOFF.md` (L165-185)
- Backup: `ops/backup/{README.md,backup.sh,restore.sh,verify-backup.sh,com.laundryghar.backup.plist}`
- Mobile: `customer-mobile/eas.json`, `rider-mobile/eas.json`, `customer-mobile/app.config.ts`, `rider-mobile/app.config.ts` (project-id / updates lines)
- Docs (claims only): `PRODUCTION_SPEC.md`, `HANDOFF.md` (ops grep + L793-805), `docs/PORTS.md`

**Commands run:** read-only `cat`/`sed`/`grep`/`find`/`git log`/`git merge-base`. GitHub MCP, read-only: `actions_list` (ci.yml and release.yml runs on `main`), `list_workflow_jobs` for run 36294076412, `get_job_logs` for the two failed mobile jobs. Local HEAD `a9fedd0` = `main` head `274b7af` + one docs-only commit, so the CI evidence applies to the code audited here.

**Could not verify:** no .NET SDK, Docker or PostgreSQL server in this container. I did not build or run images, compose, migrations, backup, restore or the workers. Runtime claims below come from reading code and config, and from the documented defaults of ASP.NET Core and YARP. Each finding is labelled with that limit. I made no code changes. The only file written is this report.

## Current-state summary

**Topology (as configured):** internet → (operator-supplied TLS proxy, not in repo) → `gateway` (YARP, :8080) → `core` / `operations` / `commerce` (internal :8080) → external PostgreSQL. `admin-web` (nginx SPA) is published on its own port. `deploy/docker-compose.yml:21-140` and `deploy/README.md:1-22` show this. Only the gateway and admin-web publish host ports. The services use a private bridge network. Containers run as the non-root `app` user and have a Docker HEALTHCHECK on `/alive` (`backend/laundryghar/Dockerfile:28-46`). This shape is reasonable for a **single-node, single-replica** deployment. Nothing in the repo goes beyond that. There is no Kubernetes or other orchestrator, no IaC, no replica configuration and no deploy job.

**Request path at the edge:** `UseForwardedHeadersIfEnabled` → `UseResponseCompression` → `UseSecurityHeaders` (HSTS, nosniff, DENY, Referrer-Policy outside Development) → `UseCors` (a static allow-list outside Development) → `UseRateLimiter` (partitioned by brand, else by IP) → health endpoints → YARP with per-cluster Polly circuit breaker, timeout and 100-request bulkhead. Sources: `laundryghar.Gateway/Program.cs:178-201, 213-258, 287-315` and `ResilientForwarderHttpClientFactory.cs`. The gateway does **not** validate JWTs. Each service validates RS256 against the core JWKS.

**CI/CD:** `ci.yml` builds and tests the backend (including Testcontainers integration tests on `postgres:16-alpine`), lints and builds admin-web, typechecks and tests both mobile apps, and checks that every migration has both an up and a down file. `release.yml` builds and pushes five images to GHCR on every push to `main`, tagged `latest` and with the SHA. It does **not** depend on CI. **All 8 CI runs on `main` concluded failure or cancelled.** In the latest run, 36294076412 (2026-09-27, sha 274b7af), the backend job 108549585458 and admin-web job 108549585404 **passed**. rider-mobile job 108549585491 failed at `npm ci` (ERESOLVE peer-dependency conflict). customer-mobile job 108549585512 failed at `tsc` (`app/_layout.tsx(11,8): TS2882 ... '../global.css'`). The Release workflow succeeded for the same commit (run 36294076432), so a red `main` still shipped `latest` images.

**Schema changes:** these are a manual operator step (`deploy/README.md:57-59`) using `db/tools/migrate.sh`. That script is transactional per file and keeps a checksummed `public.schema_migrations` table. No pipeline applies, verifies or rolls back migrations.

**Background work:** all 14 hosted workers run **in-process inside the commerce API host** (`commerce.WebApi/Program.cs:298-322`), with `BackgroundServiceExceptionBehavior.Ignore` (L196-197). Core runs `OAuthCleanupService` (`core.WebApi/Program.cs:190`). There is no leader election, no advisory lock and no `FOR UPDATE SKIP LOCKED` anywhere in the workers (grep for `skip locked|pg_try_advisory|pg_advisory|leader` found only an unrelated wallet `FOR UPDATE` in `CommerceDbContext.cs:120-123`). The `IEventPublisher` is the dev stub `LoggingEventPublisher` in every environment (`commerce.WebApi/Program.cs:290`). There is no message broker.

**Observability:** OpenTelemetry covers ASP.NET Core, HttpClient and runtime metrics and traces, but it exports **only** when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (`ServiceDefaults/Extensions.cs:153-160`). Compose does not set it. Sentry is optional (`SENTRY_DSN`), with `SendDefaultPii=false` (L68-117). There is no DB or EF instrumentation package (verified in the csproj package list) and no Prometheus endpoint. No log scope or activity tag carries `brand_id`: grep for `BeginScope|SetTag|AddTag|Baggage|Enrich|LogContext|Serilog` over the backend returned nothing relevant. The repo has no alerting configuration (deploy/, ops/, .github/ checked).

**Secrets:** these come from environment variables in a plain `.env` file next to the compose file (`deploy/docker-compose.yml:9-12, 21-27, 44`). Non-Development startup fails closed when the PII key is missing (`SharedDataModel/DependencyInjection.cs` docs + `ConfigurePiiCipher`) or the JWT signing key is missing (`RsaJwtKeyProvider.cs:30-53`). No secrets manager is wired up (see SA-OPS-009).

## Production-operations matrix

| Concern | Current state | Evidence | Gap | Risk | Recommended action | Priority |
|---|---|---|---|---|---|---|
| Build pipeline health | Backend and admin-web green; both mobile jobs red on every `main` run | CI runs 36294076412 (jobs 108549585491/…512), 32801423356, 30655059625, 29593179258, 29587337692, 29584182499, 29524708837 = failure; 36294068022 cancelled | Red `main` tolerated; no branch protection evidence | Regressions ship unnoticed | Fix mobile `npm ci`/TS2882; require CI green to merge | P1 |
| Release gating | `release.yml` pushes `latest` on every `main` push, independent of CI | `release.yml:7-10, 20-81` (no `needs`/`workflow_run`); release run 36294076432 success on a red-CI SHA | Images not gated on tests; mutable `latest` | Broken build deployed by `pull && up` | Trigger release from `workflow_run` on CI success, or add a test job as `needs`; deploy by SHA tag, not `latest` | P0 |
| Deploy automation | None. README says `docker compose pull && up -d` by hand | `deploy/README.md:62-70`; compose `image:` names are local (`docker-compose.yml:39,52,64,76,110`) | No deploy job or environments/approvals; documented pull path does not point at GHCR | Manual, error-prone, unrepeatable deploys | Set `image: ghcr.io/<owner>/laundryghar-*:${TAG}` in compose; add a deploy job with an environment approval | P1 |
| DB migrations in pipeline | Manual operator step; CI checks only that up/down pairs exist | `deploy/README.md:57-59`; `ci.yml:88-111`; `migrate.sh:138-170` | Never applied/rolled back in CI; no `verify` step at deploy; no lock | Schema/app skew; untested `.down.sql` | CI job: PG18 + extensions, run `build_from_scratch` → `migrate up` → `down all` → `up`; deploy step runs `migrate.sh up && verify` before rolling images | P1 |
| Secrets management | Plain env vars from `.env`; no Key Vault; claimed `ISecretsProvider` is absent | `docker-compose.yml:21-27,44`; `PRODUCTION_ENV.md:85-134`; grep `Secrets:Provider`/`ISecretsProvider` = 0 hits | No rotation, no secret store; docs contradict code | Secret sprawl, leaked `.env`; operators misled | Use Docker/K8s secrets with `Jwt__PrivateKeyPath` (already supported) and `AddKeyPerFile`; fix docs | P1 |
| Environment separation | `appsettings.json` secret-free; Dev JSONs hold localhost creds; CORS no localhost outside Dev; security headers off in Dev | `*/appsettings.Development.json`; `Gateway/Program.cs:191-193`; `Extensions.cs:211-215` | Dev JSONs baked into images (`.dockerignore` doesn't exclude them) | Low (only loaded in Development) | Exclude `**/appsettings.Development.json` in `.dockerignore` | P3 |
| Tenant-aware logs/metrics/traces | No `brand_id` in log scope or span tags; OTLP off in compose; no DB spans; no alerting | `Extensions.cs:119-170`; grep (no BeginScope/SetTag); compose has no `OTEL_*` | Cannot answer "which tenant is failing/slow"; no alerts | Blind multi-tenant operations | Middleware after tenant resolution: `logger.BeginScope({brand_id,user_id})` + `Activity.Current?.SetTag("tenant.brand_id")`; set OTLP endpoint; add Npgsql OTel; define SLO alerts | P1 |
| Audit events | DB audit trail (`identity_access.audit_logs`, interceptor + `IAuditWriter`, redaction list) + CQRS `AuditBehavior` log line | `Utilities/Auth/Audit/AuditContext.cs:10-40`; `CQRS/Behaviors/AuditBehavior.cs:25-43` | CQRS correlation id is a fresh GUID, unrelated to the W3C trace id | Harder cross-system correlation | Derive `CorrelationContext` from `Activity.Current.TraceId` | P3 |
| Backup / DR | Daily `pg_dump -Fc` + globals, integrity `--list`, optional S3/rclone; weekly scratch restore script | `ops/backup/backup.sh:54-82`; `verify-backup.sh:36-74`; schedulers = macOS launchd (`com.laundryghar.backup.plist:32`) or README cron text | No PITR/WAL (RPO 24 h); dumps unencrypted; verify only counts tables; no evidence it was ever run in prod | Data loss up to 24 h; unverified recoverability | Managed PG with PITR as primary; encrypt dumps (`age`/SSE-KMS); schedule verify in CI/cron with row-count + RLS checks | P1 |
| Tenant-level restore | Not implemented. Per-brand **export** exists (`kernel.export_brand`), used on cancellation | `SharedDataModel/Persistence/BrandExportService.cs:8-27`; `core.WebApi/Endpoints/Identity/AdminCancellation.cs:66` | Restoring one tenant means restoring the whole DB elsewhere and hand-copying rows | Long, risky single-tenant recovery | Document a runbook: restore into side DB (`restore.sh` default) + brand-scoped copy script driven by the export function's table list | P2 |
| Partition maintenance | partman `run_maintenance_proc` scheduled only by a launchd plist on a developer Mac; worker covers only `rider_location_pings` | `db/tools/com.laundryghar.partman.plist:30`; `PartitionMaintenanceService.cs`; `db/HANDOFF.md:175,181` | `orders`, `audit_logs`, `process_logs`, `notifications_log`, `authz.decision_log` have no prod scheduler | Inserts land in default partitions or fail once runway ends (dev DB runway noted as 2026-12-01) | Call `partman.run_maintenance_proc()` from `PartitionMaintenanceService` (single-runner lock), or pg_cron / partman BGW | P0 |
| Rate limits & quotas | Gateway: per-brand 3000/min else per-IP 300/min, in-memory; core `auth` 10/min per `RemoteIpAddress` | `Gateway/Program.cs:213-258`; `RateLimitPartitioning.cs:24-86`; `core.WebApi/Program.cs:198-216` | Brand key is unverified and attacker-chosen; XFF spoofable; core sees only the gateway IP; limits not plan-aware | Bypass + cross-tenant DoS; platform-wide login lockout | See SA-OPS-001/002 | P0 |
| Noisy neighbour / isolation | One shared DB pool; per-cluster bulkhead of 100 shared by all tenants; no container CPU/mem limits | `ResilientForwarderHttpClientFactory.cs` (`AddConcurrencyLimiter(100,0)`); compose has no `deploy.resources` | One tenant can saturate a cluster's 100 slots | Platform-wide 5xx caused by one tenant | Per-brand concurrency partition at gateway; plan-tier limits; container limits | P2 |
| Background jobs & scaling | 14 workers in commerce API host; no leader election; dispatcher/relay claim via read-then-update | `commerce.WebApi/Program.cs:298-322`; `OutboxEventRelayService.cs:101-120`; `NotificationDispatcherService.cs:115-131` | Scaling commerce double-runs jobs; stuck `sending`/`publishing` rows never reclaimed | Duplicate WhatsApp/SMS; silent message loss after crash | `UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING`; lease column + reaper; move workers to a separate single-replica `worker` service | P1 |
| File storage | `local` provider only; default root `/tmp/laundryghar-uploads` inside the container; S3/Blob throw | `operations.Infrastructure/Storage/FileStorageProviderFactory.cs:16-37`; `LocalStorageOptions.cs:9-15`; compose has no `Storage__*` or volume | Uploads (inspection/proof photos, rider KYC docs, catalog images) lost on container recreate; not backed up | Data loss on every deploy; blocks scaling operations | Implement S3/Blob provider (seam exists) before go-live; interim: named volume + include in backup | P0 |
| Caching | IMemoryCache stores with 15-30 s TTL; in-process OutputCache with tag eviction | `TokenVersionStore.cs:16`, `BrandStatusStore.cs:15`, `PolicyCache.cs:36`; `OutputCaching.cs:27-29` | Eviction does not fan out across replicas (acknowledged in code) | Stale tenant content up to TTL when scaled | Redis `IOutputCacheStore` before running >1 replica | P2 |
| DB connection pooling | Npgsql default pool (max 100/process), `EnableRetryOnFailure(3)`, session-level RLS GUCs reset by `DISCARD ALL` | `SharedDataModel/DependencyInjection.cs:86-91`; `RlsConnectionInterceptor.cs:92-100` | No PgBouncer; session GUCs make transaction-mode PgBouncer unsafe; no `Maximum Pool Size` guidance | Connection exhaustion as replicas grow | Set explicit pool sizes per host; if PgBouncer is adopted, switch to `set_config(...,true)` inside an explicit transaction or session pooling | P2 |
| Domain routing / custom-domain SSL | Brand domains stored + DNS/TLS verified; brand resolved from `Request.Host` | `BrandResolver.cs:67-72,103-118`; `BrandDomainSettings.cs:15`; `Gateway/Program.cs:315` | Behind YARP the upstream Host is the destination address (YARP default); ForwardedHeaders ignores `X-Forwarded-Host`; no cert issuance for custom domains; CORS static | Custom domains resolve to the default brand / are blocked by CORS | Enable `RequestHeaderOriginalHost` or add `XForwardedHost` with known-proxy list; on-demand TLS edge (Caddy/cert-manager); dynamic CORS from verified domains | P2 |
| Static assets / CDN | nginx serves hashed `/assets` immutable, `index.html` no-cache, gzip | `admin-web/deploy/nginx.conf:8-28` | No CDN; no CSP/HSTS on admin-web; pos-web has no image at all | Minor | Add CSP; add pos-web Dockerfile + CI + release | P2 |
| Release strategy / back-compat | `/api/v1` paths; `MobileAppConfig.IsForceUpdate`; EAS channels dev/preview/prod | `MobileAppConfig.cs:20`; `eas.json:6-47` | EAS project IDs are placeholders (OTA 404); single bundle id per app (no per-tenant store builds); no EAS CI | No OTA hotfixes; white-label mobile not deliverable | Run `eas project:init`; add EAS build/update workflow; decide white-label build matrix | P2 |

## Findings

### SA-OPS-001 — Core's `auth` rate limiter collapses every user onto the gateway's IP
- Category: Availability / rate limiting
- Severity: High
- Status: Partially Verified (code and config read end to end; not executed)
- Evidence: `backend/laundryghar/core.WebApi/Program.cs:198-216`: the `auth` policy partitions on `httpContext.Connection.RemoteIpAddress` with `PermitLimit = 10`/60 s (default). L285-296: `oauth_register` partitions the same way, 3 per hour. L505-515: `UseForwardedHeadersIfEnabled()` runs before `UseRateLimiter()`. `deploy/docker-compose.yml:25`: *"ForwardedHeaders stays OFF on the services — the gateway is the trusted edge"*, and `ForwardedHeaders__Enabled` is not set for `core` (L35-46).
- Observed behaviour: in the compose topology every request reaches core from the gateway container. `RemoteIpAddress` is therefore the gateway's bridge IP for **all** callers across all tenants, and every login, OTP, signup, partner-auth and OAuth call shares one 10/min bucket (3/hour for OAuth client registration).
- Reproduction / verification method: deploy the compose stack and send 11 OTP requests per minute from 11 different clients through the gateway. The 11th should get 429. (Not run here, no Docker.)
- Impact: platform-wide authentication outage at very low traffic, about 10 logins per minute across the whole SaaS.
- Recommended remediation (smallest safe change): set `ForwardedHeaders__Enabled=true` on the services and, in `UseForwardedHeadersIfEnabled`, populate `KnownIPNetworks` with the compose network instead of clearing it. Alternatively, partition `auth` on the rightmost XFF hop appended by the gateway.
- Regression tests required: integration test with two distinct XFF clients through a YARP hop, asserting that the buckets are independent.
- Dependencies / priority: P0. Related area: SEC.

### SA-OPS-002 — Gateway rate limit is bypassable and lets one tenant's budget be burned by anyone
- Category: Rate limiting / noisy neighbour
- Severity: High
- Status: Verified (code read; not executed)
- Evidence: `laundryghar.Gateway/RateLimitPartitioning.cs:24-38`: any syntactically valid GUID in `X-Brand-Id` (no auth needed) selects partition `brand:<guid>` with the 10x brand limit. L80-86: with no brand, the key is the **leftmost** `X-Forwarded-For` value, read straight from the request even when `ForwardedHeaders` is disabled. `Gateway/Program.cs:216-218, 245-257`.
- Observed behaviour: (a) a client that rotates a random `X-Brand-Id` or a fake leftmost XFF on each request gets a fresh bucket every time, so the limit is effectively unlimited. (b) A client that sends a victim's brand GUID (brand ids appear in public storefront calls) spends that tenant's 3000/min budget, and that tenant's real users get 429. The code comment at L10-16 says the worst outcome is "being counted against someone else's budget". That outcome is exactly a cross-tenant DoS.
- Reproduction / verification method: unit test `RateLimitPartitioning.Resolve` with random GUID headers and observe distinct keys. Load test through the gateway.
- Impact: no effective global or abuse rate limiting, plus cross-tenant DoS.
- Recommended remediation: only key on brand when a bearer token is present and parses, and ignore bare `X-Brand-Id` for partitioning. Use `Connection.RemoteIpAddress` after ForwardedHeaders with a known proxy instead of reading raw XFF. Keep an outer per-IP cap even for brand-keyed traffic.
- Regression tests required: unit tests for spoofed header and XFF cases.
- Dependencies / priority: P0. Related area: SEC.

### SA-OPS-003 — Uploaded files live in the container's `/tmp` and are lost on every redeploy
- Category: Data durability / scaling
- Severity: High
- Status: Verified (config read)
- Evidence: `operations.Infrastructure/Storage/FileStorageProviderFactory.cs:16-37`: only `local` works, and `s3`/`azure-blob` throw `NotSupportedException`. `operations.Infrastructure/DependencyInjection.cs:32-47`. `LocalStorageOptions.cs:9-15`: default root is `/tmp/laundryghar-uploads`. `deploy/docker-compose.yml:48-58`: the `operations` service has no `Storage__*` env and no volume. Callers include inspection photos, rider proof photos, **rider KYC documents** and catalog item images (`operations.Application/.../UploadRiderDocument.cs`, `UploadProofPhoto.cs`, `UploadInspectionPhoto.cs`, `ItemImageCommands.cs`). `ops/backup/backup.sh` backs up only the DB.
- Observed behaviour: files are written to the ephemeral container filesystem. `docker compose pull && up -d` recreates the container and deletes every file, while DB rows still reference the storage keys.
- Reproduction / verification method: compose up, upload a photo, recreate `operations`, GET the photo, expect 404. (Not run.)
- Impact: loss of compliance-relevant evidence (KYC, proof of delivery, damage inspection). The operations host also cannot run more than one replica.
- Recommended remediation: implement the S3/Blob provider at the existing seam before go-live. Interim step: mount a named volume at an explicit `Storage__Local__RootPath` and include it in backups.
- Regression tests required: provider contract tests (save/read/delete, brand-prefixed key).
- Dependencies / priority: P0.
- Prior-doc cross-ref: `PRODUCTION_ENV.md:160-217` says `local` is "Not suitable for Production", but compose ships it anyway.

### SA-OPS-004 — pg_partman maintenance for orders/audit/process/notification/decision logs is scheduled only on a developer Mac
- Category: Database operations
- Severity: High
- Status: Verified (config read). Runway date is taken from the doc, not measured.
- Evidence: `database_scripts/99_cross_cutting_schema_qualified.sql` (create_parent for `identity_access.audit_logs`, `order_lifecycle.orders`, `order_lifecycle.process_logs` monthly with premake 6; `engagement_cms.notifications_log` monthly with premake 3; `logistics.rider_location_pings` daily). `db/migrations/0024_authz_abac_foundation.up.sql:144-149` (`authz.decision_log` monthly). The only scheduler is `db/tools/com.laundryghar.partman.plist:30`, which hard-codes `/Users/gtmkumar/...`. `PartitionMaintenanceService.cs` only calls `logistics.ensure_rider_ping_partitions`. A repo-wide grep for `run_maintenance|pg_cron` finds only `build_from_scratch.sh:104` (one-off) and the script itself. `db/HANDOFF.md:175,181` gives runway to 2026-12-01 and says the scheduler is not enabled.
- Observed behaviour: no production component premakes monthly partitions.
- Impact: once premade partitions run out, new `orders` and `audit_logs` rows go to the default partition (later partition creation conflicts) or inserts fail. That is a revenue-path outage on a timer.
- Recommended remediation: extend `PartitionMaintenanceService` to call a SECURITY DEFINER wrapper around `partman.run_maintenance_proc()` under a single-runner advisory lock, or enable pg_cron or the partman background worker on the managed DB. Add an alert on default-partition row count > 0.
- Regression tests required: integration test asserting future partitions exist N months ahead after a maintenance run.
- Dependencies / priority: P0. Related area: DB.

### SA-OPS-005 — Background jobs assume a single commerce instance; two notification lanes can double-send or wedge
- Category: Scaling / reliability
- Severity: High
- Status: Partially Verified (code read; concurrency not reproduced)
- Evidence: `commerce.WebApi/Program.cs:298-322`: all workers are registered in the API host. `OutboxEventRelayService.cs:101-120` and `NotificationDispatcherService.cs:115-131` claim rows by `SELECT ... WHERE status IN (pending, failed)` then `SaveChanges(status = publishing/sending)` in a READ COMMITTED transaction, with no row lock and no concurrency token. A grep for `IsConcurrencyToken|IsRowVersion|xmin` over the backend found 0 hits, so EF's UPDATE is keyed by id only. The comments ("prevent concurrent workers", "guard against concurrent workers") are not borne out by the SQL. No code path resets `sending` or `publishing` (grep).
- Observed behaviour: with two commerce replicas, both can claim the same row and both send. A crash between claim and outcome leaves the row stuck in `sending`/`publishing` forever, so the message is never delivered.
- Mitigated paths (positive): `PartnerBookingDebitService` is safe because of its idempotent inbox plus unique ledger key (`PartnerBookingDebitService.cs:13-48`). Subscription invoices are protected by `UNIQUE (customer_subscription_id, billing_period_start)` (`docs/SCHEMA_FULL.sql:3730`). Charges carry `Razorpay-Idempotency` (`RazorpayPaymentGateway.cs:241-243`) and a unique `idempotency_key` on attempts (`SCHEMA_FULL.sql:3763`).
- Impact: duplicate customer WhatsApp/SMS/push messages (cost and spam) and silent loss after a crash. Scaling the commerce API for load also multiplies job runners.
- Recommended remediation: claim with `UPDATE ... SET status='sending', locked_until=now()+interval 'N min' WHERE id IN (SELECT id ... FOR UPDATE SKIP LOCKED LIMIT n) RETURNING *`, and reclaim rows whose lease has expired. Move workers into a separate `worker` compose service pinned to one replica, using the same image with a flag.
- Regression tests required: Testcontainers test running two dispatchers concurrently and asserting each row is sent once, plus a test that a stale lease is reclaimed.
- Dependencies / priority: P1 (P0 before any multi-replica deployment). Related area: DB.

### SA-OPS-006 — Release images ship from a red `main`; no deploy, migration or approval stage
- Category: CI/CD
- Severity: High
- Status: Verified (workflow files plus GitHub run history)
- Evidence: `.github/workflows/release.yml:7-10, 20-81`: triggers on push to `main` and has no dependency on CI. CI runs on `main` (newest first): 36294076412 failure, 36294068022 cancelled, 32801423356, 30655059625, 29593179258, 29587337692, 29584182499 and 29524708837 all failure. Release run 36294076432 (same SHA 274b7af) succeeded, as did 36294068000, 32801423294, 30655059355 and 29593179194. Latest CI failures: rider-mobile job 108549585491 (`npm ci` ERESOLVE) and customer-mobile job 108549585512 (TS2882 on `../global.css`). The backend (108549585458) and admin-web (108549585404) jobs passed.
- Observed behaviour: `latest` is overwritten on every push regardless of test results. There is no promotion between environments, no deploy job and no rollback procedure beyond re-tagging by hand.
- Impact: untested artifacts reach production hosts through the documented `pull && up` flow. CI has been red since it was created, which means the signal is ignored.
- Recommended remediation: run release on `workflow_run: CI, conclusion success` (or merge the jobs with `needs:`). Stop using `latest` for deploys and pin `${TAG}` to the SHA. Fix the two mobile failures. Enable branch protection that requires CI.
- Regression tests required: none (pipeline config).
- Dependencies / priority: P0.

### SA-OPS-007 — Migrations are a manual step; CI never applies or rolls them back
- Category: Database change management
- Severity: Medium
- Status: Verified
- Evidence: `ci.yml:88-111` (pairing and duplicate-number lint only). `deploy/README.md:57-59` (operator runs `migrate.sh up`). `db/tools/migrate.sh:138-170`: up/down in one transaction per file with a checksum. `no-transaction` files are not atomic (L83-93). There is no `pg_advisory_lock` around runs. Integration tests use `postgres:16-alpine` (`operations.IntegrationTests/*.cs`, e.g. `Phase1SqlMigrationTests.cs:24`), while production compose and the backup verifier use `postgres:18` (`docker-compose.yml:119`, `verify-backup.sh:23`).
- Observed behaviour: `.down.sql` files are never executed by automation. The deploy order (migrate, then roll images) is left to a human. Nothing checks that EF and the schema agree at deploy time.
- Impact: untested rollbacks, version skew between the test and production engines, and concurrent operator runs racing each other.
- Recommended remediation: add a CI job on PG18 with partman and postgis that runs `build_from_scratch.sh` → `migrate.sh up` → `migrate.sh down all` → `migrate.sh up` → `verify`. Wrap `cmd_up`/`cmd_down` in `pg_advisory_lock`. Add an expand/contract convention for backward-compatible deploys.
- Regression tests required: the CI job itself.
- Dependencies / priority: P1. Related area: DB.

### SA-OPS-008 — Logs, traces and metrics are not tenant-aware, and production exports nothing
- Category: Observability
- Severity: Medium
- Status: Verified (code and config read)
- Evidence: `laundryghar.ServiceDefaults/Extensions.cs:119-170`: OTel registers instrumentation but exports only if `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and `deploy/docker-compose.yml` sets no `OTEL_*`. No EF Core or Npgsql OTel instrumentation (csproj package list). A grep for `BeginScope|SetTag|AddTag|Baggage|LogContext|Serilog` finds no tenant enrichment. `laundryghar.Utilities/CQRS/Context/CorrelationContext.cs:9-19` generates a random GUID unrelated to the trace id. No alerting configuration anywhere in `deploy/`, `ops/` or `.github/`.
- Observed behaviour: in production the only signals are stdout logs and, if a DSN is set, Sentry errors. Neither can be filtered by brand.
- Impact: no per-tenant SLOs, no way to isolate a noisy or failing tenant, and no paging on outages.
- Recommended remediation: add one middleware after `TenantResolutionMiddleware` that opens a log scope `{brand_id, user_id}` and tags `Activity.Current` with `tenant.brand_id` (also on worker scopes). Set the OTLP endpoint in compose and add Npgsql OTel. Define alerts for 5xx rate, 429 rate, outbox/notification dead-letter counts, partition runway and backup age.
- Regression tests required: unit test that the middleware sets the scope and tag.
- Dependencies / priority: P1.
- Prior-doc cross-ref: `PRODUCTION_SPEC.md:104` claims "Serilog→Elastic, OpenTelemetry→Prometheus/Jaeger". Serilog is not referenced by any csproj.

### SA-OPS-009 — Secrets-provider abstraction is documented as done but absent from code
- Category: Secrets management / doc contradiction
- Severity: Medium
- Status: Verified
- Evidence: `backend/laundryghar/PRODUCTION_ENV.md:85-134` and `HANDOFF.md:797-803` describe `ISecretsProvider`, `FileSecretsProvider`, `Secrets:Provider=file` and `laundryghar.ServiceDefaults/Secrets/SecretsProviderFactory.cs`. `ls laundryghar.ServiceDefaults` shows only `Extensions.cs` and `ExternalDependencyResilience.cs`. A grep for `ISecretsProvider|Secrets:Provider|KeyPerFile` returns 0 hits in .cs/.csproj. `AddServiceDefaults` (Extensions.cs:26-52) wires none of this. Secrets reach containers as plain env vars from `deploy/.env` (`docker-compose.yml:21-27,44`). Secret-looking key names: `DB_CONNECTION_STRING`, `PII_ENCRYPTION_KEY`, `JWT_PRIVATE_KEY`, `POSTGRES_PASSWORD`, `SENTRY_DSN`, plus app keys `Notifications:WhatsApp:AccessToken`, `Notifications:Sms:AuthKey`, `Notifications:Push:AccessToken` (`commerce.WebApi/Program.cs:211-245`) and Razorpay `KeyId`/`KeySecret` (`RazorpayPaymentGateway.cs:285-290`). Committed client config: `customer-mobile/google-services.json` and `GoogleService-Info.plist` (Firebase client identifiers, usually public). No server secret values were found committed in the files read.
- Impact: operators following the docs will set `Secrets__Provider=file` and get nothing. There is no rotation path or central store, and secrets are visible in `docker inspect`.
- Recommended remediation: correct the docs. Use the already-supported `Jwt__PrivateKeyPath` with Docker secrets, and add `builder.Configuration.AddKeyPerFile("/run/secrets", optional:true)` in ServiceDefaults (one line, framework-provided).
- Regression tests required: config-binding test for key-per-file.
- Dependencies / priority: P1. Related area: SEC.

### SA-OPS-010 — Backup/DR is daily logical dumps only, with no PITR, no encryption and no proven schedule
- Category: Backup / DR
- Severity: Medium
- Status: Partially Verified (scripts read; never executed here, no evidence they run anywhere)
- Evidence: `ops/backup/backup.sh:54-82` (`pg_dump -Fc` + `pg_dumpall --globals-only`, `pg_restore --list` integrity check, plaintext upload to S3/rclone, 14-day local prune). `ops/backup/com.laundryghar.backup.plist:32` (macOS path), `README.md:24-29` (cron as text only). `verify-backup.sh:65-73` (asserts only `tables > 0`). `restore.sh:28-78` (safe by default: new DB, typed confirmation). Per-tenant: only the export function (`BrandExportService.cs:8-27`) and no restore path.
- Impact: RPO is 24 h and RTO is a full logical restore. Dumps (including role password hashes in globals) are unencrypted outside the DB. Single-tenant recovery requires a full side restore plus manual copying.
- Recommended remediation: use managed PG PITR as the primary mechanism (README L31-33 already suggests it). Encrypt archives. Schedule `verify-backup.sh` with row-count and RLS-policy checks. Write a brand-scoped restore runbook.
- Regression tests required: scheduled verify job with alerting on failure.
- Dependencies / priority: P1.

### SA-OPS-011 — Health checks never check the database
- Category: Operability
- Severity: Low
- Status: Verified
- Evidence: `ServiceDefaults/Extensions.cs:172-203`: only the `self` check exists, so `/health` and `/alive` are equivalent. `Gateway/HealthServicesEndpoint.cs:52-101`: `/health/services` probes `/health` (comments say `/health/ready`, which is not mapped), is unauthenticated and is published on the public gateway.
- Impact: a DB outage still reports healthy, and service topology is disclosed publicly.
- Recommended remediation: add an Npgsql readiness check tagged `ready` on `/health`. Keep `/alive` for liveness only. Restrict `/health/services` to internal access.
- Dependencies / priority: P3.

### SA-OPS-012 — Compose cannot pull the CI-built images, and pos-web has no deploy path
- Category: Deployment
- Severity: Medium
- Status: Verified (config read)
- Evidence: `deploy/docker-compose.yml:39,52,64,76,110` give `image: laundryghar-core` etc. (unqualified, so Docker Hub). `deploy/README.md:62-70` says to `docker compose pull` the GHCR images. `release.yml:67` pushes to `ghcr.io/<owner>/laundryghar-*`. `pos-web` has no Dockerfile, CI job or release matrix entry (`find -name Dockerfile*` shows only backend and admin-web). `admin-web` VITE URLs are baked at build time (`admin-web/Dockerfile:24-31`, `release.yml:46-52`), so it is one image per environment.
- Impact: the documented deploy flow either fails or pulls the wrong images. The POS client cannot be deployed from the repo.
- Recommended remediation: qualify the images as `ghcr.io/${OWNER}/laundryghar-core:${TAG:-latest}`. Add pos-web to CI and release. Consider runtime config injection (`/config.js`) for SPAs.
- Dependencies / priority: P1.

### SA-OPS-013 — Custom-domain brand resolution does not survive the gateway hop, and has no TLS automation
- Category: Domain routing / SSL
- Severity: Medium
- Status: Suspected (based on YARP's documented default `RequestHeaderOriginalHost=false`; not executed)
- Evidence: `core.Infrastructure/Services/BrandResolver.cs:67-72` resolves the brand from `context.Request.Host.Host`. `Gateway/Program.cs:46-55, 315` sets no `RequestHeaderOriginalHost` transform. `ServiceDefaults/Extensions.cs:273-276` only processes `XForwardedFor | XForwardedProto`, not `XForwardedHost`. `BrandDomainSettings.cs:15` uses CNAME target `edge.laundryghar.com`, but no edge or certificate component exists in `deploy/`. Gateway CORS is a static list (`Program.cs:185-201`; compose sets one origin, L85).
- Observed behaviour (expected): behind the gateway core sees `Host: core:8080`, so verified custom domains fall through to `X-Brand-Id`/`brandCode`/the default brand. Browsers on a tenant's domain are refused by CORS.
- Impact: the white-label custom-domain feature does not work end to end in the shipped topology.
- Recommended remediation: add `RequestHeaderOriginalHost=true` on the routes, or enable `XForwardedHost` with a known-proxy list. Front with an on-demand-TLS edge (Caddy `on_demand_tls` with an `ask` endpoint backed by `brand_domains`, or cert-manager). Build the CORS origins from verified domains.
- Regression tests required: gateway integration test asserting the original Host reaches core. Domain → brand e2e.
- Dependencies / priority: P2. Related area: TEN.

### SA-OPS-014 — Noisy-neighbour controls are global, not per tenant or plan
- Category: Resource isolation
- Severity: Medium
- Status: Verified (code read)
- Evidence: `ResilientForwarderHttpClientFactory.cs`: `AddConcurrencyLimiter(permitLimit: 100, queueLimit: 0)` and `MaxConnectionsPerServer = 100` per **cluster**, shared by all tenants. `Gateway/Program.cs:218`: one `BrandPermitLimit` for every brand, regardless of subscription tier. The compose file has no CPU or memory limits. There is one shared Npgsql pool per host (`SharedDataModel/DependencyInjection.cs:86-91`). Core's `api_key` policy does read per-key limits (`core.WebApi/Program.cs:227-283`), which is a positive.
- Impact: one heavy tenant can exhaust a cluster's in-flight slots or the DB pool for everyone.
- Recommended remediation: add a per-brand concurrency partition at the gateway (verified-token brand only, see SA-OPS-002) and map limits from plan entitlements. Add container resource limits.
- Dependencies / priority: P2.

### SA-OPS-015 — Horizontal-scaling blockers (DB-Q8 infrastructure view)
- Category: Scalability
- Severity: Medium
- Status: Verified (code read)
- Evidence: in-process OutputCache with eviction that does not fan out (`OutputCaching.cs:27-29`, acknowledged in the code). IMemoryCache with short TTLs for token version (15 s), brand status (30 s), ABAC policy (15 s) and feature catalog (5 min) (`TokenVersionStore.cs:16`, `BrandStatusStore.cs:15`, `PolicyCache.cs:36`, `FeatureCatalog.cs:21`). In-memory rate limiters per instance (SA-OPS-001/002). Local file storage (SA-OPS-003). In-process workers (SA-OPS-005). Pooling: Npgsql default (max 100 per connection string per process), `EnableRetryOnFailure(3)`, no PgBouncer. RLS GUCs are set **session-level** on every `ConnectionOpened` (`RlsConnectionInterceptor.cs:92-100`) and cleared by Npgsql's `DISCARD ALL` on return. That is safe with Npgsql's in-process pool but **unsafe with PgBouncer transaction pooling** (GUCs would leak between clients).
- Impact: replicas × 100 can exceed the managed PG `max_connections`. The obvious mitigation, transaction-mode PgBouncer, would break tenant isolation unless the GUC strategy changes. Revocation and suspension propagate per instance within TTL bounds, which is acceptable.
- Recommended remediation: set an explicit `Maximum Pool Size` per host and document the connection budget. If PgBouncer is adopted, use session pooling, or move to `set_config(...,true)` inside explicit transactions. Use Redis for OutputCache and rate limits before running more than one replica.
- Dependencies / priority: P2. Related area: DB.

### SA-OPS-016 — Mobile release pipeline is not operational (OTA, CI, white-label)
- Category: Release strategy
- Severity: Medium
- Status: Verified (config read plus CI logs)
- Evidence: `customer-mobile/app.config.ts:4-6` (`EAS_PROJECT_ID = 'laundryghar-customer'`, with a TODO saying OTA returns 404) and `rider-mobile/app.config.ts:6` (same). `eas.json:6-47` defines dev/preview/prod channels but `submit.production` credentials are empty (L48-59). No EAS workflow exists in `.github/workflows`. The bundle id is fixed (`app.config.ts:19,27`) and the brand is chosen by the build-time `DEFAULT_BRAND_CODE`. Mobile CI jobs 108549585491/108549585512 fail. A positive: `MobileAppConfig.IsForceUpdate` exists server-side (`SharedDataModel/Entities/EngagementCms/MobileAppConfig.cs:20`). I did not trace client consumption.
- Impact: no OTA hotfix channel, no reproducible store builds, and per-tenant branded store apps would need a build matrix that does not exist.
- Recommended remediation: run `eas project:init` and commit the real UUIDs. Add `eas build`/`eas update` workflows gated on mobile CI. Decide between a single multi-brand app and a per-tenant build matrix.
- Dependencies / priority: P2. Related area: FE/MOB.

### SA-OPS-017 — Container and supply-chain hygiene
- Category: Hardening
- Severity: Low
- Status: Verified
- Evidence: base images use floating tags (`Dockerfile:15,25` `sdk:10.0`/`aspnet:10.0`; `admin-web/Dockerfile:16,33` `node:22-alpine`/`nginx:alpine`; compose `postgres:18`). Actions are pinned to major versions, not SHAs (`ci.yml`, `release.yml`). There is no image scan or SBOM. `backend/laundryghar/.dockerignore:1-6` does not exclude `appsettings.Development.json`, which holds local `app_user`/`postgres` credentials and OTP test codes, so those files are copied into images (not loaded in Production). `admin-web/deploy/nginx.conf:21-24` has no CSP or HSTS.
- Recommended remediation: pin digests and SHAs, add Trivy/Grype to release, exclude Dev JSONs, add a CSP.
- Dependencies / priority: P3.

### SA-OPS-018 — Spec claims infrastructure that does not exist (broker, Redis, Hangfire, Serilog, S3)
- Category: Documentation accuracy
- Severity: Informational
- Status: Verified
- Evidence: `PRODUCTION_SPEC.md:104, 114, 118` lists Serilog, Hangfire, MassTransit, Redis, RabbitMQ and S3/Azure Blob. The csproj package list has none of them. `IEventPublisher` is `LoggingEventPublisher` in all environments (`commerce.WebApi/Program.cs:290`; `Stubs/LoggingEventPublisher.cs:17-30`), so outbox rows are marked `published` after a log line. Consumers poll the DB directly (e.g. PartnerBookingDebit, LoyaltyEarn, NotificationMapping).
- Impact: readiness assessments based on the spec overstate capability.
- Recommended remediation: update the spec to "DB-polled outbox, no broker" until a broker is introduced. Nothing currently requires one.
- Dependencies / priority: P3.

## Positive controls verified
- **Fail-closed secrets at startup:** the JWT signing key is required outside Development (`core.Infrastructure/Auth/RsaJwtKeyProvider.cs:30-53`). The PII key fails closed outside Development (`SharedDataModel/DependencyInjection.cs:21-28`, `ConfigurePiiCipher`). Base `appsettings.json` files contain no secrets (all 6 read).
- **Network exposure:** only the gateway and admin-web publish ports. Services are internal. The JWKS hop is private (`docker-compose.yml:29-32,45,97-99,112-113`).
- **Edge security headers and CORS:** HSTS, nosniff, X-Frame-Options DENY and Referrer-Policy outside Development (`ServiceDefaults/Extensions.cs:211-237`, applied in `Gateway/Program.cs:296`). CORS has no localhost origins outside Development (`Gateway/Program.cs:191-193`). The middleware order is documented and sensible (L274-315).
- **Gateway resilience:** a per-cluster circuit breaker, timeout and bulkhead whose breaker state survives YARP client rebuilds (`ResilientForwarderHttpClientFactory.cs`). Outbound provider clients get a tuned resilience pipeline (`commerce.WebApi/Program.cs:211-251`).
- **Containers:** non-root runtime user, HEALTHCHECK, multi-stage builds (`Dockerfile:15-46`). Hashed SPA assets are cached as immutable and `index.html` is no-cache (`nginx.conf:8-19`).
- **Migration tool:** each migration runs in a single transaction with atomic bookkeeping, checksum drift detection, and a rollback file required before `up` (`migrate.sh:79-93,111-154,187-204`). CI enforces up/down pairing (`ci.yml:88-111`).
- **Backup tooling:** integrity check, partial-file cleanup, safe-by-default restore with typed confirmation, and a scratch-container verify that installs partman and postgis (`backup.sh:41-64`, `restore.sh:45-73`, `verify-backup.sh:33-74`).
- **Money-path idempotency under concurrency:** partner wallet debit inbox plus unique ledger key, subscription invoice uniqueness, unique billing-attempt idempotency key, and the `Razorpay-Idempotency` header (see SA-OPS-005 evidence).
- **Pooled-connection tenant hygiene:** a scoped RLS interceptor re-sets every GUC on each open (`RlsConnectionInterceptor.cs:92-100`, `SharedDataModel/DependencyInjection.cs:47-62`).
- **Error tracking:** Sentry is opt-in with PII off and expected 4xx exceptions filtered (`ServiceDefaults/Extensions.cs:68-117`).
- **CI evidence:** the backend build and all tests, including Testcontainers integration tests, **passed** on the latest `main` (job 108549585458). admin-web lint and build passed (job 108549585404).

## Open questions / not verified
- Whether any production or staging environment exists today, and what actually runs there (scheduler, proxy, managed DB, PITR). The repo contains no environment definitions. The only external reference is the removed `trywavio.in` tunnel (CI run 29584182499 commit message).
- Live partition runway on any real DB. `db/HANDOFF.md` (dev DB, 2026-06) reported runway to 2026-12-01.
- Runtime confirmation of SA-OPS-001, 002, 005 and 013. These need Docker and .NET, which are unavailable here.
- Whether `brand_id` appears in individual log message templates often enough to approximate tenant filtering. I did not audit every log call.
- Client consumption of `IsForceUpdate`/`MinAppVersion` in the mobile apps (MOB area).
- Whether GitHub branch protection is configured. It cannot be read with the tools I had, and the repeated red `main` suggests it is not.

## Verdict inputs
- **Q15 (production operations / deployability as a multi-tenant SaaS): Not Supported.** A single-node compose deployment is demonstrably configured. However, release is ungated (red CI ships), migrations are manual, uploads are ephemeral, partition maintenance is dev-machine-only, auth rate limiting collapses behind the gateway, and observability is neither tenant-aware nor exported.
- **DB-Q8 (connection pooling, infrastructure angle): Partially Supported.** In-process Npgsql pooling with per-open RLS GUC reset is correct for one replica per host. There is no pool sizing or connection budget and no PgBouncer, and the session-level GUC design rules out transaction-mode pooling without code changes.
- **CI/CD & release safety: Not Supported.** See SA-OPS-006 and SA-OPS-012.
- **DB migration & rollback in pipeline: Partially Supported.** The tool is sound but is not run or tested by any pipeline (SA-OPS-007).
- **Secrets management: Partially Supported.** Startup fails closed and base config is secret-free, but there is no secrets store and the documented provider is missing (SA-OPS-009).
- **Tenant-aware observability & alerting: Not Supported.** See SA-OPS-008.
- **Audit events: Partially Supported.** There is a DB audit trail with redaction. Correlation is not tied to traces. Depth belongs to SEC/DB.
- **Backup/restore/DR: Partially Supported.** Scripts are solid, but there is no PITR, no encryption and no proven schedule. **Tenant-level restore: Not Supported** (export only).
- **Per-tenant rate limits & quotas: Not Supported.** Brand partitions exist but are spoofable, uniform and not plan-aware (SA-OPS-002, SA-OPS-014).
- **Background jobs safe under horizontal scale: Not Supported.** See SA-OPS-005.
- **Horizontal scaling of API hosts: Not Supported** today because of local storage, in-process caches, limiters and workers (SA-OPS-003, SA-OPS-015).
- **Custom domains / wildcard SSL: Not Supported** end to end (SA-OPS-013).
- **Environment separation: Partially Supported.** Dev and Production behaviour is properly split in code. There is only one compose topology and no staging definition.
- **Mobile release (OTA/store) & vertical back-compat: Partially Supported.** There are `/api/v1` paths and a force-update flag, but OTA is not functional and there is no build pipeline (SA-OPS-016).
