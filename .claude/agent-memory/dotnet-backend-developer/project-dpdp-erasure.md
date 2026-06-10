---
name: project-dpdp-erasure
description: DPDP erasure pipeline (Task #14) — anonymization worker, retention sweeps, customer self-service deletion endpoints, grievance config
metadata:
  type: project
---

Task #14 delivered the full DPDP erasure pipeline. Key non-obvious decisions:

**Why Catalog owns the deletion endpoints (not Identity):** The `Customer` aggregate lives in `customer_catalog` (Catalog BC). The self-service endpoints follow the existing `/api/v1/customer/account/` pattern in `CustomerEndpoints.cs`. Identity only owns auth/token concerns.

**Why:** The `account_deletion_requests` table and `AccountDeletionRequest` entity were already in SharedDataModel with status CHECK: `pending|grace_period|soft_deleted|hard_deleted|cancelled|failed`. The `customers.status` CHECK is `active|blocked|deletion_requested|deleted` — both `deletion_requested` (when request created) and `deleted` (post-anonymization) are valid terminal values.

**How to apply:** When working on customer self-service flows, check `customers.status` CHECK constraint values first; `deleted` is the terminal erasure state used by the Worker, not a soft-delete.

**CustomerAnonymizer is a public static class** (extracted from `CustomerErasureService`) so it can be unit-tested across project boundaries without `InternalsVisibleTo`. Tests live in `laundryghar.Worker.Tests`.

**Tombstone format (CORRECTED — varchar(20) constraint):**
- Phone: `+00del{customerId.ToString("N")[..12]}` — "+00del" (6) + 12 hex = 18 chars, fits phone_e164 varchar(20). Prior "+00deleted" prefix was 10 chars → 22 total → P1 crash (PostgresException 22001).
- Email: `deleted+{tombstoneId}@anon.invalid` — synthetic, unique (email column is USER-DEFINED type, no varchar cap)

**Grace period override:** `Worker:ErasureGracePeriodMinutesOverride > 0` activates a Development fast-path (e.g. 2 minutes) for testing erasure without waiting 30 days. Must be 0 in Production.

**pg_partman finding:** `rider_location_pings` already had `retention = 14 days, retention_keep_table = f` — correctly configured. `audit_logs` and `notifications_log` have no partman retention — intentional (audit/financial data kept indefinitely until admin decides).

**Retention sweep targets app-level deletes only** (notifications_outbox, otp_codes, refresh_tokens) — partitioned tables are left to pg_partman.

**Grievance config:** seeded to `engagement_cms.mobile_app_config` with key `grievance_officer` for all brands, platforms android + ios. Placeholders must be replaced before production. See PRODUCTION_ENV.md.
