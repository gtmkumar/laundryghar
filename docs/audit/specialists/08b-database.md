# 08b — Database Integrity, Indexing, Idempotency and Row-Level Security

Agent key: `database` · AREA code: `DB` · Date: 2026-10-09 · Branch: `claude/brave-dijkstra-6hlddw`

## Scope and method

**What I looked at.** The schema as the repo builds it: `database_scripts/*`, `db/build_from_scratch.sh`, every file in `db/patches/*`, `db/migrations/0001–0033` and `db/tools/migrate.sh`. On the .NET side: the EF model config (`laundryghar.SharedDataModel/Persistence/Configurations/**`), the RLS connection interceptor, tenant/DI wiring, the ABAC raw-SQL store, and the payment, wallet, coupon, order, pickup, outbox, notification, subscription and retention handlers and workers. I also checked deploy config (`deploy/docker-compose.yml`, `deploy/.env.example`, `laundryghar.AppHost/AppHost.cs`, `appsettings.Development.json`).

**Live verification. I built a disposable cluster, separate from any real database.**
- I installed `postgresql-16-partman` 5.0.1 and `postgresql-16-postgis-3` 3.4.2 with apt (OS level, not in the repo). **I did not stub any extension.**
- The cluster was PostgreSQL 16.15 on port 55432, run as the `postgres` OS user. The brief asked for the data directory under the scratch dir, but the harness keeps `/tmp/claude-0` at mode 0700, which the `postgres` user cannot traverse. I used `/var/tmp/lg-audit-8b` instead. **I stopped the cluster and deleted that directory at the end.**
- Build sequence and result of each step:
  1. `DB_NAME=lg_audit DB_PORT=55432 db/build_from_scratch.sh`. It finished with exit 0: 542 FKs, 61 updated_at triggers, 92 inert policies.
  2. `db/tools/migrate.sh up`. **It failed at 0005** with `relation "identity_access.modules" does not exist` (see SA-DB-002).
  3. Because no ordered runner exists for the ~150 files in `db/patches/`, I applied them in git first-commit order, in multiple passes until no further progress. I skipped `wipe_demo_*`. The script is `scratchpad/database/apply_patches_multipass.sh`. In total 135 patches applied.
  4. Some patches still failed on data:
     - The demo seeds need a pre-existing brand `5b375161-…`. I inserted a stub platform and brand row; some seeds still fail on other hand-made demo rows. These are seed data only.
     - Two patches assert on RBAC rows that the .NET `IdentitySeeder` creates: `phase1_slice_e_garment_to_fulfillment.sql` and `phase4_role_vertical_key.sql`. I applied scratch copies with only their `DO $verify$` data-assertion blocks removed. The DDL is unchanged.
  5. `migrate.sh up` then applied 0005–0033 cleanly (29 migrations).
  6. I re-applied `harden_app_user_and_rls_bypass.sql`. In the git-order run, `rls_proposal.sql` (same commit, sorts later alphabetically) overwrote the hardened `kernel.rls_bypass()`. The real chronological order puts harden after rls_proposal (SA-DB-002).
- Result: 162 logical tables, 136 with RLS enabled, 190 policies, 1,649 indexes.
- Tests I ran:
  - Catalog queries: `pg_class`, `pg_policies`, `pg_index`, `pg_constraint`, `pg_proc`, grants.
  - Cross-tenant tests **as `app_user`**, with GUCs set exactly the way `RlsConnectionInterceptor` sets them.
  - Two-session concurrency races.
  - A migration down-9/up round trip.
  - `EXPLAIN (ANALYZE, BUFFERS)` on synthetic data.
- **EXPLAIN data was synthetic**: brands A and B, 60k orders, 60k payments, 20k customers, 40k outbox events. Plans are labelled as synthetic-data plans. **Treat them as relative evidence only, not production statistics.**
- **No `pg_stat_user_indexes` usage numbers are reported.** A fresh cluster has none.

**What I could not verify.**
- No .NET runtime, so I did not exercise any HTTP request end to end. Application behaviour comes from tracing the code; database behaviour comes from reproduction with interceptor-equivalent GUCs.
- I did not observe Npgsql's reset-on-close (`DISCARD ALL`) at runtime. It is Npgsql's documented default; `DISCARD ALL` clearing the GUCs was reproduced in SQL.
- Whether Razorpay honours the `Razorpay-Idempotency` header was not verified.
- The real production database's patch history is unknown. The repo's documented build path cannot reproduce it.

Scratch artefacts (outside the repo) are in `/tmp/claude-0/-home-user-laundryghar/65b29198-109e-5a5c-ba03-c03109e35b59/scratchpad/database/`: `build1.log`, `migrate1.log`, `migrate2.log`, `patch_pass*.log`, `plog/`, `schema_dump.sql`, `rls_tables.txt`, `policies.txt`, `fk_noindex.txt`, `uniq_nobrand.txt`, `redundant_idx.txt`, `key_tables.txt`, `rls_test1.out`, `rls_test2.out`, `explain.out`, `c1*.out`–`c3*.out`.

## Current-state summary

**Runtime principal: `app_user`.** RLS really is in the request path.
- `deploy/.env.example:4-5` sets `Username=app_user` and warns that it must be non-superuser.
- `AppHost.cs:22-24` defaults to `app_user`.
- The compose file passes only `ConnectionStrings__Default` (`deploy/docker-compose.yml:23`).
- `ConnectionStrings:Admin` (postgres) is used only for Development seeding (`core.WebApi/Program.cs:478-493`, `SeedingSupport.cs:9-41`). Seeding is refused outside Development.
- Live role attributes: `app_user` is `rolsuper=f, rolbypassrls=f`, owns 0 tables, and has no TRUNCATE, TRIGGER or CREATE privilege anywhere. Every table is owned by `postgres`.

**Tenant context path.** The request path is: JWT → `HttpContextCurrentTenant` → `RlsConnectionInterceptor.ConnectionOpened` → session-level GUCs → RLS policies.
- `HttpContextCurrentTenant` (`laundryghar.Utilities/Services/HttpContextCurrentTenant.cs:24-61`) builds brand, franchise, store, partner, user/customer, scope_nodes, roles and permissions from the token claims.
- `RlsConnectionInterceptor.ConnectionOpened` (`laundryghar.SharedDataModel/Persistence/Interceptors/RlsConnectionInterceptor.cs:30-121`) runs one `SELECT set_config(…, false)` for **all 12** `app.*` GUCs on every logical open. Null scope, role and permission values become `'?'`.
- The interceptor is registered Scoped and resolved per DbContext (`DependencyInjection.cs:48-105`).
- `kernel.current_*()` read the GUCs with `NULLIF(…,'')::uuid`.
- `kernel.rls_bypass()` accepts `on|true|1|yes|t` once `harden_app_user_and_rls_bypass.sql:31-38` has been applied.
- Bypass is set for:
  - platform admins, on every request (`TenantResolutionMiddleware.cs:32-48`);
  - pre-auth auth paths and step-up (`core.WebApi/Program.cs:532-562, 620-646`);
  - the Razorpay webhooks (`commerce.WebApi/Program.cs:351-366`).

**RLS coverage (live).**
- All **126** tables that have a `brand_id` column have RLS enabled with at least one policy.
- 39 of them also carry the RESTRICTIVE `rls_subbrand_scope` policy from migration 0031.
- 10 non-brand tables have RLS: partner tables and authz.
- 8 identity tables have policies that are inert because RLS is disabled: `users`-side self tables and the RBAC catalogue.
- Brand A/B isolation holds for SELECT, INSERT, UPDATE and DELETE. Unset GUCs return 0 rows, not errors.
- Several structural bypass routes remain: SECURITY DEFINER functions, missing composite FKs, tables without RLS, materialized views, and a self-settable bypass GUC.
- 0031 breaks every principal whose token carries no `scope_nodes` claim, which includes **all customer tokens** (SA-DB-001).

**Idempotency.**
- **Truly idempotent** (DB-enforced): customer pickup scheduling, the partner wallet, the partner-booking debit inbox, subscription invoice issuance, notification-outbox enqueue, and wallet top-up credit.
- **Sequential-duplicate-only** (racy check-then-act with no DB backstop): POS/customer order creation, coupon redemption, refund cap, wallet balance, payment capture (webhook vs verify), outbox and notification dispatch claims, and subscription charge attempts.
- EF has **no optimistic-concurrency token anywhere**. A grep for `IsConcurrencyToken|IsRowVersion|xmin|ConcurrencyCheck` finds nothing, and there are 0 `DbUpdateConcurrencyException` handlers.
- `SELECT … FOR UPDATE` is used in exactly one place: the partner wallet (`commerce.Infrastructure/Persistence/CommerceDbContext.cs:111-125`).

## Index audit matrix

Status labels: OK / Gap / Redundant / Risk. "Live" means confirmed in the scratch DB catalog.

| # | table | index / constraint | columns & order | purpose | status | supporting query (code) | problem | recommendation | impact |
|---|---|---|---|---|---|---|---|---|---|
| I1 | order_lifecycle.orders (partitioned monthly) | `orders_pkey` | (id, created_at) | PK incl. partition key | OK (live) | all order loads | Every FK to orders must be composite. It is (`*_order_id_fkey` on (order_id, order_created_at)). | none | — |
| I2 | orders | `orders_order_number_created_at_key` UNIQUE | (order_number, created_at) | "unique" order number | Risk (live) | `CreateOrderCommand.cs:806-815` → `order_lifecycle.next_order_number()` | Including created_at makes the key meaningless for duplicate detection. Within-brand uniqueness depends only on the generator (per brand/store/year counter, `LG-yyyy-<store_code>-nnnnnn`). | Add a non-partitioned `order_lifecycle.order_number_registry(brand_id, order_number) PK`, inserted in the same transaction. | Duplicates are undetectable if the generator or store code changes |
| I3 | orders | `idx_orders_brand_store_status` partial `deleted_at IS NULL` | (brand_id, store_id, status, created_at DESC) | admin list | OK (live, E4: Merge Append of per-partition index scans, 0.86 ms synthetic) | order list handlers | — | — | — |
| I4 | orders | `idx_orders_customer` | (customer_id, created_at DESC) | customer history | OK (E7: index scan, 1.9 ms synthetic) | customer self queries | `idx_orders_customer_id_fk` (customer_id) is redundant | Drop the redundant one | write amplification |
| I5 | orders | `idx_orders_metadata_gin` | gin(metadata) | idempotency lookup by `metadata @> {"idempotency_key":…}` | Risk (E2: planner used per-partition `brand_id` index and filtered 30k rows; **421 ms synthetic**) | `CreateOrderCommand.cs:71-78` | No unique constraint, and slow because RLS predicates are evaluated per row (SA-DB-011, SA-DB-017). | A dedicated `idempotency_key` column with a partial UNIQUE (brand_id, idempotency_key, created_at), or a non-partitioned idempotency table | Duplicate orders; latency |
| I6 | orders | `idx_orders_pickup_slot_fk` and `idx_orders_pickup_slot_id_fk` | (pickup_slot_id) ×2 | FK | Redundant – exact duplicate (live) | — | duplicate | Drop one | write cost |
| I7 | commerce.payments | (none) on `gateway_order_id` | — | webhook lookup | **Gap** (E1: **Seq Scan over all brands, Rows Removed 60,000**, synthetic) | `RazorpayWebhookHandler.cs:96-100` (runs with bypass, so cross-brand) | Every webhook seq-scans the whole payments table | `CREATE UNIQUE INDEX CONCURRENTLY ON commerce.payments (gateway, gateway_order_id) WHERE gateway_order_id IS NOT NULL` | Linear growth; webhook timeouts and retries |
| I8 | payments | `idx_payments_gateway` | (gateway, gateway_payment_id) partial, **non-unique** | dedupe | Gap | webhook / verify | A gateway payment can be recorded twice | Make it UNIQUE partial | duplicate capture rows |
| I9 | payments | `payments_idempotency_key_key` UNIQUE, `payments_payment_number_key` UNIQUE | (idempotency_key), (payment_number) | idempotency | Risk – **global, not brand-scoped** (live) | `CustomerPaymentHandlers.cs:37-43` looks up by (key, brand, customer) | A collision across tenants or customers raises 23505 instead of an idempotent hit | Make it (brand_id, idempotency_key) | cross-tenant interference (low probability with UUID keys) |
| I10 | commerce.payment_mandates | `idx_mandate_gateway` non-unique | (gateway, gateway_mandate_id) | mandate dedupe | Gap | mandate flows | duplicate mandates possible | UNIQUE partial | double charging surface |
| I11 | commerce.payment_refunds | `payment_refunds_idempotency_key_key` UNIQUE partial; trigger `trg_check_refund_cap` | (idempotency_key) | refund idempotency and cap | Risk (live race, SA-DB-006) | `AdminPaymentHandlers.cs:100-118,220`; `payment_idempotency.sql:29-80` | The cap trigger does not lock the parent payment | `SELECT … FROM commerce.payments WHERE id=NEW.original_payment_id FOR UPDATE` in the trigger | over-refund |
| I12 | commerce.coupon_redemptions | none unique | — | single-use / per-order | **Gap** (live) | `CustomerCouponHandlers.cs:85-142`, `CreateOrderCommand.cs:292-311,757-771` | Limits are enforced only in the app (SA-DB-010) | Partial UNIQUE (order_id) WHERE reverted_at IS NULL. For single-use coupons, a guarded `UPDATE coupons SET current_usage_count=current_usage_count+1 WHERE id=… AND (max_total_uses IS NULL OR current_usage_count<max_total_uses)` | over-redemption |
| I13 | commerce.wallet_transactions | `wallet_transactions_idempotency_key_key` UNIQUE | (idempotency_key) – global | ledger dedupe | OK for top-up (the key embeds the payment id). Risk for admin-supplied keys (global scope). | `CustomerWalletHandlers.cs:137-211`, `AdminWalletHandlers.cs:~93-172` | A client key reused by another brand gives 23505 | (brand_id, idempotency_key) | low |
| I14 | commerce.wallet_accounts | `wallet_accounts_customer_id_key` UNIQUE + `idx_wallet_customer` | (customer_id) ×2 | 1 wallet per customer | OK / Redundant | — | duplicate index | Drop `idx_wallet_customer` | — |
| I15 | order_lifecycle.delivery_slots | UNIQUE (store_id, slot_date, slot_start, slot_type); CHECK `booked_count<=capacity` | — | slot uniqueness and capacity | **OK** (live) | `PickupCommands.cs:398-415` atomic `UPDATE … WHERE booked_count<capacity` | Tenant unique key lacks brand_id, but store_id is brand-owned | — | — |
| I16 | delivery_slot_bookings | no unique | — | booking ↔ pickup link | Gap (minor) | `PickupCommands.cs:448-458` links by (slot, customer, pickup_request_id IS NULL) | A concurrent double booking of the same slot by one customer can mis-link | Partial UNIQUE (pickup_request_id) WHERE status='active' | minor |
| I17 | order_lifecycle.pickup_requests | `pickup_requests_customer_idempotency_key` partial UNIQUE | (customer_id, idempotency_key) | idempotency | **OK** (live) | `PickupCommands.cs:338-470` (catches 23505 by constraint name) | — | — | — |
| I18 | pickup_requests | `pickup_requests_request_number_key` UNIQUE (global) | (request_number) | number | Risk | `PickupCommands.cs:61-63` `PKP-{yyyy}-{first 4 hex of brandId}-{brandCount+1}` | Two brands whose ids share the first 4 hex characters collide. COUNT+1 races within a brand. | (brand_id, request_number) plus a sequence | 23505 on create (SA-DB-007) |
| I19 | finance_royalty.expenses | `expenses_expense_number_key` UNIQUE (global) | (expense_number) | number | **Risk – reproduced** | `ExpenseCommands.cs:204-205` `EXP-{yyyyMMdd}-{brandCount+1}` | Brand B could not insert its first expense because brand A's identical number existed (live) | (brand_id, expense_number) plus a per-brand sequence | cross-tenant DoS and existence oracle |
| I20 | laundry_fulfillment.warehouse_batches / fulfillment_unit(_tags) / tenancy_org.franchise_agreements | global UNIQUE batch_number / tag_code / agreement_number | — | numbers | Risk (live) | `CreateWarehouseBatch.cs:48-49`, `GenerateTags.cs:34-42`, `SaveCommercials.cs:53` (`AGR-{franchiseCode}-{year}`; franchise codes are only unique per brand) | same class as I19 | brand-scoped uniques | same |
| I21 | royalty_invoices / subscription_invoices / franchise_subscription_invoices / customers.referral_code / riders.user_id | global UNIQUEs | — | numbers / codes | Risk (live list `uniq_nobrand.txt`, 46 entries) | various | Generator uniqueness is not tenant-scoped | review each; prefer (brand_id, …) | cross-tenant collisions |
| I22 | kernel.outbox_events | `idx_outbox_pending` (status='pending'), `idx_outbox_events_retry` (status='failed' AND attempts<10) | (occurred_at,status) / (next_attempt_at) | relay poll | Risk (E6: **Seq Scan, removed 39,600 of 40,000**, synthetic) | `OutboxEventRelayService.cs:75-82` filters `status IN ('pending','failed')` | Neither partial index matches the OR predicate; the scan grows with published rows | `CREATE INDEX … (occurred_at) WHERE status IN ('pending','failed')` plus a retention purge of published rows | relay latency |
| I23 | kernel.outbox_consumed_events | PK (consumer_name, event_id) | — | inbox dedupe | OK (live) | `PartnerBookingDebitService.cs:103-150` | FK `event_id` has no index (FK checks on outbox deletes seq-scan) | `CREATE INDEX … (event_id)` | purge cost |
| I24 | engagement_cms.notifications_outbox | `notifications_outbox_idempotency_key_key` UNIQUE | (idempotency_key) | enqueue dedupe | OK (live) | `NotificationMappingService.cs:284-322` key `evt:{eventId}:ch:{channel}` | per event, not per business fact | — | duplicate events produce duplicate sends |
| I25 | commerce.subscription_invoices / franchise_subscription_invoices | UNIQUE (subscription_id, billing_period_start) | — | one invoice per period | **OK** (live) | `SubscriptionBillingService.cs:~140-215` | — | — | — |
| I26 | commerce.subscription_billing_attempts | UNIQUE (idempotency_key) | `subcharge-{invoiceId}-{attemptNo}` | attempt dedupe | Risk | `SubscriptionBillingService.cs:343-383` | The gateway charge happens **before** the row insert, so the unique key cannot prevent a second charge | Insert an `initiated` attempt row first (claim), then charge | double charge across replicas |
| I27 | identity_access.users | `users_email_key`, `users_phone_e164_key` UNIQUE global | — | login identity | Risk for SaaS | identity flows | One person cannot be staff in two tenants (SA-DB-020) | product decision | multi-tenant onboarding |
| I28 | identity_access.user_scope_memberships | UNIQUE (user_id, scope_type, scope_id, role_id) | — | membership | OK | ScopeResolver | No tenant FK on scope_id (polymorphic) and no RLS (SA-DB-005) | — | — |
| I29 | 30 soft-delete tables | 30 UNIQUE constraints, **0 partial** on `deleted_at IS NULL` (live) | e.g. coupons (brand_id, code), customers (brand_id, phone_e164) | business keys | Gap | — | A soft-deleted row blocks reuse. Contradicts `docs/SCHEMA_FULL.sql:17` convention. | Convert to partial UNIQUE … WHERE deleted_at IS NULL where reuse is intended | UX / data hygiene |
| I30 | 69 FKs (82 rows incl. partition propagations) | **no leading supporting index** (live, `fk_noindex.txt`) | e.g. `tenancy_org.stores.franchise_id`, `laundry_fulfillment.fulfillment_unit.order_item_id`, `commerce.payments.payment_method_id`, `commerce.subscription_invoices.payment_id`, `identity_access.otp_codes.user_id`, `refresh_tokens.parent_token_id`, `kernel.outbox_consumed_events.event_id`, `order_lifecycle.delivery_schedules.(brand_id,store_id,address_id)` | FK checks on parent DELETE/UPDATE | Gap | `kernel.purge_brand` deletes every brand table; customer erasure | Seq scans of child tables per parent delete | Add the indexes for high-churn parents (orders/order_items, payments, users, outbox) | slow purge / erasure, lock time |
| I31 | 55 indexes | **prefix-redundant or exact duplicates** (live, `redundant_idx.txt`) | e.g. `idx_payments_brand_id_fk`⊂`idx_payments_status`, `idx_couponred_order`⊂`idx_coupon_redemptions_order_id_fk`, `idx_invoices_order`⊂`invoices_order_id_key`, `idx_garments_tag`⊂`garments_tag_code_key` | — | Redundant | — | write amplification on hot tables (orders, payments, fulfillment_unit) | Drop after a `pg_stat_user_indexes` review in production (not available here) | write throughput |
| I32 | 14 brand tables | **no brand_id-leading index** (live) | `subscription_usage_ledger`, `subscription_billing_attempts`, `ticket_messages`, `rider_ratings`, `delivery_schedules`, `partner_bookings`, salon tables … | RLS / tenant filter | Gap (small tables today) | — | brand-scoped scans | Add (brand_id, …) where they grow | low now |
| I33 | all restrictive-policy tables (39) | `rls_subbrand_scope` → `kernel.within_scope_cols()` plpgsql | per row | sub-brand scope | Risk (E3 vs E5: brand-A count of 30k orders **384 ms with RLS vs 48 ms bypass**, synthetic) | every list/count/report | Non-inlinable per-row function | Rewrite as a SQL-inlinable predicate, or wrap scope nodes in `(SELECT …)` so they evaluate once | ~8× CPU on scans (SA-DB-017) |
| I34 | EF model vs DB | 128 `HasDatabaseName` declarations | — | DB-first mapping | OK – 126/128 exist. The 2 misses are PostgreSQL's 63-character name truncation (`subscription_invoices_customer_subscription_id_billing_peri_key`, `franchise_subscription_invoic_…`) | `Persistence/Configurations/**` | cosmetic | align names | none |
| I35 | docs/SCHEMA_FULL.sql | — | — | reference | Stale | — | 102 tables in a single schema, with raw-cast `app.current_brand_id` RLS. Live has 162 tables in 14 schemas with kernel-helper policies. | Regenerate from `pg_dump --schema-only` or mark it historical | misleads reviewers |

## Idempotency matrix

Classification: **TI** = truly idempotent (DB-enforced under concurrency) · **SD** = sequential-duplicate-only (a check-then-act protects retries, not concurrent requests) · **UP** = unprotected.

| operation | entry point | existing protection | DB enforcement | concurrency behaviour | retry behaviour | status | race conditions | remediation |
|---|---|---|---|---|---|---|---|---|
| Razorpay order create (InitiatePayment) | customer payments API → `CustomerPaymentHandlers.cs:35-80` | lookup by (key, brand, customer) | global UNIQUE `payments.idempotency_key` | The 2nd concurrent request calls `CreateOrderAsync` at the gateway, then hits 23505 → 409 | Same key returns the existing payment | SD (+ DB backstop; external side effect duplicated) | an orphan Razorpay order is created per race | Insert a `pending` row first (claim on unique key), then call the gateway |
| Payment verify | `VerifyPaymentHandler` (`CustomerPaymentHandlers.cs:~130-175`) | `status=='pending'` check | none (no concurrency token) | Verify and webhook can both see `pending` and both write `captured` plus an outbox event | 2nd sequential call → "already captured" | SD | duplicate `payment.captured` outbox events | Guarded `UPDATE … SET status='captured' WHERE id=… AND status='pending'` (rows==1 check) |
| Razorpay webhook (captured/failed) | `POST /api/v1/webhooks/razorpay` → `RazorpayWebhookHandler.cs:70-310` | HMAC per brand (SEC-2), status check | none; `gateway_order_id` unindexed | as above; seq-scan lookup (E1) | Razorpay redelivery after commit → no-op | SD | duplicate events and notifications | guarded update + unique (gateway, gateway_order_id) |
| Refund (admin) | `AdminPaymentHandlers.cs:85-230` | idempotency key lookup + app cap check; gateway refund inside tx (`:220`) | partial UNIQUE key; cap trigger `payment_idempotency.sql:29-80` | **Reproduced: two concurrent 60 refunds on a 100 payment both committed (120 > 100)** | same key → existing refund | SD (cap UP) | over-refund at DB and at gateway | `FOR UPDATE` on the payment in the trigger and in the handler |
| Cancellation refund | `OrderCancellationRefund.cs:16-75` | key `cancel_refund_{orderId}` + AnyAsync | partial UNIQUE | 2nd insert gets 23505 | idempotent | TI (row) / cap racy as above | — | as above |
| Mandate charge (subscription renewal) | `SubscriptionBillingService.cs:343-383` → `GatewaySubscriptionCharger.cs:25-38` → `Razorpay-Idempotency` header | idemKey per invoice+attempt | UNIQUE attempt key, but inserted **after** the charge | Two worker replicas both charge, then one insert fails | depends on Razorpay honouring the header (**Not Verified**) | SD/UP across replicas | double debit | claim row first; single-runner lease (advisory lock) |
| Subscription invoice issuance | `SubscriptionBillingService.cs:~125-275` | lookup | UNIQUE (subscription_id, billing_period_start) | 2nd insert gets 23505 | idempotent | TI | — | — |
| Order creation (POS / customer / admin) | `CreateOrderCommand.cs:62-95, 757-801` | `metadata @> {"idempotency_key"}` lookup | **none** | Two concurrent requests with the same key create **two orders** (and two coupon redemptions, two package debits) | sequential retry → existing order | SD | double-tap duplicates | idempotency column + UNIQUE, catch 23505 and return the winner (copy the pickup pattern) |
| Order number | `order_lifecycle.next_order_number()` | atomic `INSERT … ON CONFLICT DO UPDATE … RETURNING` | per (brand, store, year) PK | serialised by row lock | n/a | TI (generator) | — | — |
| Pickup scheduling + slot reservation | `PickupCommands.cs:338-470` | fast-path lookup; tx with atomic slot `UPDATE … booked_count<capacity`; 23505 catch by constraint name | partial UNIQUE (customer_id, idempotency_key); CHECK booked_count≤capacity | loser rolls back, including the slot increment | returns the winner | **TI** | none found | — (reference pattern) |
| Pickup reschedule | `CustomerPickupCommands.cs:184-244, 367-382` | atomic slot decrement/increment | CHECK | atomic | — | TI (capacity) | — | — |
| Booking number generation (PKP-/EXP-/WB-/tags/AGR-) | see I18–I20 | COUNT(*)+1 | global UNIQUE | concurrent creates in the same brand collide (23505); cross-brand collisions **reproduced** | retry may succeed | UP | DoS | sequences per brand + brand-scoped unique |
| Coupon redemption | `CustomerCouponHandlers.cs:66-152`; `CreateOrderCommand.cs:276-330,757-771` | app checks (global, per-customer, one-per-order) then `CurrentUsageCount++` | none | lost update on `current_usage_count`; per-customer and per-order limits exceeded concurrently | sequential retry is blocked by the app check | SD | over-redemption | guarded atomic UPDATE + partial UNIQUEs (I12) |
| Wallet top-up credit | `CustomerWalletHandlers.cs:128-212` | lookup `topup_{paymentId}` | global UNIQUE idempotency_key (rolls back the balance change too) | 2nd concurrent request gets 23505 (no double credit) | idempotent | TI (credit) | — | — |
| Wallet balance mutation (top-up / adjust / refund-to-wallet) | `CustomerWalletHandlers.cs:181`, `AdminWalletHandlers.cs:~139`, `AdminPaymentHandlers.cs:184` | read → C# compute → `UPDATE … SET balance=<value>` | CHECK balance≥0 only; `version` not a concurrency token | **lost update reproduced in SQL** (100 +50 / −30 → 150) | — | UP | silent balance corruption, ledger BalanceAfter wrong | `UPDATE … SET balance=balance+@d WHERE id=@id AND balance+@d>=0 RETURNING balance`, or `FOR UPDATE` |
| Partner wallet debit/credit | `PartnerWalletHandlers.cs:230-310` + `CommerceDbContext.cs:111-125` (`FOR UPDATE`) | lookup + row lock | UNIQUE (partner_id, idempotency_key) | serialised | idempotent | **TI** | — | — (reference pattern) |
| Partner-booking debit (inbox) | `PartnerBookingDebitService.cs:103-150` | anti-join on `outbox_consumed_events` | PK (consumer, event_id) + debit idempotency | single instance assumed; dupes absorbed by the debit key | re-delivery safe | TI | — | — |
| Paylink webhooks (brand invoice / partner) | `core…/ProcessPaylinkWebhook.cs:55-77`; `ProcessPartnerPaylinkWebhook.cs:91-113` | status check; partner top-up key from notes | partner: UNIQUE (partner_id, key); brand invoice: none | brand invoice: idempotent state set; partner: TI | — | TI / SD (harmless) | — | — |
| Outbox relay | `OutboxEventRelayService.cs:75-200` | SELECT then UPDATE status='publishing' **WHERE id only** | none | **Reproduced: two sessions both claimed the same event** | crash after claim leaves the row in `publishing` forever (no reaper found) | SD | duplicate publish with >1 replica; event loss on crash | `UPDATE … WHERE id=@id AND status IN ('pending','failed')` or `FOR UPDATE SKIP LOCKED`; lease timeout for `publishing` |
| Notification dispatch | `NotificationDispatcherService.cs:105-200` | same claim pattern (`sending`) | UNIQUE enqueue key | same as outbox | same | SD | duplicate SMS/WhatsApp across replicas | same |
| Notification enqueue | `NotificationMappingService.cs:284-322` | AnyAsync + key | UNIQUE | 23505 on race | idempotent | TI (per event) | duplicate business events still produce duplicate notifications | dedupe events at source |
| Rider offer accept | `OfferActions.cs:39-96` | status check + "no other accepted" check | none | two riders accepting sibling offers concurrently → two accepted assignments (Partially Verified) | — | SD | double dispatch | partial UNIQUE (pickup_request_id) WHERE status IN (accepted, assigned, started, arrived) |
| Retention purge | `RetentionSweepService.cs:200-250` → `kernel.purge_brand` | per-brand tx, purge raises on stall | — | single instance assumed | rerun-safe | TI (delete) | — | — |

## RLS coverage matrix

Effective app role in every row is `app_user` (NOSUPERUSER, NOBYPASSRLS, owns nothing). "Forced" only matters for owners; owners are superuser, so it has no effect. The full table-by-table list is in scratch `rls_tables.txt` and `policies.txt`.

| schema.table (group) | RLS | forced | policies (cmd · USING · WITH CHECK) | roles | bypass risks | live test (as app_user) | recommendation |
|---|---|---|---|---|---|---|---|
| order_lifecycle.orders (partitioned), order_items, pickup_requests, delivery_slots, delivery_slot_bookings, delivery_assignments, delivery_schedules, order/invoice_number_sequences | on | no | `rls_brand` ALL: `rls_bypass() OR brand_id=current_brand_id()` (+ public-role variant on schedules/sequences) **AND RESTRICTIVE** `rls_subbrand_scope` ALL: `rls_bypass() OR within_scope_cols(brand, franchise, store, warehouse)` (both arms) | app_user / public | composite FKs absent (SA-DB-004); bypass GUC self-settable (SA-DB-015) | A sees 1 / B invisible; insert of a B row denied; UPDATE/DELETE on B = 0 rows; **customer token (`scope_nodes` absent → `'?'`) sees 0 own orders and cannot INSERT orders or pickups** (SA-DB-001) | fix SA-DB-001 |
| order_lifecycle.order_addons, order_notes, order_status_history, invoices | on | no | rls_brand (+ inert `rls_admin_only` OR'd on addons) | app_user/public | as above | brand isolation OK (static + generic test) | — |
| commerce.payments, wallet_accounts/transactions, payment_refunds, coupon_redemptions, customer_packages, loyalty_points_ledger, package_usage_ledger | on | no | `rls_brand_or_customer` ALL: `bypass OR (brand=cur AND (cur_customer IS NULL OR customer_id=cur_customer))` (payments + RESTRICTIVE subbrand) | app_user | as above | payments A/B isolation OK; customer sees 0 payments (restrictive) | — |
| commerce.coupons, packages, promotions, payment_methods, loyalty_programs, subscription_* (incl. legacy-named `custsub_/subinv_/subplan_/mandate_tenant` using kernel helpers) | on | no | brand only | app_user | — | covered by generic test | — |
| commerce.partner_wallet_accounts/transactions, partner_invoices; logistics.partners, partner_users, partner_bookings | on | no | `rls_partner`: `bypass OR partner_id=current_partner_id()` | app_user | AssignPartnerDispatch uses `SET LOCAL app.bypass_rls` inside its tx (`AssignPartnerDispatch.cs:84,120`) | not run (repo has Testcontainers tests `PartnerRlsTests.cs`) | — |
| logistics.partner_dispatches | on | no | `rls_partner_or_brand` | app_user | — | not run | — |
| customer_catalog.* (17) | on | no | rls_brand (+ subbrand on price_lists) | app_user / public | **`customer_identities.custident_tenant` uses a raw `current_setting('app.current_brand_id',true)::uuid` cast** (`db/migrations/0001_…up.sql:49-54`) | **ERROR `invalid input syntax for type uuid: ""` when brand GUC='' — even with bypass='true'** (planner evaluates the cast) | rewrite with `kernel.current_brand_id()` (SA-DB-012) |
| tenancy_org.brands, platforms | on | no | `rls_admin_only`: `rls_bypass()` | app_user | read via SECURITY DEFINER helpers (`brand_status`, `resolve_brand_domain`, …) | — | — |
| tenancy_org.franchises, stores, warehouses, territories, holidays, operating_hours, store_warehouse_mappings, brand_domains, brand_cancellations(F), onboarding_progress(F) | on | 2 forced | rls_brand (+ subbrand on stores, warehouses, mappings) | app_user/public | no composite FK (a store of brand B accepted on a brand-A order) | A/B stores isolation OK | — |
| finance_royalty.* (12) | on | no | rls_brand / legacy-named helper policies; subbrand on 10 | app_user | — | expense cross-tenant **number collision** (SA-DB-007) | — |
| laundry_fulfillment.* (11) | on | no | rls_brand (+ subbrand on 7) | app_user | — | — | — |
| logistics riders, rider_* (9) | on | no | rls_brand (+ subbrand on 5) | app_user/public | — | — | — |
| engagement_cms (11 of 13) | on | no | rls_brand / rls_brand_or_customer | app_user/public | `notification_event_catalog`, `notification_event_cursors` have no RLS (no brand column; global) | — | — |
| kernel.system_settings, feature_flags | on | no | `rls_brand_or_platform`: USING `bypass OR brand_id IS NULL OR brand=cur`; WITH CHECK `bypass OR brand=cur` | app_user | — | — | — |
| kernel.file_attachments, outbox_events | on | no | rls_brand | app_user | workers run with bypass | — | — |
| identity_access.users | on | no | select/update/delete: `bypass OR id=cur_user OR user_in_brand(id, cur_brand)` (SECURITY DEFINER helper); **insert WITH CHECK `true`** | app_user | any app_user session can insert users (global table) | not separately run | WITH CHECK `rls_bypass()` or the brand-membership predicate |
| identity_access.roles | on | no | split per cmd; select includes `brand_id IS NULL` | app_user | brand-B role invisible to A, **but A can reference it in a membership** (FK ignores RLS) | reproduced (SA-DB-005) | — |
| identity_access.audit_logs (partitioned), api_keys(F), api_key_usage(F), impersonation_grants(F), brand_feature, brand_platform_subscription/invoice, oauth_authorization_codes | on | 3 forced | rls_brand (+ subbrand on audit_logs) | app_user/public | audit writes from customer sessions also blocked by the restrictive policy (scope '?') | — | — |
| identity_access.user_permission_override | on | no | rls_user_self (public) | — | — | — | — |
| **identity_access.user_scope_memberships, user_profiles, login_history, otp_codes, refresh_tokens, password_resets, permissions, role_permissions** | **off** | — | policies exist but **inert** (`rls_user_self` / `rls_admin_only`) | — | any app_user session reads every tenant's memberships, profiles (PII), token/OTP hashes and login history; writes cross-tenant memberships | reproduced: A context inserted a membership granting a B role; counts across all brands visible | Enable with brand-aware policies (membership → brand via scope) or move writes behind SECURITY DEFINER functions (SA-DB-005) |
| identity_access catalogues (features, modules, module_bundle, bundle_feature, permission_groups, role_presets, vertical_templates/terms, oauth_clients) | off | — | none | — | global reference data; app_user has write grants | — | REVOKE INSERT/UPDATE/DELETE from app_user on catalogue tables |
| authz.policy | on | no | `rls_brand_or_platform` | app_user | **the ABAC store reads on a separate NpgsqlDataSource with no GUCs, so brand-authored policies are invisible** | reproduced: 58 platform policies visible, 0 brand policies | SA-DB-014 |
| authz.policy_condition | on | no | `rls_via_policy` (EXISTS on parent) | app_user | — | — | — |
| authz.decision_log (partitioned) | on | no | rls_brand (WITH CHECK) | app_user | **`COPY FROM` is rejected under RLS** | reproduced: `COPY FROM not supported with row-level security` | INSERT batches or a SECURITY DEFINER writer (SA-DB-014) |
| authz.action / attribute / resource_type | off | — | — | — | global | — | revoke writes |
| salon_fulfillment.* (4) | on | no | **legacy raw-cast `*_tenant` (public)** + RESTRICTIVE subbrand | app_user has **no USAGE on schema** | unused by backend (0 C# references) | `permission denied for schema salon_fulfillment` | fix before enabling the salon vertical (SA-DB-021) |
| analytics.* (7 materialized views) | n/a (MVs cannot have RLS) | — | — | app_user SELECT | **brand A session read brand B rows** | reproduced (76 + 76 rows) | per-brand security-barrier views with `security_invoker`, or keep strict app filtering (SA-DB-013) |
| authz views (`rls_drift`, `parity_*`) | — | — | — | not granted to app_user | — | — | — |
| **SECURITY DEFINER functions (22, all owned by superuser)** | — | — | — | **all executable by app_user** | `kernel.purge_brand`, `export_brand`, `set_brand_suspension`, `set_brand_cancellation_state`, `ensure_brand_subdomain`, `request_impersonation` … trust their argument; none checks `p_brand_id = current_brand_id()` | **purge_brand(B) from an A session deleted all B rows (rolled back); export_brand(B) returned all B data** | SA-DB-003 |

## Cross-cutting integrity

- **Tenant-scoped uniques include brand_id?** Partly. Catalogue codes do: coupons, services, items, roles, stores, franchises. 46 unique indexes on brand-owned tables omit brand_id (scratch `uniq_nobrand.txt`). Most are business numbers and idempotency keys, which allows cross-tenant collisions. That was reproduced for expenses (SA-DB-007).
- **Composite FKs preventing cross-tenant references?** **None.** 0 of 617 FKs include brand_id. A brand-A order referencing brand-B franchise, store and customer was accepted, and so was a brand-A payment referencing a brand-B order (SA-DB-004). FK checks run as the table owner and ignore RLS.
- **DB constraints for business invariants.** There are 322 CHECK constraints in all, covering statuses and channels. Value checks include payments amount>0, wallet balance≥0, refund amount>0, slot booked_count≤capacity, and coupon types. There are **no** CHECKs on order money totals: subtotal, discount ≤ subtotal, grand_total ≥ 0. Status *transitions* are enforced only in the app (state machine).
- **Booking conflict prevention.** There are **0 exclusion constraints** in the database. Slot capacity is protected atomically (good). Salon appointment and resource overlap has no DB guard, and the salon schema is not wired to the backend.
- **Soft delete + uniqueness.** 0 of 30 uniques on soft-delete tables are partial (I29).
- **Migration downs.** The 0025–0033 down→up round trip on the live scratch DB restored an identical policy dump (`diff` empty), RLS count (136) and index count (1,649). The downs for 0027 and 0029 intentionally **disable RLS** on the tables they own: an exact rollback that reduces isolation. Operators should treat `migrate.sh down` past 0027 as a security change.
- **Fresh-environment reproducibility.** It is broken (SA-DB-002). CI (`.github/workflows/ci.yml:32-33`) runs only Testcontainers tests over hand-trimmed fixtures (`RbacRlsFixture.cs:9-16`), never the real build.
- **Application vs database control.**
  - Brand isolation: both layers.
  - Sub-brand (franchise/store/warehouse) scope: DB layer since 0031, but it breaks customers.
  - Customer-vs-customer: DB layer only on the 8 `rls_brand_or_customer` tables, and only when `app.current_customer_id` is set. Elsewhere (e.g. `customer_addresses`, `customers`) it is app-only.
  - Role assignment, materialized views and SECURITY DEFINER lifecycle ops: app-only.
  - Idempotency: mostly app-only (see the matrix).

## Findings

### SA-DB-001 — Customer sessions blocked by RESTRICTIVE `rls_subbrand_scope`: customers cannot read or create orders, pickups or payments
- Category: RLS correctness / availability
- Severity: Critical
- Status: Verified (code traced end to end; DB behaviour reproduced with interceptor-identical GUCs; HTTP not executed)
- Related area: AUTH, ORDERS
- Evidence:
  - `core.Infrastructure/Auth/JwtTokenService.cs:98-119`: customer tokens carry only `sub`, `token_use`, `brand_id`, `phone`, and **no `scope_nodes`**. User tokens always get it (`:59-64`).
  - `HttpContextCurrentTenant.cs:53`: `ScopeNodes => Claim("scope_nodes")` is null for customers.
  - `RlsConnectionInterceptor.cs:85-90,103`: null is written as `'?'`.
  - `kernel.split_setting` turns `'?'` into NULL. `within_scope_cols` then returns NULL (`db/migrations/0031_subbrand_scope_rls.up.sql:60-110`), and the policy is created `AS RESTRICTIVE` (`:198`) on 39 tables, including orders, order_items, pickup_requests, payments, stores, delivery_slots, delivery_slot_bookings, audit_logs and order_number_sequences.
- Observed behaviour (live, app_user, brand A, customer c1, scope '?'):
  - `SELECT count(*) FROM orders WHERE customer_id=c1` → **0**; the superuser count is 7.
  - payments → 0; stores → 0.
  - `INSERT INTO orders …` → `new row violates row-level security policy "rls_subbrand_scope"`. Same for `pickup_requests`.
- Reproduction: commands run inline as app_user during this audit (not saved to a scratch file). Steps:
  1. Set the GUCs as the interceptor does for a customer JWT.
  2. Query orders, payments and stores.
  3. Insert an order and a pickup request.
- Impact: in the documented production configuration (app_user plus migrations through 0033), the customer app cannot list its orders, place orders, schedule pickups, see payments or stores, or perform any audited write. The slot-booking `UPDATE … WHERE booked_count<capacity` affects 0 rows, so it reports "slot full". Partner and API-key principals without `scope_nodes` are affected the same way on those tables.
- Recommended remediation (smallest safe change): make `within_scope_cols` return TRUE when `kernel.current_customer_id() IS NOT NULL` (customer principals are already confined by brand plus customer predicates). Alternatively, emit a resolved `scope_nodes` (e.g. `brand:<id>`) in customer tokens. The DB-side fix is preferable because it does not depend on token rotation.
- Regression tests required: Testcontainers RLS test with customer-token GUCs on orders, payments, pickup_requests and audit_logs (SELECT and INSERT); an HTTP smoke test for customer order create, list and pickup.
- Dependencies / priority: P0. Blocks any production use of the customer app.
- Prior-doc cross-ref: the 0031 header cites audit finding A-6; that change introduced this regression.

### SA-DB-002 — The documented fresh-build path cannot reproduce the production schema; re-running it silently reverts the RLS bypass fix
- Category: Migrations / operability
- Severity: High
- Status: Verified
- Related area: OPS
- Evidence:
  - `deploy/README.md:35` and `ops/backup/README.md:5` prescribe `db/build_from_scratch.sh` + `db/tools/migrate.sh up`.
  - `build_from_scratch.sh:77-120` applies only the FK patches, triggers, discriminators, token lineage and `rls_proposal.sql`.
  - ~130 other patches (`rls_enable_*`, `harden_app_user_and_rls_bypass.sql`, `payment_idempotency.sql`, `subscriptions_module.sql`, `seed_navigator_modules.sql`, …) are applied by no ordered script.
- Observed behaviour:
  - `migrate.sh up` fails at `0005_split_features_from_modules.up.sql:102` (`relation "identity_access.modules" does not exist`).
  - Several patches assert on rows created only by the .NET `IdentitySeeder` (`phase1_slice_e…:92`, `phase4_role_vertical_key.sql:113`).
  - Seeds need a hand-created brand `5b375161-…`.
  - `build_from_scratch.sh:89-90` re-runs `rls_proposal.sql`, whose `kernel.rls_bypass()` (`rls_proposal.sql:85-87`) accepts only `'on'`. That overwrites the hardened version (`harden_app_user_and_rls_bypass.sql:31-38`), which accepts the `'true'` the interceptor sends (`RlsConnectionInterceptor.cs:65`). The script header claims it is "safe to re-run against an existing database" (`:6-8`).
- Reproduction: commands in Scope and method; logs `migrate1.log`, `patch_pass.log`.
- Impact:
  - Disaster recovery, staging and new-region builds are not reproducible from the repo.
  - CI never validates the real DDL.
  - A re-run on production turns every platform-admin and worker bypass into zero rows. That fails closed, but it is an outage.
- Recommended remediation: freeze a baseline (`pg_dump --schema-only` of production) as `db/migrations/0000_baseline.up.sql`. Retire `db/patches/` from the bootstrap. Make `rls_proposal.sql` stop redefining `rls_bypass()`. Add a CI job that builds a PostgreSQL service from baseline + migrations and runs `migrate.sh verify` plus RLS smoke tests.
- Regression tests required: a CI job that builds the schema; an assertion that `kernel.rls_bypass()` returns true for `'true'`.
- Dependencies / priority: P1.

### SA-DB-003 — SECURITY DEFINER brand-lifecycle functions are callable by `app_user` with any brand id; `purge_brand` was explicitly meant not to be
- Category: RLS bypass / privilege
- Severity: High
- Status: Verified
- Evidence:
  - `db/migrations/0015_brand_cancellation.up.sql:143-165` (`export_brand`, granted to app_user, "trusts its argument"), `:175-250` (`purge_brand`; `:248-250` "REVOKE ALL … FROM PUBLIC — NOT granted to app_user").
  - `harden_app_user_and_rls_bypass.sql:59-67` sets `ALTER DEFAULT PRIVILEGES IN SCHEMA kernel GRANT EXECUTE ON FUNCTIONS TO app_user`. Every later kernel function therefore gets an explicit app_user grant, which `REVOKE … FROM PUBLIC` does not remove.
  - Live: 22 SECURITY DEFINER functions, all owned by superuser, all executable by app_user.
- Observed behaviour: from a brand-A session, `kernel.export_brand('B')` returned all of B's rows (7 tables). `kernel.purge_brand('B')` deleted every B row (rolled back). Neither function compares its argument with `kernel.current_brand_id()`.
- Impact: any SQL execution as app_user (an injection, or a single unchecked handler argument) can exfiltrate or destroy another tenant. `RetentionSweepService.cs:241` actually *depends* on this accidental grant, because it runs on the app_user connection.
- Recommended remediation:
  1. `REVOKE EXECUTE ON FUNCTION kernel.purge_brand(uuid) FROM app_user`, and run the purge worker on a dedicated `app_maintenance` role.
  2. Inside every brand-taking SECURITY DEFINER function, add `IF NOT kernel.rls_bypass() AND p_brand_id IS DISTINCT FROM kernel.current_brand_id() THEN RAISE …`.
  3. Replace `GRANT EXECUTE ON ALL FUNCTIONS` default privileges with explicit grants.
- Regression tests required: an app_user cross-brand call to each function must raise.
- Dependencies / priority: P1.

### SA-DB-004 — No composite tenant foreign keys: rows can reference another tenant's parents
- Category: Integrity / tenant isolation
- Severity: High
- Status: Verified
- Evidence: live, 0 of 617 FKs include brand_id. Example: `fk_patch_06_commerce.sql`-style `FOREIGN KEY (order_id, order_created_at) REFERENCES orders(id, created_at)` (`db/HANDOFF.md` §5).
- Observed behaviour: as app_user in brand A:
  - `INSERT orders(brand_id=A, franchise_id=B-franchise, store_id=B-store, customer_id=B-customer)` → INSERT 1.
  - `INSERT payments(brand_id=A, order_id=B-order)` → INSERT 1.
- Impact: RLS WITH CHECK only validates the row's own brand_id. A handler that trusts a client-supplied id creates cross-tenant links. Those links then leak through joins: the B-store name appears on an A invoice, and the B order is updated through the A payment.
- Recommended remediation: add `UNIQUE (brand_id, id)` on parents and composite FKs `(brand_id, x_id) REFERENCES parent(brand_id, id)` for the hot paths: orders↔customers/stores/franchises, payments↔orders, order_items↔orders, pickup_requests↔stores/customers. Use `NOT VALID` then `VALIDATE` for a zero-downtime rollout.
- Regression tests required: a cross-brand FK insert must fail.
- Dependencies / priority: P1.

### SA-DB-005 — Identity tables without RLS: cross-tenant role grants and PII/token reads are possible at the DB layer
- Category: RLS coverage
- Severity: High
- Status: Verified
- Related area: AUTHZ, IDENTITY
- Evidence: live, `user_scope_memberships`, `user_profiles`, `login_history`, `otp_codes`, `refresh_tokens`, `password_resets`, `permissions` and `role_permissions` have `relrowsecurity=f`, and their policies from `rls_proposal.sql` are inert. `identity_access.users` has `rls_users_insert WITH CHECK true`.
- Observed behaviour: from a brand-A session, an INSERT into `user_scope_memberships` granting a user brand B's `brand_owner_b` role succeeded. The role itself was invisible to A under RLS, but the FK ignores RLS. The session could also count memberships of all brands.
- Impact: escalation across tenants depends entirely on app checks in the access-control handlers. Token/OTP hashes and profiles of every tenant are readable by any app_user query.
- Recommended remediation: enable RLS on `user_scope_memberships` with a policy deriving the brand from (scope_type, scope_id) via a SECURITY DEFINER helper, or add a `brand_id` column. Enable `rls_user_self` on the token/OTP/profile tables, keeping the bypass for auth paths. Restrict the `users` INSERT check.
- Regression tests required: an A session cannot insert a membership whose role, scope or brand is B.
- Dependencies / priority: P1.

### SA-DB-006 — The refund cap (trigger + app check) is not concurrency-safe: over-refund reproduced
- Category: Idempotency / financial integrity
- Severity: High
- Status: Verified
- Evidence:
  - `db/patches/payment_idempotency.sql:29-80`: the trigger sums sibling refunds with no lock on the payment.
  - `AdminPaymentHandlers.cs:110-118`: an app SUM check, then a gateway refund inside the transaction at `:220`.
- Observed behaviour: two concurrent 60.00 refunds on a 100.00 payment both committed; `sum=120.00`, captured 100.00. A sequential third refund was correctly rejected.
- Impact: double refund at Razorpay and in the ledger under double-click or parallel admin activity.
- Recommended remediation: `PERFORM 1 FROM commerce.payments WHERE id = NEW.original_payment_id FOR UPDATE;` at the top of the trigger, and the same `FOR UPDATE` in the handler before calling the gateway.
- Regression tests required: a parallel refund test (two connections).
- Dependencies / priority: P1.

### SA-DB-007 — Globally unique business numbers generated per tenant: cross-tenant unique violations and an existence oracle
- Category: Integrity / multi-tenancy
- Severity: High
- Status: Verified (expense reproduced; others by code + schema)
- Evidence:
  - `ExpenseCommands.cs:204-205` `EXP-{yyyyMMdd}-{brandCount+1}` against `database_scripts/07_bc7_finance_royalty.sql:150` `expense_number … UNIQUE`.
  - `CreateWarehouseBatch.cs:48-49` `WB-{yyyyMMdd}-{count+1}`.
  - `PickupCommands.cs:61-63` and `CreateParcelOrderCommand.cs:163-166` `PKP-{yyyy}-{brandId[..4]}-{count+1}`.
  - `GenerateTags.cs:34-42` `LG-{brandId[..4]}-{count+i}`.
  - `SaveCommercials.cs:53` `AGR-{franchiseCode}-{year}`.
- Observed behaviour: brand A inserted `EXP-20261009-00001`. Brand B, seeing 0 expenses, generated the same number and got `duplicate key value violates unique constraint "expenses_expense_number_key"`.
- Impact:
  - Tenant B cannot create expenses or batches whenever its count equals another tenant's on the same day.
  - The error reveals another tenant's activity.
  - COUNT+1 also races inside a tenant and costs O(n) per insert (E3: 384 ms for a 30k-row count under RLS, synthetic).
- Recommended remediation: brand-scoped uniques `(brand_id, number)`, plus per-brand counters using the existing `next_order_number` pattern (`INSERT … ON CONFLICT DO UPDATE … RETURNING`).
- Regression tests required: two brands creating their first expense, batch and pickup on the same day; concurrent creates in one brand.
- Dependencies / priority: P1.

### SA-DB-008 — Wallet and other balance/counter mutations are read-modify-write with no lock or concurrency token
- Category: Concurrency
- Severity: High
- Status: Partially Verified (DB lost update reproduced with the same statement shape; app pattern traced)
- Evidence:
  - `CustomerWalletHandlers.cs:149-212` (`wallet.Balance += …; SaveChanges`), `AdminWalletHandlers.cs:~105-172`, `AdminPaymentHandlers.cs:~180-215`.
  - No `IsConcurrencyToken`/xmin in any EF configuration. `wallet_accounts.version` exists but is not a token.
  - Similar for `coupon.CurrentUsageCount++` and `promotion.RedemptionsCount++` (`CreateOrderCommand.cs:771-797`).
- Observed behaviour: two concurrent transactions read balance 100, wrote 150 and 70; final 150. The correct value is 120.
- Impact: silent money corruption. Ledger `BalanceAfter` disagrees with `wallet_accounts.balance`.
- Recommended remediation: atomic SQL updates (`balance = balance + @delta … RETURNING`), or configure `UseXminAsConcurrencyToken()` on WalletAccount, Coupon, Promotion and Payment, and handle `DbUpdateConcurrencyException` with a retry.
- Regression tests required: parallel top-up plus debit.
- Dependencies / priority: P1.

### SA-DB-009 — Background workers claim rows without a guarded update or SKIP LOCKED: duplicate publish/charge across replicas, stuck claims
- Category: Idempotency / jobs
- Severity: Medium
- Status: Verified (outbox claim race reproduced); Partially Verified (dispatcher, billing)
- Evidence:
  - `OutboxEventRelayService.cs:102-118`: SELECT, then `UPDATE … WHERE id` (EF, no status predicate).
  - `NotificationDispatcherService.cs:113-130`: same.
  - `SubscriptionBillingService.cs:353-383`: charge before the attempt row.
  - No `FOR UPDATE SKIP LOCKED` or advisory lock anywhere (grep).
  - No reaper for `publishing`/`sending`.
- Observed behaviour: session A and session B both read `pending` and both got `UPDATE 1`, so both would publish.
- Impact: today this is safe only while exactly one commerce replica runs. Horizontal scaling causes duplicate events, notifications and possibly mandate debits. A crash between claim and outcome strands rows permanently.
- Recommended remediation: claim with `UPDATE … SET status='publishing', claimed_at=now() WHERE id IN (SELECT id … FOR UPDATE SKIP LOCKED LIMIT n) RETURNING *`; add a lease timeout; insert the billing attempt in `initiated` state before charging; or take `pg_try_advisory_lock` per job.
- Regression tests required: two-worker relay test.
- Dependencies / priority: P2 (P1 before scaling out).

### SA-DB-010 — Coupon usage limits enforced only in the application
- Category: Idempotency / invariants
- Severity: Medium
- Status: Partially Verified (code + schema; no DB guard exists to test)
- Evidence: `CustomerCouponHandlers.cs:85-142`; `CreateOrderCommand.cs:292-311, 770-771`; live `coupon_redemptions` has no unique beyond the PK.
- Impact: concurrent redemptions exceed `max_total_uses` and `max_uses_per_customer`, and "one coupon per order" can be broken.
- Recommended remediation: guarded `UPDATE commerce.coupons SET current_usage_count = current_usage_count + 1 WHERE id=@id AND (max_total_uses IS NULL OR current_usage_count < max_total_uses)` (rows==1), plus partial UNIQUE `(order_id) WHERE reverted_at IS NULL`. For single-use, add UNIQUE `(coupon_id, customer_id) WHERE reverted_at IS NULL`, applicable when `is_single_use_per_cust`; this needs a trigger or a separate table.
- Regression tests required: parallel redemption.
- Dependencies / priority: P2.

### SA-DB-011 — Order-creation idempotency and payment capture are check-then-act; the webhook lookup is an unindexed cross-tenant scan
- Category: Idempotency / performance
- Severity: Medium
- Status: Partially Verified (code; EXPLAIN E1/E2 synthetic)
- Evidence: `CreateOrderCommand.cs:62-95` (jsonb metadata lookup, no unique); `RazorpayWebhookHandler.cs:96-100,191-234`; `CustomerPaymentHandlers.cs:~135-175` (status checks, EF update by PK).
- Observed behaviour: E1 shows a Seq Scan on payments, removing 60,000 rows. E2 shows the idempotency lookup at 421 ms on 30k brand rows (synthetic).
- Impact: POS double-taps create duplicate orders, along with their coupon and package side effects. Webhook and client verify can double-emit `payment.captured`, which means duplicate customer notifications. Webhook latency grows with total platform payments.
- Recommended remediation: give orders an idempotency column with a unique constraint and catch 23505 (copy `PickupCommands.cs:470-510`). Make capture a guarded `UPDATE … WHERE status='pending'`. Add a unique index on `(gateway, gateway_order_id)`.
- Regression tests required: parallel create order with the same key; webhook concurrent with verify.
- Dependencies / priority: P2.

### SA-DB-012 — Customer-identity and salon RLS policies use a raw uuid cast that throws on an empty brand GUC, even under bypass: Google customer sign-in fails
- Category: RLS correctness
- Severity: High
- Status: Verified (DB reproduced; code path traced; HTTP not executed)
- Related area: AUTH
- Evidence:
  - `db/migrations/0001_customer_social_auth_and_pin.up.sql:49-54` (`custident_tenant`: `current_setting('app.bypass_rls')='true' OR brand_id = current_setting('app.current_brand_id', true)::uuid`); `phase4_salon_fulfillment_schema.sql:90-95`.
  - The interceptor writes `''` for a null brand (`RlsConnectionInterceptor.cs:59`).
  - `/api/v1/customer/auth/google` is anonymous with bypass (`core.WebApi/Program.cs:620-646`).
  - `CustomerGoogleSignInHandler.cs:88-93` queries `CustomerIdentities`.
- Observed behaviour: with brand `''` and bypass `'true'`, `SELECT … FROM customer_identities WHERE …` → `ERROR: invalid input syntax for type uuid: ""`. The planner evaluates the cast while planning, so the bypass short-circuit does not help.
- Impact: customer Google sign-in returns a 500 in the production role configuration. This is the same DEF-002 defect class that `fix_legacy_*_rls_policies.sql` removed elsewhere.
- Recommended remediation: replace with `kernel.rls_bypass() OR brand_id = kernel.current_brand_id()`, scoped `TO app_user`. Same for the four salon policies.
- Regression tests required: anonymous plus bypass query on customer_identities.
- Dependencies / priority: P1.
- Prior-doc cross-ref: memory/patch history DEF-002.

### SA-DB-013 — Tables with tenant data that RLS cannot protect: analytics materialized views
- Category: RLS coverage
- Severity: Medium
- Status: Verified
- Evidence: 7 MVs in `analytics` with `brand_id`, granted SELECT to app_user (`harden_app_user_and_rls_bypass.sql` §4); handlers filter by brand (`GetDailyStoreRevenue.cs:29-33`, `GetDashboard.cs:33-60`).
- Observed behaviour: a brand-A session read 76 brand-A and 76 brand-B rows from `mv_daily_store_revenue`.
- Impact: isolation is app-only. Any new report endpoint that forgets `Where(BrandId)` leaks revenue and customer LTV across tenants.
- Recommended remediation: expose MVs only through `security_barrier` views filtering `brand_id = kernel.current_brand_id() OR kernel.rls_bypass()`, and revoke direct MV SELECT.
- Regression tests required: an A session sees only A rows through the views.
- Dependencies / priority: P2.

### SA-DB-014 — The ABAC store's raw connections run without tenant GUCs: brand policies, roles and entitlements are invisible, and decision-log writes are rejected
- Category: RLS × ABAC integration
- Severity: Medium
- Status: Verified (DB)
- Related area: AUTHZ
- Evidence:
  - `AbacServiceCollectionExtensions.cs:33-37` uses a separate `NpgsqlDataSource` on the same app_user connection string.
  - `NpgsqlAbacStore.cs:42-48` claims it "bypasses RLS deliberately … the role has SELECT only on these two tables"; both statements are false.
  - `:238-253` (roles / brand_feature), `:325-332`.
  - `DecisionLogWriter.cs:125-140` uses binary `COPY authz.decision_log`.
  - `0024_authz_abac_foundation.up.sql:172-182` enables RLS on decision_log.
- Observed behaviour: with no GUCs, 58 platform policies were visible and 0 brand policies; the subject query returned `role_codes={}` and `entitlements={}` while the true values were `{brand_owner_b}` and `{bookings}`. COPY gave `ERROR: COPY FROM not supported with row-level security`.
- Impact:
  - ABAC defaults to shadow/disabled (`AbacOptions.cs:15-18`), so today the parity data is wrong and every decision-log flush fails.
  - After an enforce cutover, brand policies would never apply.
  - `subject.roles contains X` deny rules would evaluate against an empty set and fail open.
- Recommended remediation: run these reads under `SET LOCAL app.bypass_rls='true'` in a transaction, or via SECURITY DEFINER read functions. Replace COPY with batched INSERT or a SECURITY DEFINER writer.
- Regression tests required: an integration test against app_user (not superuser).
- Dependencies / priority: P2 (P1 before any ABAC enforce).

### SA-DB-015 — RLS is defence-in-depth only: the bypass GUC is self-settable and platform admins bypass on every request
- Category: RLS design
- Severity: Medium
- Status: Verified
- Evidence: `kernel.rls_bypass()` reads a plain custom GUC; `TenantResolutionMiddleware.cs:37-48` bypasses for every platform-admin request (`X-Brand-Id` narrows only the app layer).
- Observed behaviour: `set_config('app.bypass_rls','true',false)` as app_user → all brands visible.
- Impact: any SQL-execution bug yields full bypass. Platform-admin endpoints have no DB backstop at all.
- Recommended remediation: move bypass to a separate DB role (e.g. `app_platform` with BYPASSRLS, or policies keyed on `current_user`) used only by the platform-admin and worker connection strings. Have platform-admin requests that specify `X-Brand-Id` set the brand GUC instead of bypassing.
- Regression tests required: an app_user session cannot obtain bypass.
- Dependencies / priority: P2.

### SA-DB-016 — Pooled-connection tenant context is safe for EF, but rests on session-level GUCs
- Category: RLS runtime
- Severity: Low
- Status: Partially Verified
- Evidence: `DependencyInjection.cs:60-64,86-96`; `RlsConnectionInterceptor.cs:30-55,93-107`.
- Observed behaviour: in SQL, a session-level `set_config` persisted into the "next request" on the same connection (orders visible), and `DISCARD ALL` cleared it. EF re-runs the interceptor on every open and writes all 12 GUCs, so the EF path is safe.
- Impact:
  - Raw `NpgsqlDataSource` paths (ABAC) do not set GUCs. That is safe today because they fail closed (SA-DB-014).
  - A transaction-pooling PgBouncer would break the model: no session affinity, and `DISCARD ALL` is unavailable.
  - Setting `No Reset On Close=true` in a connection string would make leakage depend solely on the interceptor.
- Recommended remediation: document "session pooling only" for any pooler. Consider `set_config(…, true)` inside an EF transaction for write paths. Add a startup assertion that `No Reset On Close` is not set.
- Regression tests required: pool-reuse test across two tenants.
- Dependencies / priority: P3.

### SA-DB-017 — The per-row plpgsql scope predicate makes RLS scans about 8× slower
- Category: Performance
- Severity: Medium
- Status: Verified (synthetic)
- Evidence: `0031_subbrand_scope_rls.up.sql:60-110` (`LANGUAGE plpgsql`, called per row).
- Observed behaviour (synthetic): brand-A `count(*)` over 30k orders took 384 ms with RLS and 48 ms with bypass. The E2 idempotency probe took 421 ms.
- Impact: reports, counts and number generation slow down linearly with tenant size.
- Recommended remediation: rewrite as an inlinable SQL expression over `(SELECT kernel.current_scope_nodes())`, e.g. `'platform' = ANY(nodes) OR ('store:'||store_id) = ANY(nodes) OR …`.
- Regression tests required: plan test showing an InitPlan rather than a per-row function call.
- Dependencies / priority: P2.

### SA-DB-018 — 69 FKs without supporting indexes; 55 redundant indexes
- Category: Indexing
- Severity: Medium
- Status: Verified (catalog)
- Evidence: scratch `fk_noindex.txt`, `redundant_idx.txt` (see I30/I31).
- Impact: `purge_brand`, customer erasure and parent deletes seq-scan child tables. Duplicate indexes inflate write cost on orders, payments and fulfillment_unit.
- Recommended remediation: add FK indexes on high-churn children with `CREATE INDEX CONCURRENTLY` (migration flagged `-- migrate: no-transaction`). Drop redundant indexes after confirming production `pg_stat_user_indexes`.
- Regression tests required: none (catalog check in CI).
- Dependencies / priority: P2.

### SA-DB-019 — Soft-delete tables: no partial unique constraints
- Category: Integrity
- Severity: Low
- Status: Verified
- Evidence: live, 30 of 30 uniques on tables with `deleted_at` are non-partial; `docs/SCHEMA_FULL.sql:17` states the opposite convention.
- Impact: deleted coupon, service or store codes and deleted customers' phones block reuse.
- Recommended remediation: partial uniques where reuse is a product requirement.
- Dependencies / priority: P3.

### SA-DB-020 — Staff identity is globally unique by email and phone
- Category: Multi-tenant data model
- Severity: Low
- Status: Verified
- Related area: IDENTITY
- Evidence: `users_email_key`, `users_phone_e164_key` (`UserConfiguration.cs:47-48`).
- Impact: a consultant or franchisee cannot hold accounts in two independent tenants.
- Recommended remediation: product decision. Either a global person with per-brand memberships (the current model, which then needs cross-brand membership support) or a brand-scoped unique.
- Dependencies / priority: P3.

### SA-DB-021 — No DB-level booking overlap prevention; the salon schema is inaccessible to app_user
- Category: Integrity / vertical readiness
- Severity: Low
- Status: Verified
- Related area: VERTICAL
- Evidence: 0 exclusion constraints live; `salon_fulfillment` has no USAGE for app_user (live `permission denied for schema`); 0 C# references.
- Impact: the salon vertical is DB-only and not usable. When it is wired, appointment overlap must be prevented at the DB.
- Recommended remediation: `CREATE EXTENSION btree_gist; ALTER TABLE salon_fulfillment.resource_bookings ADD EXCLUDE USING gist (resource_id WITH =, tstzrange(start_at,end_at) WITH &&) WHERE (status <> 'cancelled')`, plus grants and kernel-helper policies.
- Dependencies / priority: P3 (before salon GA).

### SA-DB-022 — Rider offer acceptance has no DB uniqueness for the active assignment
- Category: Concurrency
- Severity: Low
- Status: Partially Verified
- Evidence: `OfferActions.cs:39-96` (check "no other accepted", then update its own row); no partial unique on `delivery_assignments`.
- Impact: two riders can both hold an accepted assignment for the same pickup.
- Recommended remediation: `CREATE UNIQUE INDEX … ON order_lifecycle.delivery_assignments (pickup_request_id) WHERE status IN ('accepted','assigned','started','arrived')`.
- Dependencies / priority: P3.

## Positive controls verified

- **Runtime role is genuinely RLS-subject.** `app_user` is NOSUPERUSER and NOBYPASSRLS, owns no tables, and has no TRUNCATE, TRIGGER or schema CREATE (live). Deploy and AppHost default to it.
- **Full brand coverage.** All 126 brand-bearing tables have RLS plus policies. A/B isolation was proven live for SELECT, INSERT (WITH CHECK), UPDATE (0 rows plus a WITH CHECK on brand change) and DELETE. Unset or empty GUCs return 0 rows without errors, except the two legacy policies in SA-DB-012.
- **Pool-safe interceptor design.** Every GUC is written on every open, the `'?'` sentinel separates "unresolved" from "empty", and the interceptor is per-request scoped (`RlsConnectionInterceptor.cs:67-90`, `DependencyInjection.cs:48-64`).
- **Reference idempotency patterns:**
  - Customer pickup scheduling: partial unique plus 23505-by-constraint-name plus an atomic, CHECK-guarded slot increment (`PickupCommands.cs:338-510`).
  - Partner wallet: `FOR UPDATE` plus a `(partner_id, idempotency_key)` unique.
  - Inbox consumer: `outbox_consumed_events` PK.
  - Subscription invoice per-period unique.
  - Notification-outbox unique key.
  - Wallet top-up credit unique key.
  - Order number: atomic upsert counter.
- **Migration tooling is sound.** `migrate.sh` is transactional with checksum drift detection. The 0025–0033 down/up round trip restored identical policies, RLS state and index count.
- **EF ↔ DB index naming is consistent** (126 of 128; the 2 misses are name truncation).
- **CHECK coverage** for statuses and key money fields: payments amount>0, wallet balance≥0, refunds>0, slot capacity.
- **Partitioned parents enforce RLS correctly** (orders, process_logs, notifications_log, audit_logs, rider pings, decision_log).

## Open questions / not verified

- The production database's actual patch, migration and seed state. The repo cannot reproduce it, so the live findings assume patches were applied before migrations, as git history indicates. Check SA-DB-001, -003 and -012 against production with `pg_policies`, `has_function_privilege('app_user','kernel.purge_brand(uuid)','EXECUTE')` and a customer-token smoke test.
- Npgsql reset-on-close behaviour in the deployed Npgsql version, and whether any pooler sits in front of PostgreSQL.
- Whether Razorpay honours `Razorpay-Idempotency` on recurring charges.
- Real index usage and bloat (`pg_stat_user_indexes`, `pg_stat_statements`). Not available on a fresh cluster; none reported.
- Commerce replica count in production, which decides the real exposure of SA-DB-009.
- Partner RLS tests and the `rls_partner*` behaviour were read but not re-run live.

## Verdict inputs

| Q | Status | Justification |
|---|---|---|
| DB-Q1 PK/FK/unique/required indexes correct? | Partially Supported | PKs and partition-composite FKs are correct. 69 FKs are unindexed, 55 indexes redundant, 46 brand-table uniques omit brand_id, and there is no gateway_order_id index. |
| DB-Q2 Indexes aligned to query patterns, bottlenecks verified? | Partially Supported | Main lists are well indexed (E4, E7). The webhook seq scan (E1), outbox poll (E6), metadata idempotency (E2) and per-row RLS function (E3/E5) were verified on synthetic data only. |
| DB-Q3 Duplicates prevented under concurrency/retries? | Not Supported | Only pickup, partner wallet, inbox, subscription invoice and notification enqueue are DB-enforced. Orders, coupons, refunds, wallet balances and job claims race (two races reproduced). |
| DB-Q4 Idempotency for payments/webhooks/booking/orders/jobs? | Partially Supported | Booking (pickup and slot) is truly idempotent. Payments and webhooks are sequential-only. Order creation is sequential-only. Jobs are single-instance only. |
| DB-Q5 DB supports RLS? | Fully Supported | PostgreSQL 16 RLS with kernel helper functions, restrictive and permissive policies, and partitioned enforcement, all live. |
| DB-Q6 RLS enabled & correct on tenant tables? | Partially Supported | Enabled on 126 of 126 brand tables, but the restrictive 0031 policy breaks customer principals (SA-DB-001), legacy raw-cast policies throw (SA-DB-012), and 8 identity tables plus the MVs are unprotected. |
| DB-Q7 Can the effective app role bypass RLS? | Partially Supported (bypass possible) | Not natively (NOBYPASSRLS, not owner). It can bypass through the self-settable `app.bypass_rls`, superuser-owned SECURITY DEFINER functions (purge/export any brand), RLS-free identity tables and MVs, and cross-tenant FKs. |
| DB-Q8 Tenant context safe with pooled connections? | Partially Supported | EF interceptor plus Npgsql reset is safe. Raw ABAC connections lack context, and a transaction-mode pooler would break the model (documented only). |
| DB-Q9 Constraints sufficient for tenant ownership & invariants? | Not Supported | No composite tenant FKs, global business-number uniques, no exclusion constraints, no order-total CHECKs, and soft-delete uniques are not partial. |
| DB-Q10 Isolation at app AND DB layer? | Partially Supported | Brand isolation is at both layers for 126 tables. Role assignment, MVs, lifecycle functions and customer-vs-customer data on most tables are app-only. The sub-brand DB layer exists but is miswired for customers. |
