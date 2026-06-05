# Laundry Ghar — Deployment

> Cloud topology, environments, CI/CD, secrets, and the operational jobs the schema assumes (pg_partman maintenance, outbox dispatcher, materialized-view refresh). Foundation agent owns the Wave 0 slice; security-compliance-auditor owns the Wave 4 hardening slice.

---

## Environments

| Env       | Purpose              | Data                 | Notes                                       |
| --------- | -------------------- | -------------------- | ------------------------------------------- |
| `local`   | Dev laptops          | Seed only            | Docker Compose: Postgres + Redis + RabbitMQ |
| `dev`     | Shared integration   | Synthetic            | Auto-deploy on merge to `develop`           |
| `staging` | Pre-prod, load tests | Anonymized prod-like | Mirrors prod topology                       |
| `prod`    | Live                 | Real PII             | India region only (DPDP residency)          |

---

## Cloud topology (target)

- **Region:** India (Mumbai/Hyderabad) — required for DPDP data residency.
- **Compute:** containers (API, Worker, Gateway) behind a load balancer; autoscaled.
- **Database:** managed PostgreSQL 16+, primary + streaming replica, PITR enabled.
- **Cache:** managed Redis (OTP store, rate-limit buckets, session, hot reads).
- **Queue:** RabbitMQ (MassTransit) for outbox-published domain events.
- **Object storage:** S3-compatible / Azure Blob — garment photos, invoices, receipts; signed URLs only; field-level encryption for sensitive blobs.
- **CDN:** in front of public assets and signed media.

---

## Required PostgreSQL extensions

These must exist before migrations run (see `database/01_tenancy_identity.sql`):

```sql
pgcrypto · citext · postgis · pg_partman · pg_trgm · btree_gin · pg_stat_statements · unaccent
```

Managed-Postgres note: confirm the provider allowlists `pg_partman` and `postgis`. If not available, partitioning falls back to a scheduled function (documented as a follow-up; do not silently drop partitioning — it's load-bearing per ADR-004).

---

## Operational jobs (the schema assumes these run)

| Job                    | Cadence      | What it does                                                                      | Owner table(s)                                                                      |
| ---------------------- | ------------ | --------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| pg_partman maintenance | hourly       | Pre-make + retire partitions                                                      | `orders`, `audit_logs`, `process_logs`, `notifications_log`, `rider_location_pings` |
| Outbox dispatcher      | continuous   | Publish `outbox_events`, send `notifications_outbox` with retry/backoff           | both outbox tables                                                                  |
| MV refresh — fast      | every 15 min | `mv_daily_store_revenue`                                                          | analytics                                                                           |
| MV refresh — hourly    | hourly       | `mv_monthly_franchise_revenue`, `mv_warehouse_throughput`, `mv_rider_performance` | analytics                                                                           |
| MV refresh — daily     | nightly      | `mv_customer_ltv`                                                                 | analytics                                                                           |
| OTP cleanup            | every 15 min | delete expired `otp_codes`                                                        | identity                                                                            |
| Token cleanup          | nightly      | purge expired/revoked `refresh_tokens`, `password_resets`                         | identity                                                                            |
| GPS retention          | daily        | drop `rider_location_pings` partitions > 14 days                                  | riders                                                                              |
| Deletion pipeline      | nightly      | advance `account_deletion_requests` grace→soft→hard/anonymize                     | customers                                                                           |
| Royalty run            | monthly      | build `royalty_calculations` → `royalty_invoices`                                 | finance                                                                             |

All jobs run via Hangfire in the Worker service. Each must set `app.bypass_rls='true'` (they operate cross-tenant).

---

## CI/CD

```
push → build → unit tests → integration tests (incl. RLS leakage test)
     → migration dry-run → container build → scan → deploy(dev)
     → smoke → (manual gate) → deploy(staging) → load+E2E
     → (manual gate) → deploy(prod, blue/green)
```

- **RLS leakage test is a required gate** — tenant A must not read tenant B. A failure blocks the pipeline.
- Migrations run forward-only; every migration reviewed by `database-architect`.
- Blue/green for prod; DB migrations are backward-compatible within a release window.

---

## Secrets

- All secrets in a managed Key Vault / secrets manager — never in repo, never in `.env` committed.
- `.env.example` lists variable names with placeholders only.
- DB connection strings, JWT signing keys (RS256), Razorpay keys, WhatsApp/Meta tokens, SMS provider keys, S3 credentials, KMS key references — all vault-resident.
- Field-level encryption keys: KMS-managed, 90-day rotation; the app stores only KMS _references_, never key material (mirrors the encryption registry pattern).

---

## Security baseline (Wave 4 verifies)

- TLS 1.3 everywhere; HSTS.
- JWT RS256, 15-min access + 30-day refresh, rotating refresh families (reuse detection).
- Argon2id password hashing; OTP 6-digit, 5-min TTL, Redis-backed, 3-attempt cap.
- Rate limiting via Redis sliding window.
- Upload scanning (ClamAV) before any blob is trusted; signed URLs for retrieval.
- PCI-DSS SAQ-A posture (Razorpay tokenization; we never store PAN).
- DPDP Act 2023: purpose-bound consent (ADR-008), erasure pipeline, India residency.

---

## Performance targets (Wave 4 load test)

```
API latency      p50 < 150ms   p95 < 500ms   p99 < 1s
Mobile cold start                < 2s
DB query (hot paths)             < 50ms with indexes
Order placement E2E              < 2s perceived
```

---

## Disaster recovery

- **RPO:** ≤ 5 min (streaming replication + WAL archiving).
- **RTO:** ≤ 1 hour (promote replica or PITR restore).
- DR drill in Wave 4 and quarterly thereafter: restore to a point in time, verify table count == 92 and a known order reconciles.
