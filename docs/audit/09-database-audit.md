# 09 — Database Audit: Indexing, Idempotency and Row-Level Security

Consolidated deliverable · Date: 2026-10-09 · Branch audited: `claude/brave-dijkstra-6hlddw`
Primary sources: [08b](specialists/08b-database.md) (DB specialist: matrices, live cluster), [10c](specialists/10c-qa-verification-db-mobile.md) (QA-C: independent rebuild and corrections), [10a](specialists/10a-qa-verification-security.md) (QA-A: SQL repros S1–S7), [08](specialists/08-backend-api.md) (backend: handler-side idempotency), [02](specialists/02-multitenancy.md) (tenancy: tenant-context path), [01b](specialists/01b-architect-challenge-review.md) (architect: disputes and roadmap). Canonical IDs, final severities and phases come from [FINDINGS](../../FINDINGS.md) / [`findings-registry.json`](findings-registry.json).

## Summary

- **The RLS model is right; the wiring is not.** Production runs as `app_user` (NOSUPERUSER, NOBYPASSRLS, owns nothing). All 126 tables with a `brand_id` column have RLS, and brand A/B isolation held for SELECT, INSERT, UPDATE and DELETE on two independent rebuilds.
- **Migration 0031 is a fail-closed outage.** Its RESTRICTIVE sub-brand policy returns 0 rows and rejects writes for every session without `scope_nodes`. That covers customers, API keys and **all non-platform staff on the commerce host**. Canonical: [SA-TEN-001](../../FINDINGS.md#sa-ten-001), Critical, P0.
- **Customer-vs-customer isolation is not enforced by the DB on the commerce host.** That host never sets `app.current_customer_id`, so in the repro a customer saw another customer's wallet (10c T1b). Canonical: [SA-TEN-002](../../FINDINGS.md#sa-ten-002), High.
- **The DB trusts the application.** Five bypass routes were reproduced: the bypass GUC is self-settable, SECURITY DEFINER functions take any brand id, eight identity tables have RLS off, the materialized views (MVs) cannot carry RLS, and there are no composite tenant FKs.
- **Idempotency is mostly check-then-act.** 08b found only six operations truly DB-enforced (pickup scheduling, partner wallet, partner-booking inbox, subscription invoice issuance, notification enqueue, wallet top-up credit). The two-session repros showed an **over-refund** (120 refunded on a 100 payment), a **wallet lost update** and a **double outbox claim**. Pickup booking is truly idempotent only when a key is sent, and the customer app sends none.
- **Indexing is good on the main list paths.** The gaps are a webhook lookup that scans every tenant's payments, an outbox poll index that does not match the poll query, 69 unindexed FKs and 55 redundant indexes. All plans are from **synthetic data only**.
- **The documented schema build cannot reproduce production, and re-running it silently reverts the RLS bypass hardening** ([SA-DB-002](../../FINDINGS.md#sa-db-002)). A stale pg_partman row makes every maintenance run abort ([SA-QC-003](../../FINDINGS.md#sa-qc-003)).
- **Verdicts:** DB-Q5 Fully Supported; DB-Q3 and DB-Q9 Not Supported; all other DB-Q verdicts Partially Supported. None was checked against a production database.

Status labels are preserved from the source reports: **Verified**, **Partially Verified**, **Suspected** and **Not Tested**. Severities are the registry's **final** values; where QA changed a value, the original is shown as `orig`.

---

## 1. Method and environment

### 1.1 How each agent built its throwaway PostgreSQL 16 cluster

No agent touched a real database. Each built a private, disposable cluster, ran it as the `postgres` OS user, and deleted it afterwards.

| Agent / report | Port · data dir | Schema fidelity | Build recipe | Teardown |
|---|---|---|---|---|
| DB specialist ([08b §Scope](specialists/08b-database.md)) | 55432 · `/var/tmp/lg-audit-8b` | **Full repo build.** PG 16.15, with `postgresql-16-partman` 5.0.1 and `postgresql-16-postgis-3` 3.4.2 installed with apt at OS level, so no extension was stubbed. | See below. Result: 162 logical tables, 136 with RLS, 190 policies, 1,649 indexes. | Stopped and deleted |
| QA-C ([10c C1–C10](specialists/10c-qa-verification-db-mobile.md)) | 55435 · `/var/tmp/lg-qa-c` | **Full repo build, independent of 08b.** pg_partman and postgis were already present at OS level. | Same sequence, run separately, then an own two-tenant fixture (`fixture.sql`) instead of the demo seeds | Stopped and deleted; `git status` clean |
| QA-A ([10a §1](specialists/10a-qa-verification-security.md)) | 55434 · `/var/tmp/lg-qa-a` | **Trimmed tables plus verbatim extracts.** Function and policy text copied from `rls_proposal.sql:65-83`, `harden_app_user_and_rls_bypass.sql:31-38`, `0025…up.sql:49-68`, the whole of 0031, `0015…up.sql:265-294` and the whole of 0029. | `00_base.sql` … `08_scen.sql` (scenarios S1–S7, S2b, S7b) | Stopped and deleted |
| Multi-tenancy ([02 §Scope](specialists/02-multitenancy.md)) | 55433 · `/var/tmp/lg-mt02` | **Trimmed `commerce.payments` and `order_lifecycle.orders`** plus verbatim policy and function text. No partman or postgis. | `repro_setup.sql`, `f0031.sql`, `repro_scen.sql`, `repro_susp.sql` | — |

Full-build sequence used by 08b and independently by 10c:
1. `DB_NAME=… DB_PORT=… db/build_from_scratch.sh`. Exit 0, but with only 92 *inert* policies.
2. `db/tools/migrate.sh up`. **It failed at 0005**: `relation "identity_access.modules" does not exist` (SA-DB-002).
3. `db/patches/*.sql` applied in git first-commit order, repeated in passes until no further progress. `wipe_demo_*` was excluded. 10c needed 3 passes, so the patch set has no deterministic order.
4. `migrate.sh up` again. 0005–0033 then applied cleanly (29 migrations).
5. `harden_app_user_and_rls_bypass.sql` was re-applied, because in git order `rls_proposal.sql` overwrites the hardened `kernel.rls_bypass()` (10c C6).

### 1.2 Workarounds, and what each may distort

| Workaround | Why it was needed | What it may distort |
|---|---|---|
| Data directory under `/var/tmp`, not the scratchpad | The harness keeps `/tmp/claude-0` at mode 0700, and the `postgres` user cannot traverse it | Nothing material |
| Patches applied in git first-commit order, in several passes | No ordered runner exists for the ~150 files in `db/patches/` (SA-DB-002) | **The production patch order is unknown.** Any finding that depends on which patch "wins" (the `rls_bypass()` body, function grants, partman config) may differ in production |
| `DO $verify$` blocks removed from scratch copies of `phase1_slice_e_garment_to_fulfillment.sql` (lines 59-92) and `phase4_role_vertical_key.sql` (lines 85-113); the DDL was unchanged | They assert on RBAC rows that only the .NET `IdentitySeeder` creates | None for DDL. RBAC seed data was absent or hand-made |
| Demo seeds skipped, or partly applied over a stub brand `5b375161-…` (08b); 10c used its own fixture | The seeds depend on a pre-existing hand-made brand | Seed data only |
| `harden_app_user_and_rls_bypass.sql` re-applied after the run | `rls_proposal.sql` overwrote the hardened `kernel.rls_bypass()` | **The tests assume the hardened bypass** (accepts `'true'`). An environment rebuilt with `build_from_scratch.sh` would instead fail closed on every bypass path (SA-DB-002) |
| Principals emulated in SQL with the exact 12-GUC `SELECT set_config(…, false)` statement from `RlsConnectionInterceptor.cs:93-117` | No .NET SDK and no Docker | GUC values were derived by reading `HttpContextCurrentTenant.cs:24-63` and `CommerceHostCurrentTenant.cs:42-94`. The interceptor itself was never executed |
| Trimmed schemas (10a, 02) | No partman or postgis at the time; faster to isolate the predicate | Other policies, triggers and constraints on those tables were absent. Those repros prove **predicate semantics only**. The full-build repros (08b, 10c) agree with them |
| Synthetic data for EXPLAIN (08b): brands A and B, 60k orders, 60k payments, 20k customers, 40k outbox events | No production data | Two equal-sized, uniformly distributed brands. Plans are **relative evidence only** |
| Npgsql reset-on-close (`DISCARD ALL`) reproduced in SQL, not observed at runtime | No .NET runtime | Pool behaviour of the deployed Npgsql version is unverified (SA-DB-016) |

### 1.3 What this audit does not have

> **No production statistics were available.** There are no `pg_stat_user_indexes`, `pg_stat_statements`, bloat or row-count figures; a fresh cluster has none, and **none are reported anywhere in this audit**. Every `EXPLAIN (ANALYZE, BUFFERS)` figure (E1–E7) comes from **synthetic data** on a throwaway cluster. Treat timings as relative, not as production latency.

The following are also absent:
- No HTTP request was executed end to end. All application behaviour comes from static traces.
- No xUnit or Testcontainers test was run. The repo's RLS tests (`RlsIsolationTests`, `SubBrandScopeRlsTests`, `Partner*RlsTests`, and others) were read, not executed (see 02's test section).
- The production database state is unknown: the patch order, the `kernel.rls_bypass()` body, the `partman.part_config` contents, the `app_user` function grants, whether 0031 is applied, the replica count, and whether any pooler sits in front of PostgreSQL.
- Razorpay behaviour was not checked: neither the `Razorpay-Idempotency` header nor the multi-attempt and refund semantics.

---

## 2. Index audit matrix

This section takes rows I1–I35 from [08b](specialists/08b-database.md#index-audit-matrix) and applies the QA corrections from [10c](specialists/10c-qa-verification-db-mobile.md). Rows Q1–Q3 are constraint gaps that QA-C verified in the catalog; they are not new findings. "Live" means confirmed in the scratch-cluster catalog. Status values: OK / Gap / Redundant / Risk.

### 2.1 Matrix

| # | table | index / constraint | columns & order | purpose | status | supporting query (code) | problem | recommendation | impact (canonical ID, final severity) |
|---|---|---|---|---|---|---|---|---|---|
| I1 | `order_lifecycle.orders` (partitioned monthly) | `orders_pkey` | (id, created_at) | PK including the partition key | OK (live) | all order loads | Every FK to orders must be composite, and it is (`(order_id, order_created_at)`) | — | — |
| I2 | orders | `orders_order_number_created_at_key` UNIQUE | (order_number, created_at) | "unique" order number | Risk (live) | `CreateOrderCommand.cs:806-815` → `order_lifecycle.next_order_number()` | Including `created_at` makes the key useless for duplicate detection; uniqueness rests on the generator alone | A non-partitioned `order_number_registry(brand_id, order_number)` PK, written in the same transaction | Undetectable duplicates if the generator or store code changes (index matrix only; no finding ID) |
| I3 | orders | `idx_orders_brand_store_status` partial `deleted_at IS NULL` | (brand_id, store_id, status, created_at DESC) | admin list | OK (E4: Merge Append of per-partition index scans, 0.86 ms, synthetic) | order list handlers | — | — | — |
| I4 | orders | `idx_orders_customer` | (customer_id, created_at DESC) | customer history | OK (E7: index scan, 1.9 ms, synthetic) | customer self queries | `idx_orders_customer_id_fk (customer_id)` is a redundant prefix | Drop the redundant one (see 2.3) | write amplification ([SA-DB-018](../../FINDINGS.md#sa-db-018), Medium) |
| I5 | orders | `idx_orders_metadata_gin` | gin(metadata) | idempotency lookup by `metadata @> {"idempotency_key":…}` | **Risk** (E2: the planner used the per-partition `brand_id` index and filtered 30k rows; **421 ms, synthetic**) | `CreateOrderCommand.cs:71-94` | No unique constraint; slow because RLS evaluates per row | A dedicated `idempotency_key` column with a partial UNIQUE `(brand_id, idempotency_key[, created_at])`, or a non-partitioned idempotency table; catch 23505 | Duplicate orders and double debits ([SA-API-004](../../FINDINGS.md#sa-api-004), High; SA-DB-011 is a dup, orig Medium) |
| I6 | orders | `idx_orders_pickup_slot_fk` and `idx_orders_pickup_slot_id_fk` | (pickup_slot_id) ×2 | FK | **Redundant**: exact duplicate (live) | — | duplicate | Drop one | write cost (SA-DB-018) |
| I7 | `commerce.payments` | **none** on `gateway_order_id` | — | webhook lookup | **Gap** (E1: **Seq Scan over all brands, Rows Removed 60,000**, synthetic) | `RazorpayWebhookHandler.cs:96-100` (runs with bypass, so it scans every tenant) | Every webhook seq-scans the whole payments table | `CREATE UNIQUE INDEX CONCURRENTLY … ON commerce.payments (gateway, gateway_order_id) WHERE gateway_order_id IS NOT NULL` | Latency grows linearly with platform payments, then webhook timeouts and retries (SA-API-004 group) |
| I8 | payments | `idx_payments_gateway` partial, **non-unique** | (gateway, gateway_payment_id) | dedupe | Gap | webhook / verify | A gateway payment can be recorded twice | Make it a UNIQUE partial index | duplicate capture rows (no separate ID) |
| I9 | payments | `payments_idempotency_key_key` UNIQUE, `payments_payment_number_key` UNIQUE | (idempotency_key), (payment_number) | idempotency | Risk: **global, not brand-scoped** (live) | `CustomerPaymentHandlers.cs:37-43` looks up by (key, brand, customer) | A cross-tenant or cross-customer key collision raises 23505 instead of returning the existing row | `(brand_id, idempotency_key)` | low probability with UUID keys ([SA-DB-007](../../FINDINGS.md#sa-db-007) class; SA-TEN-012 is a dup) |
| I10 | `commerce.payment_mandates` | `idx_mandate_gateway` non-unique | (gateway, gateway_mandate_id) | mandate dedupe | Gap | mandate flows | Duplicate mandates are possible | UNIQUE partial | double-charge surface (no separate ID) |
| I11 | `commerce.payment_refunds` | `payment_refunds_idempotency_key_key` UNIQUE partial; trigger `trg_check_refund_cap` | (idempotency_key) | refund idempotency and cap | **Risk: race reproduced twice** (08b; 10c T5: two concurrent 60.00 refunds on 100.00 both committed, 120.00 refunded) | `AdminPaymentHandlers.cs:100-118,148-231`; `payment_idempotency.sql:29-80` (SECURITY INVOKER, no lock) | The cap trigger does not lock the parent payment. **QA addition:** the API's `refund_type` values violate the CHECK (Q3), and the gateway is called before the insert | `PERFORM 1 FROM commerce.payments WHERE id = NEW.original_payment_id FOR UPDATE` in the trigger and handler; insert-then-call ordering | over-refund ([SA-API-009](../../FINDINGS.md#sa-api-009), High; SA-DB-006 is a dup) |
| I12 | `commerce.coupon_redemptions` | **no unique** beyond the PK | — | single use / one per order | **Gap** (live) | `CustomerCouponHandlers.cs:85-142`; `CreateOrderCommand.cs:292-311,757-771` | Limits enforced in the app only | Partial UNIQUE `(order_id) WHERE reverted_at IS NULL`; guarded `UPDATE coupons SET current_usage_count = current_usage_count + 1 WHERE … AND (max_total_uses IS NULL OR current_usage_count < max_total_uses)` | over-redemption ([SA-DB-010](../../FINDINGS.md#sa-db-010), Medium) |
| I13 | `commerce.wallet_transactions` | `wallet_transactions_idempotency_key_key` UNIQUE (global) | (idempotency_key) | ledger dedupe | OK for top-up (the key embeds the payment id); Risk for admin-supplied keys | `CustomerWalletHandlers.cs:137-211`; `AdminWalletHandlers.cs:~93-172` | A client key reused by another brand returns 23505 | `(brand_id, idempotency_key)` | low (SA-DB-007 class) |
| I14 | `commerce.wallet_accounts` | `wallet_accounts_customer_id_key` UNIQUE plus `idx_wallet_customer` | (customer_id) ×2 | one wallet per customer | OK / Redundant | — | duplicate index | Drop `idx_wallet_customer` | — (SA-DB-018) |
| I15 | `order_lifecycle.delivery_slots` | UNIQUE (store_id, slot_date, slot_start, slot_type); CHECK `booked_count <= capacity` | — | slot uniqueness and capacity | **OK** (live) | `PickupCommands.cs:398-415` atomic `UPDATE … WHERE booked_count < capacity` | The tenant key lacks brand_id, but store_id is brand-owned. **QA:** the admin/POS path bypasses the increment ([SA-API-019](../../FINDINGS.md#sa-api-019)) | Route admin creation through the same atomic increment | capacity drift (SA-API-019, Medium) |
| I16 | `delivery_slot_bookings` | no unique | — | booking ↔ pickup link | Gap (minor) | `PickupCommands.cs:448-458` links by (slot, customer, `pickup_request_id IS NULL`) | A concurrent double booking of the same slot by one customer can mis-link | Partial UNIQUE `(pickup_request_id) WHERE status = 'active'` | minor (no separate ID) |
| I17 | `order_lifecycle.pickup_requests` | `pickup_requests_customer_idempotency_key` partial UNIQUE | (customer_id, idempotency_key) WHERE key IS NOT NULL | idempotency | **OK as a DB control** (live). **QA correction:** effective only when a key is sent, and the customer app sends none | `PickupCommands.cs:338-510` (catches 23505 by constraint name) | Unused by the client ([SA-API-018](../../FINDINGS.md#sa-api-018); SA-MOB-018 is a dup). Customer inserts are currently denied by 0031 (SA-TEN-001) | The client sends `Idempotency-Key` per checkout attempt | duplicate pickups on retry (SA-API-018, Medium) |
| I18 | pickup_requests | `pickup_requests_request_number_key` UNIQUE (global) | (request_number) | number | Risk (live) | `PickupCommands.cs:61-63` `PKP-{yyyy}-{brandId[..4]}-{brandCount+1}` | Brands sharing a 4-hex prefix can collide; COUNT+1 races within a brand | `(brand_id, request_number)` plus a per-brand counter | 23505 on create ([SA-DB-007](../../FINDINGS.md#sa-db-007), **Medium**, orig High) |
| I19 | `finance_royalty.expenses` | `expenses_expense_number_key` UNIQUE (global) | (expense_number) | number | **Risk, reproduced**: brand B could not insert its first expense because brand A held the same number | `ExpenseCommands.cs:204-205` `EXP-{yyyyMMdd}-{brandCount+1}` | Cross-tenant DoS plus an existence oracle. **QA:** the intra-brand variant under 0031 is more frequent ([SA-QC-002](../../FINDINGS.md#sa-qc-002)) | `(brand_id, expense_number)` plus a per-brand counter (the `next_order_number` upsert pattern) | SA-DB-007 (Medium); SA-QC-002 (Medium) |
| I20 | `laundry_fulfillment.warehouse_batches`, `fulfillment_unit(_tags)`, `tenancy_org.franchise_agreements` | global UNIQUE on `batch_number`, `tag_code`, `agreement_number` | — | numbers | Risk (live). **QA, T8:** a W2-scoped user counted 0 visible batches and collided with W1's `WB-20261009-0001` | `CreateWarehouseBatch.cs:48-49`; `GenerateTags.cs:34-42`; `SaveCommercials.cs:53` | Same class as I19, plus generators that cannot see the whole brand once 0031 applies | Brand-scoped uniques plus counters run under a brand-only predicate or a SECURITY DEFINER function | SA-DB-007; **SA-QC-002** (Verified for batches) |
| I21 | royalty / subscription / franchise-subscription invoices, `customers.referral_code`, `riders.user_id`, … | global UNIQUEs (46 on brand-owned tables, live) | — | numbers and codes | Risk | various | Generator uniqueness is not tenant-scoped (classified in 2.4) | Review each; prefer `(brand_id, …)` | cross-tenant collisions (SA-DB-007) |
| I22 | `kernel.outbox_events` | `idx_outbox_pending` (status='pending'), `idx_outbox_events_retry` (status='failed' AND attempts < 10) | (occurred_at, status) / (next_attempt_at) | relay poll | Risk (E6: **Seq Scan, removed 39,600 of 40,000**, synthetic) | `OutboxEventRelayService.cs:75-82` filters `status IN ('pending','failed')` | Neither partial index matches the OR predicate; the scan grows with published rows | `CREATE INDEX … (occurred_at) WHERE status IN ('pending','failed')` plus a retention purge of published rows | relay latency (index matrix only; related [SA-DB-009](../../FINDINGS.md#sa-db-009)) |
| I23 | `kernel.outbox_consumed_events` | PK (consumer_name, event_id) | — | inbox dedupe | OK (live) | `PartnerBookingDebitService.cs:103-150` | FK `event_id` has no index, so FK checks on outbox deletes seq-scan | `CREATE INDEX … (event_id)` | purge cost (SA-DB-018) |
| I24 | `engagement_cms.notifications_outbox` | `notifications_outbox_idempotency_key_key` UNIQUE | (idempotency_key) | enqueue dedupe | OK (live) | `NotificationMappingService.cs:284-322`, key `evt:{eventId}:ch:{channel}` | Keyed per event, not per business fact | — | Duplicate events still produce duplicate sends |
| I25 | `commerce.subscription_invoices` / `franchise_subscription_invoices` | UNIQUE (subscription_id, billing_period_start) | — | one invoice per period | **OK** (live) | `SubscriptionBillingService.cs:~140-215` | — | — | — |
| I26 | `commerce.subscription_billing_attempts` | UNIQUE (idempotency_key) `subcharge-{invoiceId}-{attemptNo}` | — | attempt dedupe | Risk | `SubscriptionBillingService.cs:343-383` | The gateway charge happens **before** the row insert, so the key cannot prevent a second charge | Insert an `initiated` attempt row first (claim), then charge | double charge across replicas ([SA-DB-009](../../FINDINGS.md#sa-db-009), Medium) |
| I27 | `identity_access.users` | `users_email_key`, `users_phone_e164_key` UNIQUE (global) | — | login identity | Risk for SaaS | identity flows | One person cannot be staff in two tenants | Product decision | [SA-DB-020](../../FINDINGS.md#sa-db-020) (Low) |
| I28 | `identity_access.user_scope_memberships` | UNIQUE (user_id, scope_type, scope_id, role_id) | — | membership | OK as a key | `ScopeResolver` | No tenant FK on the polymorphic `scope_id`, and **no RLS** | See section 4 | [SA-DB-005](../../FINDINGS.md#sa-db-005) (High); app vector [SA-AUTHZ-003](../../FINDINGS.md#sa-authz-003) |
| I29 | 30 soft-delete tables | 30 UNIQUEs, **0 partial** on `deleted_at IS NULL` (live) | e.g. coupons (brand_id, code), customers (brand_id, phone_e164) | business keys | Gap | — | A soft-deleted row blocks reuse; contradicts the convention in `docs/SCHEMA_FULL.sql:17` | Partial UNIQUE `… WHERE deleted_at IS NULL` where reuse is intended | [SA-DB-019](../../FINDINGS.md#sa-db-019) (Low) |
| I30 | **69 FKs** (82 catalog rows including partition clones) | **no leading supporting index** (live) | see 2.2 | FK checks on parent DELETE/UPDATE | Gap | `kernel.purge_brand`, customer erasure | Child tables are seq-scanned on every parent delete | Index the high-churn children with `CREATE INDEX CONCURRENTLY` | slow purge and erasure, lock time ([SA-DB-018](../../FINDINGS.md#sa-db-018), Medium; QA: not re-verified) |
| I31 | **55 indexes** | prefix-redundant (41) or exact duplicates (14) (live) | see 2.3 | — | Redundant | — | Write amplification on hot tables (orders, payments, fulfillment_unit) | Drop **only after** reviewing production `pg_stat_user_indexes` (not available here) | write throughput (SA-DB-018) |
| I32 | 14 brand tables | **no brand_id-leading index** (live) | `subscription_usage_ledger`, `subscription_billing_attempts`, `ticket_messages`, `rider_ratings`, `delivery_schedules`, `partner_bookings`, salon tables, … | RLS / tenant filter | Gap (small tables today) | — | brand-scoped scans | Add `(brand_id, …)` where these tables grow | low now |
| I33 | all 39 restrictive-policy tables | `rls_subbrand_scope` → `kernel.within_scope_cols()` (plpgsql) | per row | sub-brand scope | Risk (E3 vs E5: brand-A count over 30k orders, **384 ms with RLS vs 48 ms with bypass**, synthetic; QA: not re-run) | every list, count and report | Non-inlinable per-row function | An inlinable SQL predicate over `(SELECT kernel.current_scope_nodes())` | about 8× CPU on scans ([SA-DB-017](../../FINDINGS.md#sa-db-017), Medium) |
| I34 | EF model vs DB | 128 `HasDatabaseName` declarations | — | DB-first mapping | OK: 126 of 128 exist; the 2 misses are PostgreSQL's 63-character name truncation | `Persistence/Configurations/**` | cosmetic | align names | none |
| I35 | `docs/SCHEMA_FULL.sql` | — | — | reference | Stale: 102 tables in a single schema vs 162 tables in 14 schemas live | — | Misleads reviewers | Regenerate from `pg_dump --schema-only`, or mark it historical | — |
| Q1 | `order_lifecycle.delivery_assignments` | only the PK is unique; CHECK on status and leg_type only; only trigger is `set_updated_at` (10c T11) | — | one live leg per job | **Gap** (live, QA-C) | `PickupCommands.cs:204-279`; `DeliveryAssignmentCommands.cs:29-107`; `AutoDispatchService.cs:313-315,396-400`; `OfferActions.cs:39-96` | No DB uniqueness or transition guard | Partial UNIQUE `(pickup_request_id) WHERE status IN ('offered','assigned','accepted','started','arrived')`, plus the same on `(order_id, leg_type)` | two riders on one job ([SA-MOB-002](../../FINDINGS.md#sa-mob-002), High; SA-DB-022 is a dup, orig Low) |
| Q2 | `finance_royalty.expenses`, `warehouse_batches` under 0031 | global UNIQUE plus scope-blind COUNT+1 (10c T8) | — | numbers | **Risk, reproduced** (batches) | as I19/I20 | Scoped staff hit 23505 against siblings in the same brand | Per-brand counter function | [SA-QC-002](../../FINDINGS.md#sa-qc-002) (Medium) |
| Q3 | `commerce.payment_refunds.refund_type` | CHECK `IN ('full','partial','goodwill','dispute_loss')` (`database_scripts/06_bc6_commerce.sql:418-419`) | — | contract | **Mismatch, reproduced** (10c T7d: `'wallet'` and `'gateway'` violate the CHECK) | `CommerceDtos.cs:438-449` documents `"gateway"`/`"wallet"`; `AdminPaymentHandlers.cs:132,150,217-230` | The Razorpay refund is issued **before** the failing INSERT | Split `RefundMethod` from `RefundType`; insert `processing` and commit before calling the gateway | money leaves with no ledger row ([SA-QC-001](../../FINDINGS.md#sa-qc-001), High) |

**Synthetic EXPLAIN reference (08b, not re-run by QA).**

| Plan | Query | Plan shape (synthetic) | Timing (synthetic) |
|---|---|---|---|
| E1 | webhook lookup by `gateway_order_id` | Seq Scan on payments, Rows Removed 60,000 | — |
| E2 | order idempotency `metadata @>` | per-partition `brand_id` index, filtering 30k rows | 421 ms |
| E3 / E5 | brand-A `count(*)` over 30k orders | E3 with RLS / E5 with bypass | 384 ms / 48 ms |
| E4 | admin order list | Merge Append of per-partition index scans | 0.86 ms |
| E6 | outbox relay poll | Seq Scan, removed 39,600 of 40,000 | — |
| E7 | customer order history | index scan | 1.9 ms |

### 2.2 The 69 FKs without a leading supporting index (SA-DB-018, I30)

Source: the DB agent's catalog artefact `scratchpad/database/fk_noindex.txt` (outside the repo, cited in 08b). It has 82 rows: 69 logical FKs plus 13 partition-clone rows (`customer_packages_…_fkey1` … `fkey13`, one per `orders_p*` partition and the default). Per schema: identity_access 15, commerce 9, order_lifecycle 8, laundry_fulfillment 8, customer_catalog 8, tenancy_org 6, finance_royalty 4, engagement_cms 4, salon_fulfillment 2, logistics 2, authz 2, kernel 1.

<details><summary>Full list (child.column → parent), as recorded in the artefact</summary>

| Schema | Child column(s) → parent |
|---|---|
| identity_access (15) | `roles.feature_key`→features; `otp_codes.user_id`→users; `refresh_tokens.parent_token_id`→refresh_tokens; `password_resets.user_id`→users; `oauth_authorization_codes.client_id`→oauth_clients; `modules.feature_key`→features; `user_permission_override.permission_id`→permissions; `brand_feature.feature_key`→features; `bundle_feature.feature_key`→features; `vertical_templates.default_bundle_code`→module_bundle; `role_presets.requires_feature`→features; `impersonation_grants.approved_by_user_id` / `revoked_by_user_id` / `support_user_id`→users; `api_keys.revoked_by_user_id`→users |
| commerce (9) | `customer_packages.(purchase_order_id, purchase_order_created_at)`→orders (two constraints recorded: `customer_packages_purchase_order_id_fkey` and `…_purchase_order_created_fkey`); `loyalty_points_ledger.loyalty_program_id`→loyalty_programs; `promotions.coupon_id`→coupons; `payments.payment_method_id`→payment_methods; `customer_subscriptions.mandate_id`→payment_mandates; `subscription_invoices.payment_id`→payments; `subscription_billing_attempts.mandate_id`→payment_mandates; `partner_wallet_transactions.partner_wallet_account_id`→partner_wallet_accounts |
| order_lifecycle (8) | `order_items.fabric_type_id`→fabric_types; `order_items.item_variant_id`→item_variants; `order_addons.addon_id`→add_ons; `pickup_requests.address_id`→customer_addresses; `pickup_requests.rescheduled_from_id`→pickup_requests; `delivery_schedules.address_id`→customer_addresses; `delivery_schedules.brand_id`→brands; `delivery_schedules.store_id`→stores |
| laundry_fulfillment (8) | `fulfillment_unit.fabric_type_id` / `item_group_id` / `item_id` / `item_variant_id`→catalog; `fulfillment_unit.order_item_id`→order_items; `warehouse_batches.service_id`→services; `quality_checks.post_wash_inspection_id` / `pre_wash_inspection_id`→fulfillment_unit_inspections |
| customer_catalog (8) | `customers.primary_store_id`→stores; `customers.referred_by_customer_id`→customers; `customer_addresses.serviceable_store_id`→stores; `account_deletion_requests.user_id`→users; `price_lists.parent_price_list_id`→price_lists; `price_list_items.fabric_type_id`→fabric_types; `price_list_items.item_variant_id`→item_variants; `value_price_slabs.service_id`→services |
| tenancy_org (6) | `franchises.franchise_agreement_id`→franchise_agreements; `stores.franchise_id`→franchises; `store_warehouse_mappings.brand_id`→brands; `operating_hours.brand_id`→brands; `brand_cancellations.requested_by_user_id` / `withdrawn_by_user_id`→users |
| finance_royalty (4) | `shift_handovers.cash_book_id`→cash_books; `shift_handovers.to_user_id`→users; `royalty_invoices.franchise_agreement_id`→franchise_agreements; `franchise_subscription_invoices.payment_id`→commerce.payments |
| engagement_cms (4) | `notification_templates.parent_template_id`→notification_templates; `notifications_outbox.template_id`→notification_templates; `app_banners.coupon_id`→coupons; `app_banners.promotion_id`→promotions |
| salon_fulfillment (2) | `resource_bookings.appointment_id`→appointments; `resource_bookings.resource_id`→resources |
| logistics (2) | `rider_incentive_awards.rule_id`→incentive_rules; `partner_bookings.created_by_partner_user_id`→partner_users |
| authz (2) | `policy.action`→action; `policy_condition.left_attribute`→attribute |
| kernel (1) | `outbox_consumed_events.event_id`→outbox_events |

</details>

**Prioritise** the children of high-churn or purge-path parents: `fulfillment_unit.order_item_id`, `payments.payment_method_id`, `subscription_invoices.payment_id`, `otp_codes.user_id`, `password_resets.user_id`, `refresh_tokens.parent_token_id`, `outbox_consumed_events.event_id`, `pickup_requests.address_id`, `stores.franchise_id`, and the `delivery_schedules` columns (08b I30). The catalogue-reference FKs (feature keys, authz) point at near-static parents and can wait.

### 2.3 The 55 redundant indexes (SA-DB-018, I31)

Source: `scratchpad/database/redundant_idx.txt`. There are 14 exact duplicates and 41 prefix-redundant indexes. By schema: commerce 12, laundry_fulfillment 9, order_lifecycle 12, finance_royalty 7, logistics 5, identity_access 5, engagement_cms 2, customer_catalog 2, tenancy_org 1. Most are an auto-generated `idx_*_fk` single-column index shadowed by a hand-written composite index with the same leading column.

<details><summary>Examples by hot table (redundant ⊂ covering index)</summary>

- **orders:** `idx_orders_pickup_slot_id_fk` = `idx_orders_pickup_slot_fk` (exact); `idx_orders_customer_id_fk` ⊂ `idx_orders_customer`; `idx_orders_franchise_id_fk` ⊂ `idx_orders_franchise`.
- **order_items / order_addons:** `idx_order_items_order_id_fk` = `idx_orderitems_order`; `idx_order_addons_order_id_fk` = `idx_orderaddons_order` (both exact).
- **payments:** `idx_payments_brand_id_fk` ⊂ `idx_payments_status`; `idx_payments_customer_id_fk` ⊂ `idx_payments_customer`.
- **payment_refunds:** `idx_refunds_order` ⊂ `idx_payment_refunds_order_id_fk`; `idx_payment_refunds_brand_id_fk` ⊂ `idx_refunds_status`.
- **coupon_redemptions:** `idx_couponred_order` ⊂ `idx_coupon_redemptions_order_id_fk`; `idx_coupon_redemptions_customer_id_fk` ⊂ `idx_couponred_customer`.
- **wallet:** `idx_wallet_customer` = `wallet_accounts_customer_id_key`; `idx_wallet_transactions_customer_id_fk` ⊂ `idx_wallettxn_customer`.
- **fulfillment_unit:** `idx_garments_tag` = `garments_tag_code_key`; `idx_garments_order_id_fk` = `idx_garments_order`; `idx_garments_customer_id_fk` ⊂ `idx_garments_customer`.
- **delivery_assignments:** `idx_delivassign_order` ⊂ `idx_delivery_assignments_order_id_fk`; `idx_delivery_assignments_rider_id_fk` ⊂ `idx_delivassign_rider`; `idx_delivery_assignments_pickup_request_id_fk` = `idx_delivassign_pickup`.
- **invoices / oauth_clients / customer_identities:** `idx_invoices_order` = `invoices_order_id_key`; `idx_oauth_clients_client_id` = `oauth_clients_client_id_key`; `idx_custident_customer` ⊂ `customer_identities_customer_id_provider_key`.

</details>

Caution before dropping. A few prefix pairs differ in sort direction, for example `idx_cashbk_store_date (store_id, book_date DESC)` against the unique `(store_id, book_date, shift_label)`, and `idx_royinv_franchise` and `idx_subinv_subscription` likewise. B-tree backward scans usually make these redundant, but check them against real plans and `pg_stat_user_indexes` in production first. The exact duplicates (I6, I14, the order_items/addons pair, the tag, invoice and oauth pairs) are safe to drop first.

### 2.4 Unique indexes on brand-owned tables that omit `brand_id` (I21; 46, live)

Source: `scratchpad/database/uniq_nobrand.txt`. The table classifies each one:

| Class | Unique keys | Assessment |
|---|---|---|
| Scoped by a brand-owned parent (tenant-safe) | stores/warehouses mappings `(store_id, warehouse_id, service_types)`, `operating_hours (scope_type, scope_id, …)`, `customer_devices (customer_id, device_id)`, `price_list_items (price_list_id, …)`, `pickup_requests (customer_id, idempotency_key)`, `delivery_slots (store_id, …)`, `rider_assignments (rider_id, shift_date, shift_start)`, `wallet_accounts (customer_id)`, `cash_books (store_id, book_date, shift_label)`, `royalty_invoices (franchise_id, period_start, period_end)`, `notification_preferences (user_id/customer_id, category)`, `customer_identities (customer_id, provider)`, `invoices (order_id)`, subscription-invoice period keys, `franchise_subscriptions (franchise_id) WHERE active-ish`, `rider_ratings`, `rider_incentive_awards`, `brand_platform_invoice (subscription_id, billing_period_start)` | OK. The parent id is itself brand-owned |
| **Generated business numbers (collide across tenants)** | `franchise_agreements.agreement_number`, `pickup_requests.request_number`, `fulfillment_unit.tag_code`, `fulfillment_unit_tags.tag_code`, `warehouse_batches.batch_number`, `payments.payment_number`, `payment_refunds.refund_number`, `expenses.expense_number`, `royalty_invoices.invoice_number`, `subscription_invoices.invoice_number`, `franchise_subscription_invoices.invoice_number`, `orders (order_number, created_at)` | **Risk.** Collisions reproduced for expenses (08b) and batches (10c T8) (SA-DB-007, SA-QC-002) |
| **Global idempotency keys** | `outbox_events.idempotency_key`, `payments.idempotency_key`, `payment_refunds.idempotency_key`, `wallet_transactions.idempotency_key`, `notifications_outbox.idempotency_key`, `subscription_billing_attempts.idempotency_key` | Risk where the key is client-supplied (I9, I13). Server-derived keys (`topup_{paymentId}`, `evt:{eventId}:ch:…`) are safe in practice |
| Global by design (review) | `customers.referral_code`, `riders.user_id`, `brand_domains.domain` (also covers **unverified** rows; SA-TEN-012), `push_tokens.token`, `whatsapp_message_log.wa_message_id`, `oauth_authorization_codes.code_hash`, `api_keys.key_prefix`, `authz.policy (key, version)` | Mostly intentional. `brand_domains` enables domain squatting by an unverified claim (SA-TEN-012, a dup of SA-DB-007) |

### 2.5 Production verification still required (read-only, on production)

The following must be run against the production database. **None of it has been done**, and no output from these queries exists in this audit:
- `pg_stat_user_indexes` (`idx_scan`, size): needed before dropping any of the 55 redundant indexes, and to rank the 69 FK indexes.
- `pg_stat_statements` top queries: needed to confirm E1, E2, E6 and E3 at production scale.
- `EXPLAIN (ANALYZE, BUFFERS)` of the webhook lookup, the outbox poll and the order idempotency lookup, run under `app_user` with real GUCs.

---

## 3. Idempotency matrix

Classification: **TI** = truly idempotent (DB-enforced under concurrency) · **SD** = sequential-duplicate-only (a check-then-act protects sequential retries, not concurrent requests) · **UP** = unprotected. The QA reconciliation is applied in the *classification* column.

Two cross-cutting facts apply to every row:
- **No optimistic concurrency token exists anywhere.** A grep for `IsConcurrencyToken|IsRowVersion|xmin|ConcurrencyCheck|DbUpdateConcurrencyException` finds nothing outside tests ([SA-API-005](../../FINDINGS.md#sa-api-005)).
- **`SELECT … FOR UPDATE` appears once**, in the partner wallet (`CommerceDbContext.cs:111-125`).

| operation | entry point | existing protection (app) | DB enforcement | concurrency behaviour | retry behaviour | verification status | classification (after QA) | race conditions | remediation (canonical ID, final severity, phase) |
|---|---|---|---|---|---|---|---|---|---|
| **Payment: Razorpay order create** (`InitiatePayment`) | `CustomerPaymentHandlers.cs:35-86` | lookup by (key, brand, customer) | global UNIQUE `payments.idempotency_key` | The 2nd concurrent request calls `CreateOrderAsync` at the gateway, then hits 23505 → 409 | same key → existing payment | Partially Verified (code) | **SD** (DB backstop on the row; the external side effect is duplicated) | an orphan Razorpay order per race; client-supplied amount and order (SA-API-007) | Insert a `pending` row first (claim on the unique key), then call the gateway; brand-scope the key (I9). [SA-API-007](../../FINDINGS.md#sa-api-007) High P0 |
| **Payment verify** (client) | `VerifyPaymentHandler` (`CustomerPaymentHandlers.cs:~130-177`) | `status == 'pending'` check | none | Verify and webhook can both see `pending`, and both write `captured` plus an outbox event | a sequential 2nd call → "already captured" | Partially Verified (code) | **SD** | duplicate `payment.captured`; a signature mismatch sets `failed`, making a later capture be ignored (SA-API-007) | Guarded `UPDATE … SET status='captured' WHERE id=@id AND status IN ('pending','failed')`, rows==1; one shared "on captured" routine. SA-API-004 group (SA-DB-011 dup) / SA-API-007 |
| **Webhook: Razorpay captured / failed** | `POST /api/v1/webhooks/razorpay` → `RazorpayWebhookHandler.cs:70-310` (bypass, per-brand HMAC) | state check only; no event-id dedupe | none; `gateway_order_id` unindexed (I7) | concurrent duplicates both pass the check and both emit (SA-SUB-016) | redelivery after commit → no-op | Verified (code); race **Not Tested** | **SD** | double outbox events → double notifications; cross-tenant seq scan (E1) | `webhook_events(event_id PK)` insert-first, or a guarded update; UNIQUE `(gateway, gateway_order_id)`. [SA-SUB-016](../../FINDINGS.md#sa-sub-016) Low P1; SA-API-004 group |
| **Webhook: paylink (brand invoice / partner top-up)** | `ProcessPaylinkWebhook.cs:55-77`; `ProcessPartnerPaylinkWebhook.cs:91-113` | status check; partner key from notes | partner: UNIQUE `(partner_id, idempotency_key)`; brand invoice: none | brand invoice: idempotent end state; partner: unique key | safe | Verified (code) | brand invoice **SD (harmless)**; partner **TI** | the brand handler does not compare the amount; it ignores `past_due` invoices | Accept `issued` or `past_due` and verify the amount. [SA-SUB-002](../../FINDINGS.md#sa-sub-002) High P2; SA-SUB-016 |
| **Refund (admin)** | `POST /api/v1/admin/payments/refunds` → `AdminPaymentHandlers.cs:77-231` | key lookup plus an app SUM cap **outside** the transaction (`:100-118`); gateway refund **inside** the retried transaction and **before** the INSERT (`:148-230`) | partial UNIQUE key; cap trigger `payment_idempotency.sql:29-80` (SECURITY INVOKER, no lock) | **Reproduced twice:** two concurrent 60.00 refunds on 100.00 both committed (120.00) | same key → existing row; **a transient DB failure re-runs the lambda and calls Razorpay again** | **Verified** (08b + 10c T5); retry path Verified (code) | key: TI on the row · **cap: UP** · gateway call: **UP** | over-refund; **QC-001:** documented `refundType` `"gateway"`/`"wallet"` violates the CHECK, so Razorpay has refunded while the DB rolls back and leaves no row (T7d). Today reachable only by platform admins, because brand staff on the commerce host cannot see the payment (SA-TEN-001) | Insert as `processing` and commit; call the gateway outside the transaction with a deterministic reference; `FOR UPDATE` on the payment in the trigger and handler; split `RefundMethod`/`RefundType`. [SA-API-009](../../FINDINGS.md#sa-api-009) High P0 (dup SA-DB-006); [SA-QC-001](../../FINDINGS.md#sa-qc-001) High P0 |
| **Cancellation refund** | `OrderCancellationRefund.cs:16-80` | key `cancel_refund_{orderId}` + AnyAsync | partial UNIQUE | 2nd insert → 23505 | idempotent | Verified (code) | **TI (row)**; cap racy as above | **The queued refund is never executed** | An idempotent refund executor (worker plus inbox marker). [SA-API-008](../../FINDINGS.md#sa-api-008) High P0 |
| **POS / customer / admin order creation** | `CreateOrderCommand.cs:62-95, 757-803`; POS sends `Idempotency-Key` (`pos-web/src/api/orders.ts:38-50`) | `metadata @> {"idempotency_key"}` lookup | **none** | Two concurrent same-key requests create **two orders**, two coupon redemptions, double loyalty burn and double package debit | sequential retry → existing order; a key containing `"` → 422 | Verified (code); race **Not Tested** | **SD** | POS double-tap; axios retry after a 401 refresh | `idempotency_key` column + partial UNIQUE `(brand_id, idempotency_key)`; catch 23505 and return the winner (copy the pickup pattern); serialise metadata with `JsonSerializer`. [SA-API-004](../../FINDINGS.md#sa-api-004) High P1 (dup SA-DB-011, orig Medium) |
| **Order number** | `order_lifecycle.next_order_number()` | atomic `INSERT … ON CONFLICT DO UPDATE … RETURNING` | per (brand, store, year) PK | serialised by the row lock | n/a | Verified | **TI** (generator) | — | — (reference pattern for every other number generator) |
| **Order status transition** | `UpdateOrderStatusCommand.cs:43-64`; rider path `UpdateMyTaskStatus.cs:152-176` | in-memory `EnsureTransition`; the rider path skips it | none (no `WHERE status=@from`) | cancel racing complete: last write wins | — | Verified (code) | **UP** | delivered-after-cancelled; a refund queued for a delivered order | `UPDATE … WHERE id=@id AND status=@from`, 0 rows → 409. [SA-SOLID-001](../../FINDINGS.md#sa-solid-001) High P3 (dup SA-API-006); [SA-MOB-001](../../FINDINGS.md#sa-mob-001) High P0 |
| **Customer pickup scheduling + slot reservation** | `POST /orders/api/v1/customer/pickup-requests` → `PickupCommands.cs:338-510` | fast-path lookup when a key is supplied; transaction with atomic slot `UPDATE … booked_count < capacity`; 23505 caught by constraint name | partial UNIQUE `(customer_id, idempotency_key)`; CHECK `booked_count <= capacity` | the loser rolls back, including its slot increment | returns the winner | Verified (code + live constraint) | **QA reconciliation:** **TI only when a key is sent.** The customer app sends **no** key (`customer-mobile/src/api/orders.ts:128-136`), so effectively **UP** for retries. In the production role configuration the INSERT is **denied** by 0031 (10c T1) | duplicate pickups and extra slot units on retry; the address is not ownership-checked (cross-customer and cross-brand accepted at DB level, T7b) | Client sends `Idempotency-Key`; fix SA-TEN-001 **in the same release** as the address check. [SA-API-018](../../FINDINGS.md#sa-api-018) Medium P1 (dup SA-MOB-018); [SA-MOB-005](../../FINDINGS.md#sa-mob-005) Medium P0; [SA-TEN-001](../../FINDINGS.md#sa-ten-001) |
| **Admin / POS pickup creation** | `PickupCommands.cs:38-50,104`; reject at `:657-672` | none for capacity | CHECK only on the increments that do happen | no increment at all | — | Verified (code) | **UP** (capacity) | overbooking; rejecting decrements capacity that was never taken | Use the same atomic increment as the customer path. [SA-API-019](../../FINDINGS.md#sa-api-019) Medium P1 |
| **Pickup reschedule** | `CustomerPickupCommands.cs:179-244, 367-382` | atomic slot decrement and increment | CHECK | atomic | — | Verified (code) | **TI** (capacity) | — | — |
| **Slot booking link** (`delivery_slot_bookings`) | `PickupCommands.cs:448-458` | link by (slot, customer, `pickup_request_id IS NULL`) | no unique | a concurrent double booking by one customer can mis-link | — | Partially Verified | **SD** | minor | Partial UNIQUE `(pickup_request_id) WHERE status='active'` (I16) |
| **Rider assignment** (manual, auto-dispatch) | `PickupCommands.cs:204-279`; `DeliveryAssignmentCommands.cs:29-107`; `AutoDispatchService.cs:313-315,396-400` | none: no `pr.Status` check, no existing-leg check; `AnyAsync` in auto-dispatch | **none** (10c T11) | two live legs for one pickup; assigning a **cancelled or completed** pickup revives it | a sequential repeat also double-assigns | **Verified (manual paths, deterministic)**; races Suspected | **UP** | manual + manual, auto + manual, two worker replicas | Partial UNIQUE on live legs; status check; map 23505 → 409; a reassign command. [SA-MOB-002](../../FINDINGS.md#sa-mob-002) High P1 (dup SA-DB-022, orig Low) |
| **Rider offer accept / expiry** | `OfferActions.cs:39-96` (the "taken" check at `:59-71` runs outside the transaction); `AutoDispatchService.cs:461-477` expiry sweep | status check | none | two riders can both accept; expire can overwrite an accept | — | Partially Verified | **SD** | accept vs accept; accept vs expire | `UPDATE … SET status='accepted' WHERE id=@id AND status='offered' AND offer_expires_at > now()` plus the partial unique. SA-MOB-002 |
| **Rider task status** | `UpdateMyTaskStatus.cs:34-42,96,152-176,263-264` | allow-list only | CHECK on the vocabulary only (T11) | — | a repeated `completed` decrements rider load on every call | Verified (code) | **UP** | — | Leg state machine with a conditional update. [SA-MOB-001](../../FINDINGS.md#sa-mob-001) High P0 |
| **Business numbers** (PKP-/EXP-/WB-/tags/AGR-) | see I18–I20 | `COUNT(*)+1` | global UNIQUE | concurrent creates in a brand collide; cross-brand collisions **reproduced** (expenses) and **intra-brand under 0031 reproduced** (batches, T8) | a retry fails again the same day (the count does not change) | Verified | **UP** | DoS of back-office creation; existence oracle | Per-brand counter function (upsert pattern) plus `(brand_id, number)` uniques. [SA-DB-007](../../FINDINGS.md#sa-db-007) Medium P3; [SA-QC-002](../../FINDINGS.md#sa-qc-002) Medium P1 |
| **Wallet top-up credit** | `CustomerWalletHandlers.cs:128-212` | lookup `topup_{paymentId}` | global UNIQUE `idempotency_key`; rolls back the balance change too | 2nd concurrent request → 23505, no double credit | idempotent | Verified (code + constraint) | **TI** (credit row) | only `/verify` credits, not the webhook (SA-API-007) | — |
| **Wallet balance mutation** (top-up, adjust, refund-to-wallet) | `CustomerWalletHandlers.cs:180-181`; `AdminWalletHandlers.cs:~105-172`; `AdminPaymentHandlers.cs:~180-215` | read → compute in C# → `UPDATE … SET balance=<value>` | CHECK `balance >= 0` only; `version` is not a concurrency token | **lost update reproduced** (08b: +50/−30 on 100 → 150; 10c T6: → 70; correct is 120) | — | **Verified** (10c T6) | **UP** | silent balance corruption; ledger `BalanceAfter` wrong | `UPDATE … SET balance = balance + @d WHERE id=@id AND balance + @d >= 0 RETURNING balance`, or xmin token + 409. [SA-API-005](../../FINDINGS.md#sa-api-005) High P1 (dup SA-DB-008) |
| **Partner wallet debit / credit** | `PartnerWalletHandlers.cs:230-310` + `CommerceDbContext.cs:111-125` | lookup + `FOR UPDATE` | UNIQUE `(partner_id, idempotency_key)` | serialised | idempotent | Verified | **TI** | — | — (reference pattern) |
| **Partner-booking debit** (inbox consumer) | `PartnerBookingDebitService.cs:103-150` | anti-join on `outbox_consumed_events` | PK (consumer, event_id) + debit key | duplicates absorbed by the debit key | redelivery-safe | Verified | **TI** | — | — (reference pattern for every consumer) |
| **Coupon redemption** | `CustomerCouponHandlers.cs:66-152`; `CreateOrderCommand.cs:276-330,757-771` | app checks (global, per-customer, one per order), then `CurrentUsageCount++` | none (I12) | lost update on `current_usage_count`; limits exceeded concurrently | a sequential retry is blocked by the app check | Partially Verified (no DB guard exists to test) | **SD** | over-redemption | Guarded atomic UPDATE + partial uniques. [SA-DB-010](../../FINDINGS.md#sa-db-010) Medium P1 |
| **Loyalty burn / package debit / promotion counters** (order create) | `CreateOrderCommand.cs:388-415,485,771-797` | read-modify-write; `Version++` unchecked | none (append-only ledgers, no balance guard) | lost update | — | Verified (code); race **Not Tested** | **UP** | balance drifts from the ledger | Atomic or xmin as above. SA-API-005 |
| **Loyalty earn** (worker) | `LoyaltyEarnService.cs:95-115` | strict `OccurredAt >` watermark | unique ledger `(order_id, transaction_type, brand_id)` | duplicate earn blocked by the unique key | — | Verified (code; acknowledged in-code) | **TI** (no double earn) but **events can be skipped** | ties and late commits skipped permanently | Inbox pattern (`outbox_consumed_events`). [SA-API-015](../../FINDINGS.md#sa-api-015) Medium P1 |
| **Customer subscription invoice issuance** | `SubscriptionBillingService.cs:~125-275` | AnyAsync lookup | UNIQUE (subscription_id, billing_period_start) | 2nd insert → 23505 | idempotent | Verified (live constraint) | **TI** | — | — |
| **Brand (SaaS) renewal invoice** | `BrandPlatformBillingService.cs:62-69` | AnyAsync + unique `(subscription_id, billing_period_start)` | UNIQUE | — | — | Partially Verified (code + AsyncLocal semantics) | TI on the row, **but the renewal pass sees 0 rows** under `app_user` (plain `CreateAsyncScope`, no worker bypass) | no renewal is ever issued | `CreateWorkerAsyncScope()`. [SA-SUB-004](../../FINDINGS.md#sa-sub-004) High P2 (dup SA-API-010) |
| **Subscription renewal charge (mandate)** | `SubscriptionBillingService.cs:343-383` → `GatewaySubscriptionCharger.cs:25-38` (`Razorpay-Idempotency` header) | key per invoice + attempt | UNIQUE attempt key, but inserted **after** the charge (I26) | two worker replicas both charge, then one insert fails | depends on Razorpay honouring the header (**Not Verified**); endpoint and header themselves Suspected (SA-SUB-015) | Partially Verified (code) | **SD single-replica / UP across replicas** | double debit | Claim row first (`initiated`); single-runner lease or advisory lock; sandbox contract test. [SA-DB-009](../../FINDINGS.md#sa-db-009) Medium P1; [SA-SUB-015](../../FINDINGS.md#sa-sub-015) Medium P2 |
| **Notification enqueue** | `NotificationMappingService.cs:284-322` | AnyAsync + key `evt:{eventId}:ch:{channel}` | UNIQUE | 23505 on race | idempotent | Verified (live) | **TI per event** | duplicate business events (e.g. double `payment.captured`) still produce duplicate sends | Dedupe events at source (see the capture rows) |
| **Notification mapping** (watermark consumer) | `NotificationMappingService.cs:119-178` | cursor `(occurred_at, id)`; `occurred_at` is app `UtcNow`, not commit time; a failed event is skipped at `:164-171` | none | — | — | Verified (code; acknowledged in-code) | **UP for completeness** (events skipped, not duplicated) | late commits skipped permanently | Inbox pattern. [SA-API-015](../../FINDINGS.md#sa-api-015) Medium P1 |
| **Notification dispatch** | `NotificationDispatcherService.cs:105-200,314-336` | re-read, then `status='sending'` with no row lock | UNIQUE enqueue key only | two replicas can both send | a row stuck in `sending` after a crash is never reclaimed | Partially Verified (code); not reproduced | **SD** (single instance only) | duplicate SMS/WhatsApp; silent loss | `UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED LIMIT n) RETURNING *` plus a lease timeout; a single worker host. [SA-OPS-005](../../FINDINGS.md#sa-ops-005) Medium P1 (dups SA-API-014, SA-ARCH-007); SA-DB-009 |
| **Outbox relay** | `OutboxEventRelayService.cs:74-200` | SELECT, then `UPDATE status='publishing'` **WHERE id only** | none | **Reproduced (08b): two sessions both claimed the same event** | crash after claim → `publishing` forever (no reaper) | **Verified** | **SD**, plus a defect | **QA-added defect:** when the in-transaction re-check finds the row already claimed (`current is null`), `ProcessEventAsync` **still publishes** (`:120-137` run unconditionally), so a second relay republishes **even without a race**. Today the publisher is `LoggingEventPublisher` only, so there is no external broker effect yet | Conditional claim or SKIP LOCKED; return before publishing when the claim fails; lease reaper; poll index (I22). [SA-DB-009](../../FINDINGS.md#sa-db-009) Medium P1 |
| **Refresh-token rotation** | `RefreshTokenHandler.cs:43-112` | read, set `RevokedAt` in memory, insert child | none | two concurrent refreshes both succeed | reuse detection can revoke the family on near-simultaneous tabs | Verified (code); race **Not Tested** | **SD** | defeats single-use rotation | `UPDATE … SET revoked_at=now() WHERE id=@id AND revoked_at IS NULL`, 0 rows = reuse. [SA-API-020](../../FINDINGS.md#sa-api-020) Medium P1 |
| **Retention purge** | `RetentionSweepService.cs:200-250` → `kernel.purge_brand` | per-brand transaction | — | single instance assumed | rerun-safe | Partially Verified | **TI** (delete) | depends on the accidental `app_user` EXECUTE grant (SA-DB-003) | Dedicated maintenance role **before** revoking. [SA-DB-003](../../FINDINGS.md#sa-db-003) Medium P0 |

**Tally (after QA).**
- **TI:** order-number generator, pickup scheduling **with a key**, reschedule capacity, partner wallet, partner-booking inbox, subscription invoice issuance, wallet top-up credit, notification enqueue, cancellation-refund row, loyalty-earn row.
- **SD:** payment create and verify, webhooks, order creation, coupon redemption, offer accept, outbox and notification claims, refresh rotation, mandate charge (single replica).
- **UP:** refund cap and refund gateway call, wallet and loyalty balances, order status transitions, rider assignment, business-number generators, admin pickup capacity, customer pickups as actually called (no key).

The architect ([01b §2.1](specialists/01b-architect-challenge-review.md)) summarises this as "Supported only for pickup booking and partner wallet; Not Supported for money and dispatch paths".

---

## 4. RLS coverage matrix

### 4.1 Coverage at a glance (live catalog, 08b; consistent with 10c)

| Metric | Value |
|---|---|
| Logical tables | 162 (14 schemas) |
| Tables with RLS enabled | 136 |
| Policies | 190 |
| Tables with `brand_id` | 126, **all** with RLS and at least one policy |
| Tables carrying the RESTRICTIVE `rls_subbrand_scope` (0031) | 39 (`db/migrations/0031_subbrand_scope_rls.up.sql:139-177`; `AS RESTRICTIVE FOR ALL TO app_user`) |
| Non-brand tables with RLS | 10 (partner tables, authz) |
| Identity tables with policies but **RLS disabled** (inert) | 8 |
| `FORCE ROW LEVEL SECURITY` | 5 tables (`api_keys`, `api_key_usage`, `impersonation_grants`, `brand_cancellations`, `onboarding_progress`). This only matters for owners; every table is owned by `postgres`, so it has no effect on `app_user` |
| Effective runtime role | **`app_user`**: `rolsuper=f`, `rolbypassrls=f`, owns 0 tables, no TRUNCATE/TRIGGER/CREATE (live). `deploy/.env.example:4-5`, `AppHost.cs:22-24`. **No startup assertion** enforces this in production (02) |
| Coverage guard | Migration 0027 aborts if a `brand_id` table lacks RLS (`0027…up.sql:74-95`) |

### 4.2 Matrix by table group

"Ops" lists the commands covered by the policies. Permissive policies are OR-ed; the RESTRICTIVE policy is AND-ed. "Role" is the policy's `TO` role: `public` means the policy applies to every role, including `app_user`.

| Group (tables) | RLS / forced | Policies (permissive; + restrictive) | Ops | Role | Bypass risks | Live test results (as `app_user`) |
|---|---|---|---|---|---|---|
| **order_lifecycle core**: orders (partitioned), order_items, pickup_requests, delivery_slots, delivery_slot_bookings, delivery_assignments, delivery_schedules, order/invoice_number_sequences | on / no | `rls_brand`: `rls_bypass() OR brand_id = current_brand_id()` (public variant on schedules and sequences) **+ RESTRICTIVE** `rls_subbrand_scope`: `rls_bypass() OR within_scope_cols(brand, franchise, store, warehouse)`, applied in USING and WITH CHECK | ALL | app_user / public | No composite FKs (T7a); self-set bypass (T3, S5) | A sees its rows, B invisible; insert of a B row denied; UPDATE/DELETE on B = 0 rows. **Customer (scope `'?'`): own orders 0, INSERT order and pickup denied** (08b; 10a S1; 10c T1). Ops-host staff with `brand:A`: orders 2 (S4) |
| order_lifecycle other: order_addons, order_notes, order_status_history, invoices | on / no | `rls_brand` (+ inert `rls_admin_only` OR-ed on addons) | ALL | app_user / public | as above | brand isolation OK (generic test) |
| **commerce customer-owned**: payments, wallet_accounts/transactions, payment_refunds, coupon_redemptions, customer_packages, loyalty_points_ledger, package_usage_ledger | on / no | `rls_brand_or_customer`: `bypass OR (brand = cur AND (cur_customer IS NULL OR customer_id = cur_customer))`; **payments only** also has the RESTRICTIVE policy | ALL | app_user | **The commerce host never sets `app.current_customer_id`**, so the customer arm is NULL and the policy degrades to brand-only (SA-TEN-002) | payments A/B isolation OK. **Commerce-host customer: `wallet_accounts` = 2 (c1 and c2)** (10c T1b). Pre-0031 payments: 2 (10a S2b). Post-0031 payments: 0 for customers (S2) and **commerce-host staff** (S3, T1b) |
| commerce brand catalogue and billing: coupons, packages, promotions, payment_methods, loyalty_programs, payment_mandates, subscription_* (legacy-named `custsub_/subinv_/subplan_/mandate_tenant` using kernel helpers) | on / no | brand only | ALL | app_user | — | covered by the generic test |
| **partner (RaaS)**: commerce.partner_wallet_accounts/transactions, partner_invoices; logistics.partners, partner_users, partner_bookings; logistics.partner_dispatches | on / no | `rls_partner`: `bypass OR partner_id = current_partner_id()`; dispatches `rls_partner_or_brand` | ALL | app_user | `AssignPartnerDispatch.cs:84,120` uses `SET LOCAL app.bypass_rls` in its transaction; a single (RLS-only) layer ([SA-AUTHZ-015](../../FINDINGS.md#sa-authz-015), Low) | **Not Tested** live. `Partner*RlsTests.cs` (17) exist but were not run |
| **customer_catalog** (17): customers, customer_addresses, devices, items, services, price_lists, … | on / no | `rls_brand` (+ restrictive on price_lists) | ALL | app_user / public | **customer-vs-customer is app-only** (brand-only policy) | **Customer c1 sees c2's address** (10c T1: `customer_addresses` = 2) |
| customer_catalog.customer_identities | on / no | **legacy `custident_tenant`**: `current_setting('app.bypass_rls')='true' OR brand_id = current_setting('app.current_brand_id', true)::uuid` (`0001_…up.sql:49-54`) | ALL | **public** | raw cast | **ERROR `invalid input syntax for type uuid: ""`** with brand `''` **even with bypass `'true'`**; `stores` under the same GUCs returned rows (08b; 10c T7c) → Google customer sign-in fails ([SA-DB-012](../../FINDINGS.md#sa-db-012), High P0) |
| **tenancy_org**: franchises, stores, warehouses, territories, holidays, operating_hours, store_warehouse_mappings, brand_domains, brand_cancellations (F), onboarding_progress (F), franchise_agreements | on / 2 forced | `rls_brand` (+ restrictive on stores, warehouses, mappings) | ALL | app_user / public | no composite FK (a B store is accepted on an A order) | A/B stores isolation OK; customer and commerce-host staff see 0 stores (T1) |
| tenancy_org.brands, platforms | on / no | `rls_admin_only`: `rls_bypass()` | ALL | app_user | tenants read their own brand only through SECURITY DEFINER helpers (`brand_status`, `resolve_brand_domain`, …) ([SA-TEN-007](../../FINDINGS.md#sa-ten-007)) | — |
| **finance_royalty** (12) | on / no | `rls_brand` or legacy-named helper policies (`fransub_tenant`, `fransubinv_tenant`); restrictive on 10 | ALL | app_user | scope-blind number generators (SA-QC-002) | expense number collision (SA-DB-007). Finance is dead for commerce-host staff under 0031 (10c note on SA-DB-001) |
| **laundry_fulfillment** (11) | on / no | `rls_brand` (+ restrictive on 7) | ALL | app_user | — | batch collision under 0031 (T8) |
| **logistics riders**: riders, rider_* (9), rider_location_pings (partitioned) | on / no | `rls_brand` (+ restrictive on 5); many `public` | ALL | app_user / public | rider-vs-rider isolation is app-only; `riders` has no `store_id` column, so the policy cannot narrow by store (10c location table) | — |
| **engagement_cms** (11 of 13) | on / no | `rls_brand` / `rls_brand_or_customer` | ALL | app_user / public | `notification_event_catalog` and `notification_event_cursors` have no RLS (global, no brand column). Anonymous public content returns nothing under enforced RLS ([SA-TEN-006](../../FINDINGS.md#sa-ten-006), Medium P4; SQL analogue reproduced by 02) | — |
| kernel.system_settings, feature_flags | on / no | `rls_brand_or_platform`: USING `bypass OR brand_id IS NULL OR brand = cur`; WITH CHECK `bypass OR brand = cur`. **system_settings is also in the 0031 restrictive list** | ALL | app_user | — | tenants read platform rows but cannot write them (02) |
| kernel.file_attachments, outbox_events | on / no | `rls_brand` | ALL | app_user | workers run with bypass | — |
| kernel.outbox_consumed_events | **off** | none | — | — | no brand column; worker-only | — |
| **identity_access.users** | on / no | select/update/delete: `bypass OR id = cur_user OR user_in_brand(id, cur_brand)` (SECURITY DEFINER helper); **INSERT WITH CHECK `true`** (`0029…up.sql:122`) | split per command | app_user | any session can insert a user, **including `user_type='platform_admin'`** (10a S6, the DB step of [SA-AUTHZ-001](../../FINDINGS.md#sa-authz-001)) | S6: `INSERT 0 1` from a brand-B session |
| identity_access.roles | on / no | split per command; select includes `brand_id IS NULL` | split | app_user | an FK to a role ignores RLS | B role invisible to A, **but A can reference it in a membership** (T4) |
| identity_access.audit_logs (partitioned), api_keys (F), api_key_usage (F), impersonation_grants (F), brand_feature, brand_platform_subscription / invoice, oauth_authorization_codes | on / 3 forced | `rls_brand` (+ restrictive on audit_logs) | ALL | app_user / public | **audited writes from customer and commerce-host sessions fail** under the restrictive policy (scope `'?'`) | — |
| identity_access.user_permission_override | on / no | `rls_user_self` | ALL | public | — | — |
| **identity_access.user_scope_memberships, user_profiles, login_history, otp_codes, refresh_tokens, password_resets, permissions, role_permissions** | **off** | policies exist but **inert** (`rls_user_self` / `rls_admin_only`); memberships left off deliberately (`0029…up.sql:147-155`) | — | — | any `app_user` session reads every tenant's memberships, profiles (PII), token/OTP hashes and login history, and can write cross-tenant memberships | **Reproduced:** an A session inserted a membership granting a B role (08b; 10c T4); `otp_codes` and `refresh_tokens` SELECT-able ([SA-DB-005](../../FINDINGS.md#sa-db-005), High P1) |
| identity_access catalogues (features, modules, module_bundle, bundle_feature, permission_groups, role_presets, vertical_templates/terms, oauth_clients) | off | none | — | — | global reference data, but **`app_user` has write grants** | — |
| **authz.policy** | on / no | `rls_brand_or_platform` | ALL | app_user | **the ABAC store reads through a separate `NpgsqlDataSource` with no GUCs** (`AbacServiceCollectionExtensions.cs:33-37`; `NpgsqlAbacStore.cs:40-48`) | 58 platform policies visible, **0 brand policies**; `role_codes={}` (08b; 10c T10) ([SA-DB-014](../../FINDINGS.md#sa-db-014), Medium P1) |
| authz.policy_condition | on / no | `rls_via_policy` (EXISTS on the parent) | ALL | app_user | — | — |
| authz.decision_log (partitioned) | on / no | `rls_brand` (WITH CHECK) | ALL | app_user | **`COPY FROM` is rejected under RLS** (`DecisionLogWriter.cs:125-140`) | `ERROR: COPY FROM not supported with row-level security` (T10) |
| authz.action / attribute / resource_type | off | — | — | — | global; writes should be revoked | — |
| **salon_fulfillment** (4) | on / no | legacy raw-cast `*_tenant` (public) + restrictive | ALL | public | `app_user` has **no USAGE** on the schema; unused by the backend (0 C# references) | `permission denied for schema salon_fulfillment` ([SA-DB-021](../../FINDINGS.md#sa-db-021), Low P5; raw cast also SA-DB-012) |
| **analytics** (7 materialized views) | n/a: MVs cannot have RLS | — | SELECT | app_user | isolation is app-only (`GetDailyStoreRevenue.cs:29-33`); a global refresh is triggerable by any `analytics.refresh` holder | **A session read B rows**: 76 + 76 (08b); A 2 / B 1 on `mv_customer_ltv` (10c T9) ([SA-DB-013](../../FINDINGS.md#sa-db-013), Medium P3; dup SA-TEN-013) |
| **SECURITY DEFINER functions** (22, all owned by superuser) | — | — | EXECUTE | **all executable by `app_user`**, via `pg_default_acl` on kernel (10c C8), contrary to `0015…up.sql:248-250` "NOT granted to app_user" | `purge_brand`, `export_brand`, `set_brand_suspension`, `set_brand_cancellation_state`, `ensure_brand_subdomain`, `request_impersonation`, … trust their argument | **`export_brand(B)` from an A session returned B's data; `purge_brand(B)` deleted every B row (rolled back)** (08b; 10c T2). `set_brand_cancellation_state(B, …)` from A cancelled B (10a S7b) ([SA-DB-003](../../FINDINGS.md#sa-db-003), Medium P0) |

### 4.3 Principal × host lane matrix (where RLS actually lands today)

| Principal and host | GUCs as emitted | Restrictive tables (39) | `rls_brand_or_customer` tables | Brand-only tables | Evidence |
|---|---|---|---|---|---|
| Staff, core/ops host | `scope_nodes = brand:A` (or narrower), all subject GUCs | brand/scope-correct | brand (customer arm NULL, which is correct for staff) | brand | 10a S4 (payments 2, orders 2); 10c T1b |
| **Staff, commerce host** | `CommerceHostCurrentTenant` has no `ScopeNodes` → `'?'` | **0 rows; writes and audited writes denied** | payments 0 (restrictive); wallets brand-wide | visible | 10a S3; 10c T1b (payments 0 vs 2 on the ops host). SA-TEN-001 |
| **Customer, core/ops host** | `customer_id = c1`, `scope_nodes = '?'` | **0 rows; INSERT order and pickup denied** | own rows only (on tables without the restrictive policy) | **brand-wide** (c2's address visible) | 10a S1; 10c T1. SA-TEN-001 |
| **Customer, commerce host** | `customer_id = ''`, `user_id = c1`, `scope_nodes = '?'` | payments 0 | **brand-wide** (c1 sees c2's wallet) | brand-wide | 10a S2/S2b; 10c T1b. SA-TEN-002 |
| API-key principal | no `scope_nodes` (`ApiKeyAuthentication.cs:181-189`) | 0 rows (by trace) | — | — | **Not Tested** live; static trace in 02 |
| Partner | `partner_id`; `rls_partner` | n/a | n/a | n/a | **Not Tested** live |
| Platform admin | `bypass_rls` on **every** request (`TenantResolutionMiddleware.cs:32-48`) | bypassed | bypassed | bypassed | no DB backstop; WITH CHECK never applies (SA-TEN-007) |
| Commerce workers | bypass inside a positively marked `WorkerScope` | bypassed | bypassed | bypassed | fail-closed without the marker; the renewal pass lacks it (SA-SUB-004) |
| Anonymous auth / signup / webhooks | `bypass_rls = 'true'`, brand `''` | bypassed | bypassed | bypassed, **except the raw-cast policies, which throw** | 10c T7c (SA-DB-012) |
| Raw `NpgsqlDataSource` (ABAC store, `BrandExportService`) | **no GUCs at all** | 0 rows | 0 rows | 0 rows | fails closed (10c T10). `BrandExportService.cs:24-26` is safe only because `export_brand` is DEFINER (02) |
| `app_user` session that sets `app.bypass_rls` itself | `set_config('app.bypass_rls','true',false)` | **all brands** | **all brands** | **all brands** | 10a S5 (orders 0 → 2 of brand A); 10c T3 (`DELETE` of B's payment → `DELETE 1`). SA-TEN-007 |

### 4.4 Bypass routes (DB-level) and their status

| Route | Mechanism | Reproduced? | Canonical (final) |
|---|---|---|---|
| Self-settable bypass GUC | `kernel.rls_bypass()` reads the plain custom GUC `app.bypass_rls` | Yes (S5, T3) | [SA-TEN-007](../../FINDINGS.md#sa-ten-007) Medium P1 (dup SA-DB-015) |
| SECURITY DEFINER functions | 22 superuser-owned, executable by `app_user`, no `p_brand_id = current_brand_id()` check | Yes (T2, S7b) | [SA-DB-003](../../FINDINGS.md#sa-db-003) Medium P0 (orig High; QA: no HTTP path passes a foreign brand, and the GUC already gives the same power) |
| Identity tables without RLS | 8 tables with RLS off; users INSERT `WITH CHECK (true)` | Yes (T4, S6) | [SA-DB-005](../../FINDINGS.md#sa-db-005) High P1; app vector [SA-AUTHZ-003](../../FINDINGS.md#sa-authz-003) High P0 |
| Materialized views | no RLS possible; SELECT granted | Yes (T9) | [SA-DB-013](../../FINDINGS.md#sa-db-013) Medium P3 |
| Raw `NpgsqlDataSource` paths | no interceptor, so no GUCs | Yes (T10): fails closed today | [SA-DB-014](../../FINDINGS.md#sa-db-014) Medium P1 |
| Cross-tenant FK references | FK checks run as the owner and ignore RLS | Yes (T7a, T7b) | [SA-DB-004](../../FINDINGS.md#sa-db-004) Medium P3 (orig High) |
| Platform-admin blanket bypass | every request bypasses; RLS has no role in platform paths | Verified (code) | SA-TEN-007 |
| Platform-admin minting | tenant API can create `platform_admin`, which then bypasses everything | DB step reproduced (S6); chain static | [SA-AUTHZ-001](../../FINDINGS.md#sa-authz-001) Critical P0; [SA-ARCH-013](../../FINDINGS.md#sa-arch-013) High P0 |

---

## 5. Cross-cutting integrity

### 5.1 Control-layer map: app-level vs DB-level

| Invariant | App-level control | DB-level control | Net |
|---|---|---|---|
| Brand isolation (126 tables) | `RequireBrandId()` predicates (159 files, 02) | `rls_brand*` policies, live-tested | **Both layers**, except: commerce-host and customer lanes broken by 0031 (fail-closed); bypass routes in 4.4 |
| Sub-brand (franchise/store/warehouse) scope | `IsWithinScope` in CreateOrder, UpdateOrderStatus, IssueRefund | 0031 RESTRICTIVE on 39 tables | DB layer present but **miswired** for every principal without `scope_nodes` (SA-TEN-001) |
| Customer vs customer | per-handler `CustomerId` predicate | only the 8 `rls_brand_or_customer` tables, and **only when `app.current_customer_id` is set**, which the commerce host never does | **QA correction:** 08b's claim "DB-enforced on the 8 tables" is **false on the commerce host** (10c contradiction 2; T1b). On `customers` and `customer_addresses` it is app-only |
| Rider vs rider | self-resolve by `UserId` + brand (`UpdateMyTaskStatus.cs:44-53`) | none beyond brand + restrictive scope | app-only (10c location table) |
| Partner isolation | partner claim | `rls_partner` | DB-only (single layer, SA-AUTHZ-015) |
| Role assignment / memberships | `GrantMembership` checks (but accepts any user id, SA-AUTHZ-003) | **none** (RLS off) | app-only |
| Analytics | `Where(BrandId)` | none (MVs) | app-only |
| Brand lifecycle (export / purge / cancel / suspend) | handler passes the caller's own brand (`AdminCancellation.cs:51-97`) | DEFINER functions trust their argument | app-only |
| Idempotency / concurrency | mostly check-then-act | 6 unique-key-backed paths (section 3) | mostly app-only |
| Status transitions (order, leg) | in-memory strategy, bypassed on the rider path | CHECK on vocabulary only | app-only and incomplete |
| Cross-tenant parent references | `CreateOrderCommand.cs:98-115` checks store and customer brand | **0 of 617 FKs include brand_id** | app-only where checked; the pickup `address_id` is not checked (SA-MOB-005) |

### 5.2 Tenant-scoped uniques

Catalogue codes are brand-scoped: coupons, services, items, roles, stores, franchises, and customers `(brand_id, phone_e164)` / `(brand_id, customer_code)`. The 46 uniques without brand_id are classified in 2.4. Generated business numbers are the material risk:
- a cross-tenant collision was reproduced for expenses (SA-DB-007, Medium, orig High);
- an **intra-tenant** collision under 0031 was reproduced for warehouse batches (SA-QC-002, Medium).

QA judged the intra-tenant case "far more frequent" than the cross-tenant one.

### 5.3 Composite foreign keys

**None exist.** 0 of 617 FKs include `brand_id` (live). Reproductions:
- a brand-A order referencing a brand-B franchise, store and customer was accepted (08b; 10c T7a);
- a brand-A payment referencing a brand-B order was accepted (08b);
- a customer pickup at another customer's address, and at a brand-B address, was accepted once the 0031 scope is simulated as fixed (10c T7b).

Cross-tenant **reads** of the linked parent remain blocked by RLS on the parent. That is why QA set the severity to Medium. Recommended: `UNIQUE (brand_id, id)` on parents and `(brand_id, x_id) REFERENCES parent (brand_id, id)` on the hot paths, rolled out as `NOT VALID` then `VALIDATE` ([SA-DB-004](../../FINDINGS.md#sa-db-004), Medium P3; dup SA-TEN-011).

### 5.4 CHECK constraints

There are 322 CHECK constraints (live), covering status and channel vocabularies plus value checks: payments `amount > 0`, wallet `balance >= 0`, refund `amount > 0`, slot `booked_count <= capacity`, and coupon types. Gaps:
- **No CHECK on order money totals** (subtotal, discount ≤ subtotal, grand_total ≥ 0) (08b).
- **Status transitions are app-only**; delivery_assignments has vocabulary CHECKs only (T11).
- **The API contract disagrees with the CHECKs.** The refund API's `"gateway"`/`"wallet"` violates `refund_type` ([SA-QC-001](../../FINDINGS.md#sa-qc-001), High P0). Royalty code filters on payment status `"completed"`, which the payments CHECK never allows ([SA-SOLID-003](../../FINDINGS.md#sa-solid-003), High P2).

### 5.5 Booking overlap and capacity

- **0 exclusion constraints** exist in the database (live).
- Laundry slot capacity is **correctly** protected by an atomic conditional `UPDATE` plus a CHECK, on both the customer path and reschedule. The admin/POS path skips the increment ([SA-API-019](../../FINDINGS.md#sa-api-019), Medium P1).
- Salon appointment and resource overlap has no DB guard, and the schema is not reachable by `app_user` ([SA-DB-021](../../FINDINGS.md#sa-db-021), Low P5). It needs `btree_gist` plus `EXCLUDE USING gist (resource_id WITH =, tstzrange(start_at, end_at) WITH &&) WHERE (status <> 'cancelled')` before salon GA.
- Rider assignment has no uniqueness at all ([SA-MOB-002](../../FINDINGS.md#sa-mob-002), High P1).

### 5.6 Soft delete and uniqueness

0 of 30 uniques on soft-delete tables are partial (live). Deleted coupon, service and store codes and deleted customers' phone numbers therefore block reuse ([SA-DB-019](../../FINDINGS.md#sa-db-019), Low P3). The contradicting convention in `docs/SCHEMA_FULL.sql:17` is part of the stale-reference issue (I35).

### 5.7 Do migrations preserve policies?

- **Round trip:** 0025–0033 down→up on the live scratch DB restored an identical policy dump (empty `diff`), the same RLS count (136) and the same index count (1,649) (08b).
- **Downs that reduce isolation:** the downs for 0027 and 0029 intentionally **disable RLS** on the tables they own. Treat `migrate.sh down` past 0027 as a security change (08b).
- **The tooling itself is sound:** `migrate.sh` is transactional with checksum drift detection (08b positive control).
- **CI never applies migrations.** "Migration lint" only checks that each up file has a down file (10a §3; [SA-OPS-007](../../FINDINGS.md#sa-ops-007), Medium P1). The RLS tests run on a hand-trimmed fixture that sets 6 of the 12 GUCs, with pooling off, and `SubBrandScopeRlsTests.cs:264-273` asserts the 0031 denial as intended behaviour ([SA-TEN-010](../../FINDINGS.md#sa-ten-010), Medium P1; 10c contradiction 8).

### 5.8 A rebuild reverts the `rls_bypass` hardening

[SA-DB-002](../../FINDINGS.md#sa-db-002), High P0; dups SA-ARCH-008, SA-SUB-020, SA-VERT-010. Verified by 08b and reproduced by 10c C3/C6. The file contents were re-checked for this report.

- **The two definitions disagree.**
  - `db/patches/rls_proposal.sql:85-87` defines `kernel.rls_bypass()` as `COALESCE(current_setting('app.bypass_rls', true), 'off') = 'on'`.
  - `db/patches/harden_app_user_and_rls_bypass.sql:31-38` redefines it to accept `('on','true','1','yes','t')`.
  - The interceptor sends `'true'` (`RlsConnectionInterceptor.cs:65`).
- **The build script re-applies the old one.** `db/build_from_scratch.sh` Stage 6 (`:89-90`) re-runs `rls_proposal.sql`, and its header claims it is "safe to re-run against an existing database" (`:6-8`).
- **Effect of a re-run on an existing database:** every platform-admin, worker, webhook and pre-auth bypass silently becomes "not bypassed". Most of those paths then see 0 rows. That fails closed, but it is an outage.
- **Related re-run hazard:** `app_user_role.sql:34` and `harden_app_user_and_rls_bypass.sql:47` reset the `app_user` password to a known value ([SA-TEN-015](../../FINDINGS.md#sa-ten-015), Medium P1).
- **The documented build cannot reproduce the schema:**
  - `migrate.sh up` fails at `0005…up.sql:102`;
  - about 130 patches are applied by no ordered script;
  - two patches assert on seeder-only rows;
  - the demo seeds need a hand-made brand.
- **Fix:** freeze a production `pg_dump --schema-only` as `0000_baseline`; retire `db/patches/` from the bootstrap; stop `rls_proposal.sql` from redefining `rls_bypass()`; add a CI job that builds the schema and asserts `kernel.rls_bypass()` honours `'true'`.

### 5.9 Stale pg_partman configuration

[SA-QC-003](../../FINDINGS.md#sa-qc-003), Medium P0; with [SA-OPS-004](../../FINDINGS.md#sa-ops-004), High P0 (dup SA-MOB-012). Verified on the rebuilt schema (10c T12).

**Cause.** `99_cross_cutting_schema_qualified.sql` registers `order_lifecycle.process_logs` with partman. `db/patches/phase1_slice_c_laundry_fulfillment.sql:24-28,56-90` then moves the table to `laundry_fulfillment` without updating `partman.part_config`.

**Observed (T12):**
- `CALL partman.run_maintenance_proc()` → `ERROR: Given parent table not found in system catalogs: order_lifecycle.process_logs`. The whole call aborts.
- A 38-day-old rider-ping partition survived that run.
- A per-table `partman.run_maintenance('logistics.rider_location_pings')` **did** drop it.

**Combined with the missing scheduler (SA-OPS-004):**
- no future monthly partitions are premade for orders, audit_logs, notifications_log and decision_log, so rows fall into DEFAULT partitions after the documented runway;
- the 14-day deletion of rider GPS history never runs (DPDP).

**Fix:**
- a migration that repoints the `part_config` row;
- a CI or startup assertion that every `part_config.parent_table` resolves;
- per-table maintenance calls, so one bad row cannot block the rest;
- a scheduler with a single-runner lock;
- an alert on any rows in a default partition.

**Production `part_config` state is unknown.**

### 5.10 Pooling and session-level GUCs

The interceptor writes all 12 GUCs, with the `''`/`'?'` sentinels, on **every** EF connection open (`RlsConnectionInterceptor.cs:57-121`). That makes the EF path pool-safe. The supporting evidence:
- In SQL, a session-level `set_config` persisted into the "next request" on the same connection, and `DISCARD ALL` cleared it.
- **Runtime pooling was not observed.**

Risks ([SA-DB-016](../../FINDINGS.md#sa-db-016), Low P5; [SA-OPS-015](../../FINDINGS.md#sa-ops-015), Medium P5):
- raw `NpgsqlDataSource` paths do not set GUCs (they fail closed today);
- transaction-mode PgBouncer would leak GUCs between clients;
- `No Reset On Close=true` would leave isolation resting on the interceptor alone.

### 5.11 RLS predicate performance

`kernel.within_scope_cols()` is plpgsql and runs per row. Synthetic E3/E5 measured about 8× (384 ms vs 48 ms) ([SA-DB-017](../../FINDINGS.md#sa-db-017), Medium P5; not re-run by QA). Fix it together with the SA-TEN-001 predicate change: an inlinable SQL expression over `(SELECT kernel.current_scope_nodes())`, so the scope list is evaluated once as an InitPlan.

---

## 6. Verdicts DB-Q1…DB-Q10

The questions are as defined in [08b §Verdict inputs](specialists/08b-database.md#verdict-inputs). Statuses: Fully Supported / Partially Supported / Not Supported / Not Verified.

| Q | Question | Verdict | Evidence | Disagreements / QA notes |
|---|---|---|---|---|
| **DB-Q1** | Are PK/FK/unique/required indexes correct? | **Partially Supported** | PKs and partition-composite FKs are correct (I1). 69 unindexed FKs and 55 redundant indexes (I30/I31, SA-DB-018); 46 brand-table uniques omit brand_id (2.4); no `gateway_order_id` index (I7); no uniqueness for live assignments (Q1). | QA-C agrees; the FK and redundancy counts were not re-counted by QA |
| **DB-Q2** | Are indexes aligned to query patterns, with bottlenecks verified? | **Partially Supported** | Main lists are well indexed (E4, E7). Webhook seq scan (E1), outbox poll (E6), metadata idempotency (E2) and per-row RLS function (E3/E5). | **All plans are synthetic. Production bottlenecks: Not Verified** (no `pg_stat_*`). QA-C agrees; plans not re-run |
| **DB-Q3** | Are duplicates prevented under concurrency and retries? | **Not Supported** | Over-refund reproduced (08b, 10c T5); wallet lost update reproduced (T6); outbox double claim reproduced; no assignment guard (T11); order creation and coupons check-then-act. Only pickup (with a key), partner wallet, inbox, subscription invoice, notification enqueue and top-up credit are DB-enforced. | **Dissent:** 08 rated this Partially (unique keys exist on several paths). The architect ruled **Not Supported for money and dispatch; supported only for pickup booking and partner wallet** (01b §2.1). QA-C agrees with Not Supported |
| **DB-Q4** | Is there idempotency for payments, webhooks, booking, orders and jobs? | **Partially Supported** | Booking (pickup + slot) is TI by DB design; payments and webhooks are SD; order creation is SD; jobs are single-instance only, and the outbox republishes. | **QA caveat:** the pickup TI pattern is unused by the customer app (no key) and currently unreachable for customers (0031). Refunds are worse than SD because of SA-QC-001 (gateway refund issued before a failing insert) |
| **DB-Q5** | Does the database support RLS? | **Fully Supported** | PostgreSQL 16 RLS with kernel helper functions, permissive and restrictive policies, and partitioned-parent enforcement, all exercised live. | QA-C agrees |
| **DB-Q6** | Is RLS enabled and correct on tenant tables? | **Partially Supported** | Enabled on 126 of 126 brand tables, with A/B isolation proven. But 0031 denies whole principal classes (SA-TEN-001), the raw-cast policies throw (SA-DB-012), and 8 identity tables plus the MVs are unprotected. | **QA: understated in 08b.** 0031 breaks the customer, API-key **and commerce-host staff** lanes (T1b), so in the production role configuration RLS is "correct" only for core/ops staff |
| **DB-Q7** | Can the effective app role bypass RLS? | **Partially Supported** (bypass possible) | Not natively: NOBYPASSRLS, not owner. It can bypass via the self-settable `app.bypass_rls` (S5, T3), superuser-owned DEFINER functions (T2, S7b), RLS-free identity tables (T4, S6), MVs (T9) and cross-tenant FKs (T7a). | QA-A and QA-C agree and reproduced it. "Partially Supported" here means the native role property holds but structural bypass routes exist |
| **DB-Q8** | Is tenant context safe with pooled connections? | **Partially Supported** | The EF interceptor writes all GUCs on each open, and Npgsql reset clears them (SQL repro). Raw ABAC and export connections lack context (fail closed); session-level GUCs rule out transaction-mode pooling. | **Dissent:** 02 rated **Fully Supported for EF-opened connections**. 08b, 11, QA-A, QA-C and the architect rated **Partially**, because the question concerns the whole system. No leak path was found. Runtime pooling: **Not Verified** |
| **DB-Q9** | Are constraints sufficient for tenant ownership and invariants? | **Not Supported** | No composite tenant FKs (T7a); global business-number uniques plus scope-blind generators (T8); no exclusion constraints; no order-total CHECKs; non-partial soft-delete uniques; API/CHECK contract mismatch (T7d). | QA-C agrees |
| **DB-Q10** | Is isolation enforced at both the app AND DB layers? | **Partially Supported** | Brand isolation is two-layer for 126 tables. Role assignment, MVs, lifecycle functions and customer-vs-customer data on most tables are app-only; the sub-brand DB layer exists but is miswired. | **QA correction:** 08b's statement that customer-vs-customer is DB-enforced on the 8 `rls_brand_or_customer` tables is **false on the commerce host**, which serves those tables (T1b). 02 and 10a agree with Partially |

**Unverified production settings.** Every verdict above was reached on schemas rebuilt from the repository. None was checked against production. Run these read-only checks there before relying on any verdict:

| Check | What it decides |
|---|---|
| `SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user` from the deployed connection string | whether RLS applies at all |
| `pg_policies` for `rls_subbrand_scope` (is 0031 applied?) | whether SA-TEN-001 is a live outage |
| `pg_get_functiondef('kernel.rls_bypass()'::regprocedure)` | whether the hardening was reverted (SA-DB-002) |
| `has_function_privilege('app_user', 'kernel.purge_brand(uuid)', 'EXECUTE')` and `pg_default_acl` | SA-DB-003 |
| `SELECT parent_table FROM partman.part_config` | SA-QC-003 |
| Row counts in `*_default` partitions | SA-OPS-004 runway |
| `pg_stat_user_indexes`, `pg_stat_statements` | DB-Q2 and the index drops |
| Npgsql `No Reset On Close`, any PgBouncer and its mode | DB-Q8 |
| Commerce replica count | real exposure of SA-DB-009 / SA-OPS-005 |
| Razorpay `Razorpay-Idempotency` support | SA-SUB-015 |

---

## 7. Prioritised DB remediation list

The order is registry phase (P0 → P5), then final severity. "Layer" says where the fix lives. Every fix is the smallest safe change named in the source reports; none needs a rewrite, a schema-per-tenant model or a DB per tenant (01b §4.1 keeps shared DB + RLS).

| # | Canonical ID (dups) | Final sev | Phase | Fix (smallest safe change) | Layer | Tests to add |
|---|---|---|---|---|---|---|
| 1 | [SA-TEN-001](../../FINDINGS.md#sa-ten-001) (SA-DB-001, SA-AUTHZ-006) + [SA-ARCH-014](../../FINDINGS.md#sa-arch-014) | Critical | P0 | Give non-staff principals a defined scope in `within_scope_cols` (e.g. TRUE when `current_customer_id()` is set, or token-use-aware); make `CommerceHostCurrentTenant` publish the same subject GUCs as `HttpContextCurrentTenant` | DB + app | Lane × host × restrictive-table matrix on a migrated schema (customer, API key, commerce staff; SELECT + INSERT on orders, pickups, payments, delivery_slots, audit_logs); adapter-parity test |
| 2 | [SA-MOB-005](../../FINDINGS.md#sa-mob-005) | Medium | P0 | Address-ownership check in the customer pickup handler. **Ship in the same release as #1**, because the IDOR becomes live once 0031 is fixed | app (+ #19 composite FK later) | foreign `addressId` → 404; own address → 201 |
| 3 | [SA-TEN-002](../../FINDINGS.md#sa-ten-002) (SA-AUTHZ-005) | High | P0 | Publish `app.current_customer_id` from the commerce host (same change as #1) | app | Commerce-adapter RLS test: customer A cannot read customer B's payment or wallet |
| 4 | [SA-DB-012](../../FINDINGS.md#sa-db-012) | High | P0 | Replace the raw-cast `custident_tenant` and salon policies with `kernel.rls_bypass() OR brand_id = kernel.current_brand_id()`, `TO app_user` | DB | brand `''` + bypass `'true'` against every table touched by auth paths |
| 5 | [SA-DB-002](../../FINDINGS.md#sa-db-002) (SA-ARCH-008, SA-SUB-020, SA-VERT-010) | High | P0 | Freeze a baseline from production; stop `rls_proposal.sql` redefining `rls_bypass()`; retire patches from the bootstrap | DB / ops | CI schema build (baseline + migrations + `verify`); assert `kernel.rls_bypass()` returns true for `'true'` |
| 6 | [SA-API-009](../../FINDINGS.md#sa-api-009) (SA-DB-006) + [SA-QC-001](../../FINDINGS.md#sa-qc-001) | High | P0 | Insert the refund as `processing` and commit, then call the gateway outside the transaction with a deterministic reference; `FOR UPDATE` on the payment in the trigger and handler; split `RefundMethod`/`RefundType` | DB + app | Two-connection refund race (cap holds); simulated transient failure → exactly one gateway call; each documented `refundType` persists; a fake gateway sees 0 calls when the row cannot be written |
| 7 | [SA-API-007](../../FINDINGS.md#sa-api-007) / [SA-API-008](../../FINDINGS.md#sa-api-008) | High | P0 | One idempotent "on captured" routine (guarded `UPDATE … WHERE status IN ('pending','failed')`); refund executor using the inbox pattern | app (DB-guarded) | webhook `failed` → `captured` ends captured and paid; verify + webhook race credits once; a cancelled paid order's refund executes once |
| 8 | [SA-DB-003](../../FINDINGS.md#sa-db-003) | Medium (orig High) | P0 | Create an `app_maintenance` role for `RetentionSweepService` **first**; then `REVOKE EXECUTE` on brand-lifecycle DEFINER functions from `app_user`; add a `p_brand_id = current_brand_id() OR rls_bypass()` guard inside each; replace the kernel default privileges with explicit grants | DB | `has_function_privilege('app_user', …)` = false for purge/export; each brand-taking DEFINER raises on a foreign brand |
| 9 | [SA-QC-003](../../FINDINGS.md#sa-qc-003) + [SA-OPS-004](../../FINDINGS.md#sa-ops-004) (SA-MOB-012) | Medium / High | P0 | Repoint the `part_config` row; per-table maintenance under a single-runner lock in the worker; default-partition alert | DB / ops | `run_maintenance_proc()` succeeds on the migrated schema; partitions exist N months ahead; a ping partition older than 14 days is dropped |
| 10 | [SA-MOB-001](../../FINDINGS.md#sa-mob-001) | High | P0 | Leg state machine via conditional `UPDATE … WHERE status = @from`; route the rider order completion through `EnsureTransition` | app (DB-guarded) | cancelled → completed rejected; a double `completed` decrements load once |
| 11 | [SA-DB-005](../../FINDINGS.md#sa-db-005) (+ app vector [SA-AUTHZ-003](../../FINDINGS.md#sa-authz-003), P0) | High | P1 | Enable RLS on `user_scope_memberships` with a brand derived via a DEFINER helper (or a `brand_id` column); enable `rls_user_self` on token, OTP and profile tables, keeping the auth-path bypass; restrict the users INSERT check; revoke catalogue writes | DB | An A session cannot insert a membership whose user, role, scope or brand is B; `app_user` cannot read another brand's otp_codes or refresh_tokens |
| 12 | [SA-API-004](../../FINDINGS.md#sa-api-004) (SA-DB-011) | High | P1 | `idempotency_key` column + partial UNIQUE `(brand_id, idempotency_key)`, catch 23505; guarded capture; UNIQUE `(gateway, gateway_order_id)` (I7) and `(gateway, gateway_payment_id)` (I8) | DB + app | Parallel POS create with the same key → one order, one coupon redemption, one ledger debit; webhook concurrent with verify → one `payment.captured` |
| 13 | [SA-API-005](../../FINDINGS.md#sa-api-005) (SA-DB-008) | High | P1 | Atomic `balance = balance + @d … RETURNING`, or `UseXminAsConcurrencyToken()` on wallet, loyalty, coupon, package, order, mapping `DbUpdateConcurrencyException` → 409 | DB + app | Parallel top-up + debit asserting `balance == SUM(ledger)` |
| 14 | [SA-MOB-002](../../FINDINGS.md#sa-mob-002) (SA-DB-022) | High | P1 | Partial UNIQUE on live legs `(pickup_request_id)` and `(order_id, leg_type)`; status check; 23505 → 409; conditional accept and expire; reassign command | DB + app | Concurrent assign → one live leg; assign-after-assign 409; assign of a cancelled pickup 409; accept vs expire; reassign moves load |
| 15 | [SA-DB-009](../../FINDINGS.md#sa-db-009) + [SA-OPS-005](../../FINDINGS.md#sa-ops-005) (SA-API-014, SA-ARCH-007) | Medium | P1 | Claim with `FOR UPDATE SKIP LOCKED` or a conditional update plus a lease; **return before publishing when the claim fails** (relay defect); billing attempt row inserted `initiated` before charging; separate single-replica worker host or advisory lock per job; outbox poll index (I22) | DB + app | Two relay instances → each event published once; claimed-then-crash lease reclaimed; already-claimed row not republished; two dispatchers send once; two billing replicas → one gateway call |
| 16 | [SA-API-015](../../FINDINGS.md#sa-api-015) | Medium | P1 | Move notification mapping and loyalty earn to the `outbox_consumed_events` inbox pattern | app (DB-backed) | interleaved-commit test: a late-committed event is still processed |
| 17 | [SA-DB-010](../../FINDINGS.md#sa-db-010) | Medium | P1 | Guarded usage-count `UPDATE`; partial UNIQUE `(order_id) WHERE reverted_at IS NULL`; per-customer single-use guard | DB | parallel redemption at the cap |
| 18 | [SA-QC-002](../../FINDINGS.md#sa-qc-002) | Medium | P1 | Per-brand counter (the `next_order_number` upsert) in a DEFINER function or under a brand-only predicate | DB | two warehouse-scoped users create batches the same day; two franchise-scoped users create expenses |
| 19 | [SA-API-018](../../FINDINGS.md#sa-api-018) (SA-MOB-018) / [SA-API-019](../../FINDINGS.md#sa-api-019) | Medium | P1 | Client sends `Idempotency-Key`; the admin pickup path uses the atomic slot increment | app | jest: header present; parallel pickup with the same key → one row (reference pattern regression); admin booking on a full slot rejected; booked count consistent after reject |
| 20 | [SA-API-020](../../FINDINGS.md#sa-api-020) | Medium | P1 | Conditional `UPDATE refresh_tokens … WHERE revoked_at IS NULL` | DB-guarded app | parallel refresh → exactly one success |
| 21 | [SA-DB-014](../../FINDINGS.md#sa-db-014) | Medium | P1 (P1 hard gate before any ABAC enforce) | ABAC reads in a transaction with `SET LOCAL app.bypass_rls`, or DEFINER read functions; replace COPY with batched INSERT or a DEFINER writer | DB + app | integration test against `app_user` (not superuser) |
| 22 | [SA-TEN-007](../../FINDINGS.md#sa-ten-007) (SA-DB-015) | Medium | P1 | A separate login role for platform and worker connections (`app_admin` already exists in `rls_proposal.sql:54-57`) instead of a tenant-settable GUC; platform requests with `X-Brand-Id` set the brand GUC instead of bypassing; startup assertion that the runtime role is NOSUPERUSER and NOBYPASSRLS | DB + app | an `app_user` session cannot obtain bypass |
| 23 | [SA-DB-018](../../FINDINGS.md#sa-db-018) | Medium | P1 | `CREATE INDEX CONCURRENTLY` on high-churn FK children (2.2); drop the exact duplicates now and prefix duplicates after production `pg_stat_user_indexes` | DB | CI catalog check: no FK without a leading index on listed parents |
| 24 | [SA-TEN-010](../../FINDINGS.md#sa-ten-010) / [SA-OPS-007](../../FINDINGS.md#sa-ops-007) / [SA-TEN-015](../../FINDINGS.md#sa-ten-015) | Medium | P1 | Generate the fixture's GUC setter from the interceptor; run RLS tests with pooling on against the real migrated schema; remove the hard-coded `app_user` password from patches | test / ops | see "RLS under pooling" below |
| 25 | [SA-SUB-016](../../FINDINGS.md#sa-sub-016) | Low | P1 | `webhook_events(event_id PK)` insert-first dedupe, or a guarded update; verify the paylink amount | DB + app | two concurrent identical webhooks → one outbox event |
| 26 | [SA-SUB-004](../../FINDINGS.md#sa-sub-004) (SA-API-010) / [SA-SUB-015](../../FINDINGS.md#sa-sub-015) | High / Medium | P2 | `CreateWorkerAsyncScope()` for the renewal pass; validate the mandate charge flow against the sandbox | app | worker test under `app_user` issues a renewal; gateway contract test |
| 27 | [SA-DB-004](../../FINDINGS.md#sa-db-004) (SA-TEN-011) | Medium (orig High) | P3 | `UNIQUE (brand_id, id)` on parents plus composite FKs on hot paths, `NOT VALID` → `VALIDATE` | DB | cross-brand FK insert must fail (orders↔customers/stores/franchises, payments↔orders, pickups↔addresses) |
| 28 | [SA-DB-007](../../FINDINGS.md#sa-db-007) (SA-TEN-012) | Medium (orig High) | P3 | `(brand_id, number)` uniques; brand-scope the client-supplied idempotency keys; limit domain uniqueness to verified rows | DB | two brands' first expense, batch and pickup on the same day |
| 29 | [SA-DB-013](../../FINDINGS.md#sa-db-013) (SA-TEN-013) | Medium | P3 | `security_barrier` views filtering `brand_id = current_brand_id() OR rls_bypass()`; revoke direct MV SELECT; restrict refresh | DB | an A session sees only A rows through the views |
| 30 | [SA-DB-019](../../FINDINGS.md#sa-db-019) / [SA-DB-020](../../FINDINGS.md#sa-db-020) | Low | P3 | Partial uniques where reuse is a product requirement; product decision on global staff identity | DB / product | unique-index tests |
| 31 | [SA-DB-017](../../FINDINGS.md#sa-db-017) | Medium | P5 | Inlinable scope predicate (do it together with #1 if it is touched anyway) | DB | plan test shows an InitPlan, not a per-row function call |
| 32 | [SA-DB-016](../../FINDINGS.md#sa-db-016) / [SA-OPS-015](../../FINDINGS.md#sa-ops-015) | Low / Medium | P5 | Document "session pooling only"; startup assertion that `No Reset On Close` is unset; consider `set_config(…, true)` inside transactions before any PgBouncer | ops / app | pool-reuse test (below) |
| 33 | [SA-DB-021](../../FINDINGS.md#sa-db-021) | Low | P5 | Before salon GA: grants, kernel-helper policies and a `btree_gist` exclusion constraint | DB | overlapping resource booking rejected |

### 7.1 Tests to add, by the categories requested

The commerce layer has no test project today ([SA-ARCH-010](../../FINDINGS.md#sa-arch-010), Medium P1). Most tests below need a Testcontainers PostgreSQL built from the **real** migrated schema (#5), not the trimmed fixture.

**Concurrent same-key requests** (two connections, released together)
- POS `CreateOrder` with the same key → 1 order, 1 coupon redemption, 1 loyalty and 1 package ledger debit (SA-API-004).
- Customer pickup with the same key → 1 pickup, 1 slot unit (regression guard for the reference pattern).
- `InitiatePayment` with the same key → 1 payment row **and** 1 gateway `CreateOrderAsync` call (fake gateway).
- Wallet top-up `/verify` twice → 1 credit.
- Admin wallet adjust with a client key → 1 ledger row; the key is brand-scoped.

**Duplicate webhooks**
- Two concurrent identical `payment.captured` → 1 status change, 1 outbox event (SA-SUB-016).
- Webhook concurrent with `/verify` → captured once; a wallet top-up is credited once.
- `payment.failed` then `payment.captured` → ends captured and the order is paid (SA-API-007).
- Brand paylink on a `past_due` invoice → paid and reinstated (SA-SUB-002).
- Partner paylink redelivered → 1 top-up.

**Retried jobs**
- Two `OutboxEventRelayService` instances → each event published once; a row claimed by the other instance is **not** published (SA-DB-009 defect).
- Crash after claim → the lease expires and the row is reclaimed.
- Two `NotificationDispatcherService` instances → each row sent once (SA-OPS-005).
- Two billing replicas → 1 gateway charge per invoice attempt.
- Renewal pass under `app_user` issues the due invoice (SA-SUB-004).
- Admin refund with an injected transient DB failure → 1 gateway refund (SA-API-009).
- Refund executor processes a pending cancellation refund once (SA-API-008).
- Interleaved-commit watermark test (SA-API-015).
- `run_maintenance_proc()` succeeds (SA-QC-003).

**Booking and assignment conflicts**
- Two refunds of 60 on a 100 payment → one rejected (SA-API-009).
- Parallel coupon redemption at `max_total_uses` → the cap holds (SA-DB-010).
- Parallel wallet top-up and debit → `balance == SUM(ledger)` (SA-API-005).
- Concurrent manual assign, and auto-dispatch vs manual → one live leg; assign-after-assign 409; assign of a cancelled or completed pickup 409; accept vs expire (SA-MOB-002).
- Admin booking on a full slot rejected; reject releases only capacity that was taken (SA-API-019).
- Two brands' first expense, batch and pickup on the same day; two warehouse-scoped users' batches (SA-DB-007, SA-QC-002).
- Cancel vs rider-complete race ends in a single legal state (SA-SOLID-001 / SA-MOB-001).

**RLS cross-tenant tests run as `app_user` under pooling**
- Run as `app_user` (assert `rolbypassrls = f`), **with Npgsql pooling on**, through the real `RlsConnectionInterceptor` (not a hand-written GUC setter) and every `ICurrentTenant` adapter (SA-TEN-010).
- Lane matrix (#1): staff (core/ops and commerce), customer (core/ops and commerce), API key, partner, worker and anonymous, against every RESTRICTIVE table plus the `rls_brand_or_customer` tables. Expect brand A ≠ brand B, and customer c1 ≠ c2, on payments, wallets and addresses.
- **Pool-reuse test:** request as tenant A, then as tenant B on the same physical connection (`Max Pool Size=1`) → B sees no A rows. Repeat for a raw `NpgsqlDataSource` path, and assert that `No Reset On Close` is not set.
- Bypass resistance: an `app_user` session cannot obtain bypass once the role split lands (SA-TEN-007); each DEFINER function raises on a foreign brand (SA-DB-003); a cross-brand membership insert fails (SA-DB-005); a cross-brand FK insert fails (SA-DB-004); MV views return only own-brand rows (SA-DB-013); anonymous + bypass against `customer_identities` does not error (SA-DB-012); the users INSERT of `platform_admin` from a tenant session is refused (SA-AUTHZ-001 DB backstop).
- Schema guard: the CI build asserts `kernel.rls_bypass()` honours `'true'` and every `part_config.parent_table` resolves (SA-DB-002, SA-QC-003).

---

## 8. Positive controls (verified)

- **The runtime role is genuinely subject to RLS.** `app_user` is NOSUPERUSER and NOBYPASSRLS, owns nothing, and has no TRUNCATE, TRIGGER or CREATE (live). Deploy and AppHost default to it (08b).
- **Full brand coverage.** All 126 brand tables have RLS, with A/B isolation proven for SELECT, INSERT (WITH CHECK), UPDATE and DELETE. Unset GUCs return 0 rows without errors, except the raw-cast policies (08b; 10c). Migration 0027 enforces the coverage.
- **Pool-safe interceptor design.** All 12 GUCs are written on every open, with `''`/`'?'` sentinels and a per-request scope (`RlsConnectionInterceptor.cs:57-121`). The 0031 failure is a lane-mapping defect, not an interceptor defect (01b).
- **Worker bypass fails closed** without a positive marker (`CommerceHostCurrentTenant.cs:80-90`).
- **Reference idempotency patterns exist and are correct:**
  - customer pickup: partial unique + 23505 by constraint name + an atomic, CHECK-guarded slot increment;
  - partner wallet: `FOR UPDATE` + a `(partner_id, key)` unique;
  - the `outbox_consumed_events` inbox;
  - the per-period subscription-invoice unique;
  - the notification enqueue key;
  - the wallet top-up key;
  - the atomic order-number upsert.

  Most remediations in section 7 copy these patterns.
- **Webhook HMAC** uses the per-brand secret, a constant-time compare, and fails closed outside Development (`RazorpayWebhookHandler.cs:103-150,365-377`).
- **Migration tooling** is transactional with checksum drift detection, and the 0025–0033 round trip was lossless.
- **CHECK coverage** exists for statuses and key money fields.
- **Partitioned parents enforce RLS** correctly: orders, process_logs, notifications_log, audit_logs, rider pings and decision_log.
- **EF ↔ DB index naming is consistent**: 126 of 128 names match, and the 2 misses are PostgreSQL's 63-character name truncation.

---

## Observations for registry triage

These need no new ID. Each is a consistency note, or a fact seen while consolidating, that the registry owner may want to triage.

1. **Possible duplicate FK on `commerce.customer_packages`.** Status: Suspected (catalog artefact only; the DDL source of the second constraint was not traced).
   - The 08b artefact `fk_noindex.txt` lists two parent FK constraints on the same column pair `(purchase_order_id, purchase_order_created_at)` → orders: `customer_packages_purchase_order_id_fkey` (from `db/patches/auth_token_lineage_and_package_purchase_fk.sql:44-45`) and `customer_packages_purchase_order_id_purchase_order_created_fkey`.
   - The same patch creates `idx_customer_packages_purchase_order_id_fk` on `(purchase_order_id)` (`:51-52`). The "unindexed" classification for this pair may therefore be conservative.
2. **Phase mismatches:**
   - SA-DB-005 is P1 in the registry, while the 01b roadmap places "RLS on identity tables + composite tenant FKs (SA-DB-004/005)" in Phase 3.
   - SA-MOB-002 is P0 in the specialist report and P1 in the registry. This report follows the registry.
3. **Worker-claim grouping.** 10c D11 proposed SA-API-014 as canonical with SA-DB-009 as a member. The registry instead makes SA-OPS-005 canonical (dups SA-API-014, SA-ARCH-007) and keeps SA-DB-009 separate for the relay republish defect. This report follows the registry; the two IDs should stay cross-linked.
4. **02 vs 10c on `purge_brand`.** 02's positive control "purge function not granted to `app_user` (`0015:248-250`)" is contradicted by 10c C8 (granted through `pg_default_acl`) and by 08b's `RetentionSweepService` dependency. The registry's SA-DB-003 reflects the corrected view; 02's positive-control line is stale.
5. **QA's own findings marked as unverified.** The registry `qa` field for SA-QC-002 and SA-QC-003 reads "Not independently re-verified by QA". They are QA-C's own findings, reproduced in T8 and T12, so the wording is misleading but not wrong.
6. **Index-only recommendations without a finding ID:**
   - I2 (order-number registry);
   - I8 (unique `gateway_payment_id`);
   - I10 (unique mandate id);
   - I16 (slot-booking link);
   - I22 (outbox poll index);
   - I32 (brand-leading indexes).

   These should be folded into SA-API-004 / SA-DB-009 / SA-DB-018 or left as matrix-only items.
7. **`kernel.system_settings` under 0031.** It is in the 0031 RESTRICTIVE list (`0031…up.sql:152`). The reports list it but do not trace the effect on settings reads made from customer or commerce-host sessions, for example the per-brand gateway settings lookup. Status: Not Tested. Include it in the lane-matrix test (#1).

