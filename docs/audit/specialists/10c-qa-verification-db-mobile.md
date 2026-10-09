# 10c — QA, Testing and Independent Verification: Database + Mobile/Delivery/Maps slice

Agent key: `qa-c` · AREA code: `QC` · Date: 2026-10-09 · Branch: `claude/brave-dijkstra-6hlddw`

Reports under challenge: `08b-database.md` (SA-DB-001…022) and `12-mobile-delivery-maps.md` (SA-MOB-001…021).

## Scope and method

**What I checked**
- **Every Critical and High finding in both reports.** That is 9 DB findings (001–008, 012) and 4 MOB findings (001–004).
- **Mediums, above the one-third floor:**
  - DB: 4 of 8 (009, 013, 014, 015).
  - MOB: 8 of 12 (005, 008, 010, 011, 012, 013, 016, 017).

**How I checked each one**
- I re-traced the path myself: endpoint mapping and authorization metadata, then the handler, then EF/SQL, then the DB policy, trigger or constraint.
- I searched for mitigating controls the specialist might have missed:
  - later migrations or patches;
  - DB triggers and constraints;
  - EF global query filters;
  - caller checks;
  - client guards.

**Live database reproduction**

I built a throwaway cluster on my own, separate from the DB agent's.

| Item | Value |
|---|---|
| Engine | PostgreSQL 16.15 at `/usr/lib/postgresql/16/bin` |
| Data directory | `/var/tmp/lg-qa-c` |
| Port | 55435 |
| Run as | `postgres` OS user |
| Extensions | `pg_partman` and `postgis` were already installed at OS level (confirmed via `/usr/share/postgresql/16/extension/{pg_partman,postgis}.control`). Nothing was stubbed. |
| Teardown | Stopped and deleted at the end (`pg_ctl stop` + `rm -rf /var/tmp/lg-qa-c`) |

- Every security and concurrency repro ran **as `app_user`** (`rolbypassrls=f`).
- The session GUCs were set with the **exact 12-variable `SELECT set_config(…, false)` statement** from `RlsConnectionInterceptor.cs:93-117`.
- The values came from `HttpContextCurrentTenant.cs:24-63`, or from `CommerceHostCurrentTenant.cs:42-94` where noted.
- Null values were written the way the interceptor writes them: `''`, or `'?'` for scope_nodes and roles.

**What I could NOT run**
- No .NET SDK and no Docker. So:
  - no HTTP request was executed;
  - no xUnit or Testcontainers test was run;
  - every application-level statement below comes from reading code.
- No mobile device or simulator, and I did not run `npm ci` or jest myself.
- I made no repo changes. `git status` was clean at the end. Scratch files are in `scratchpad/qa-c/`.

## Commands run and results

| # | Command (abridged) | Result |
|---|---|---|
| C1 | `initdb -A trust`, then `pg_ctl -o '-p 55435'` | PG 16.15 up |
| C2 | `DB_NAME=lg_qa DB_PORT=55435 db/build_from_scratch.sh` | exit 0: 61 updated_at triggers, "RLS policies (app_user): 92" |
| C3 | `db/tools/migrate.sh up` | **exit 3**: `0005_split_features_from_modules.up.sql:102: ERROR: relation "identity_access.modules" does not exist`. **SA-DB-002 reproduced.** |
| C4 | `apply_multipass.sh`: `db/patches/*.sql` in git first-add order (tie → name), single-transaction, `wipe_demo_*` excluded | pass 1: 128/140; pass 2: 132; pass 3: no progress. Remaining: five demo seeds (FK to a missing demo brand), `phase1_slice_e…:92` and `phase4_role_vertical_key.sql:113` (asserts on rows that only the .NET IdentitySeeder creates), and `rbac_catalog_logistics_partner.sql` (depends on phase4). |
| C5 | Applied scratch copies of phase1_slice_e and phase4 with **only** their `DO $verify$` blocks removed (lines 59-92 and 85-113), then `rbac_catalog_logistics_partner.sql` | OK. These are the same workarounds the DB agent used; seeds were not needed because I wrote my own fixture. |
| C6 | `pg_proc` for `kernel.rls_bypass()` before re-hardening | `SELECT COALESCE(current_setting('app.bypass_rls', true), 'off') = 'on'`. The git-order run lets `rls_proposal.sql` (same commit as `harden_…`, sorts later) overwrite the hardened function. The interceptor sends `'true'` (`RlsConnectionInterceptor.cs:65`). **SA-DB-002 second half reproduced.** |
| C7 | `migrate.sh up` again | 0005–0033 applied (29) |
| C8 | Privileges check **before** re-applying harden: `has_function_privilege` + `pg_default_acl` | `purge_brand`, `export_brand`, `set_brand_suspension` and `set_brand_cancellation_state` all have proacl `{postgres=X/postgres,app_user=X/postgres}`; `pg_default_acl` kernel/f = `{app_user=X/postgres}`. The grant to app_user therefore comes from **default privileges alone**, not from re-running `harden`'s `GRANT … ON ALL FUNCTIONS`. |
| C9 | Re-applied `harden_app_user_and_rls_bypass.sql` (as the DB agent did) and loaded the two-tenant fixture `fixture.sql` | Fixture: brands A `a1…`, B `b2…`; customers c1, c2 (A) and cB; addresses; orders; captured payments; wallets; a B role; an A staff user |
| T1 | `t_db001.sql` (customer c1 GUCs as the core/operations host) | `current_scope_nodes()` = NULL. Own orders **0**, own payments **0**, stores **0**. `customer_addresses` **2**: c2's address is visible, because addresses are brand-only RLS. INSERT order → `new row violates row-level security policy "rls_subbrand_scope" for table "orders"`; the same for `pickup_requests`. |
| T1b | Same customer with commerce-host GUCs (`customer_id=''`, `user_id=c1`) | `wallet_accounts` visible = **2** (c1 **and c2**), so customer isolation on commerce degrades to brand only. Brand-A **staff** via commerce host (`scope '?'`): payments **0**. The same staff via core/ops host (`brand:A`): payments **2**. |
| T2 | `t_db003.sql` (brand-A staff session, app_user) | `purge_exec=t, export_exec=t`. B orders visible via RLS = 0. `kernel.export_brand(B)` returned 8 B row groups (payments, addresses, customers, roles, orders, brands, franchises, stores). Inside `BEGIN…ROLLBACK`, `kernel.purge_brand(B)` deleted every B row. After rollback, the superuser still sees B's order. |
| T3 | `t_db015.sql` (same session, `set_config('app.bypass_rls','true')`) | `DELETE` of B's payment → `DELETE 1`; B's brand row visible (rolled back). **The self-settable bypass gives the same cross-tenant power as purge/export.** |
| T4 | `t_db005.sql` | B role invisible to A (roles RLS = 0 rows), yet `INSERT INTO user_scope_memberships (A user, brand B, B role)` → inserted (rolled back). `otp_codes` and `refresh_tokens` are SELECT-able by app_user, and `relrowsecurity=f` on memberships, profiles, otp, refresh and role_permissions. |
| T5 | `t_db006_s1/s2.sql`: two concurrent app_user sessions each `INSERT payment_refunds 60.00` on a 100.00 payment (`pg_sleep(3)` before COMMIT) | Both `INSERT 0 1`, both COMMIT; `captured 100.00 / refunded 120.00 / 2 rows`. A sequential third refund was rejected by `check_refund_cap`. **SA-DB-006 reproduced.** The first attempt with status `processed` was rejected by `payment_refunds_status_check`, so I used `succeeded`, the value the handler writes. |
| T6 | `t_db008_s1/s2.sql`: EF-shaped read-then-`UPDATE … SET balance=<read>+delta` race (+50 / −30 on 100) | Both `UPDATE 1`; final **70.00** (correct: 120). **SA-DB-008 reproduced.** |
| T7 | `t_misc.sql` | (a) brand-A order pointing at B franchise, store and customer → `INSERT 0 1` (SA-DB-004). (b) Customer-c1 GUCs with the scope simulated as fixed (`brand:A`): pickup at **c2's address** and at **brand-B's address** → both `INSERT 0 1` (SA-MOB-005 at the DB layer). (c) brand `''` + bypass `'true'` on `customer_identities` → `ERROR: invalid input syntax for type uuid: ""`, while `stores` under the same GUCs returned 2 (SA-DB-012). (d) `refund_type` `'wallet'` and `'gateway'` → `violates check constraint "payment_refunds_refund_type_check"` (SA-QC-001). |
| T8 | `t_db007.sql` (warehouse-W2-scoped staff; superuser pre-inserted `WB-20261009-0001` for W1) | The brand-wide count visible to W2 = **0**. Inserting the generator's next number `WB-20261009-0001` → `duplicate key value violates unique constraint "warehouse_batches_batch_number_key"` (SA-QC-002). |
| T9 | `t_db013.sql` (after `REFRESH MATERIALIZED VIEW analytics.mv_customer_ltv`) | Brand-A session: A rows 2, **B rows 1** (SA-DB-013) |
| T10 | `COPY authz.decision_log (occurred_at, decision) FROM STDIN` as app_user | `ERROR: COPY FROM not supported with row-level security` (SA-DB-014). `authz.policy` as app_user with no GUCs: 58 platform, 0 brand. |
| T11 | Catalog queries on `delivery_assignments` | Only unique index is the PK. CHECK on status/leg_type only. Only trigger is `set_updated_at`. **No DB transition or uniqueness guard** (SA-MOB-001/002). |
| T12 | `t_mob012.out`: created `rider_location_pings_p20260901` (38 days old), then `CALL partman.run_maintenance_proc()` | **ERROR: `Given parent table not found in system catalogs: order_lifecycle.process_logs`**. The call aborts and the old partition survives. Then `SELECT partman.run_maintenance('logistics.rider_location_pings')` → the old partition is **dropped**. `part_config` row for pings: `1 day / 14 days / keep_table=f`. (SA-MOB-012 corrected; SA-QC-003.) |
| C10 | `pg_ctl stop`; `rm -rf /var/tmp/lg-qa-c`; `git status --short` | Cluster gone; repo clean |

Static greps used for verification (all read-only):
- writers of `DeliveryOtp`, `PickupOtp`, `GeoLocation`, `CreatePoint`, `ST_MakePoint` and `HasQueryFilter`;
- readers of `LastKnownLocation` and `RiderLocationPings`;
- `bypass_rls` setters;
- `ICurrentTenant` implementations;
- the `rider-mobile/package-lock.json` versions of react and react-dom.

## Verification table

Legend: C = Critical, H = High, M = Medium, L = Low. V = Verified, PV = Partially Verified, S = Suspected.

### SA-DB-*

| ID | Specialist sev/status | QA verdict | Corrected sev/status | Evidence QA read/ran | Notes |
|---|---|---|---|---|---|
| SA-DB-001 | C / V | **Confirmed – scope corrected** | C / V | T1, T1b. `JwtTokenService.cs:98-119` (customer token has no `scope_nodes`). `HttpContextCurrentTenant.cs:53`. `RlsConnectionInterceptor.cs:85-90`. `0031…up.sql:60-110,198`. No later migration touches it (0032/0033 grep: only membership/notification changes). No customer bypass: grep of every `bypass_rls` setter shows only pre-auth, webhook and platform-admin paths. | Broader than reported. `CommerceHostCurrentTenant.cs:42-94` does not implement `ScopeNodes`, so **every non-platform staff request on the commerce host** also gets `'?'`: brand-A staff saw 0 payments (T1b). Finance (expenses, cash books, royalty) and payments are dead for brand staff, and refunds fail because the SECURITY INVOKER `check_refund_cap` cannot see the payment. Also blocks customer slot listing (`delivery_slots` is restrictive), which makes SA-MOB-020 moot today. Canonical: SA-TEN-001. |
| SA-DB-002 | H / V | **Confirmed** | H / V | C3, C4, C6; `build_from_scratch.sh:89-90`; `rls_proposal.sql` vs `harden_…sql:31-38` | Same workarounds needed (verify-block removal and seed skip). The patch set needs 3 passes, so it has no deterministic order. |
| SA-DB-003 | H / V | **Confirmed – severity corrected (to Medium)** | M / V | C8, T2, T3; `0015…up.sql:143-165, 175-250` (comment "NOT granted to app_user" at :249); `harden…sql:59-67` default privileges; `AdminCancellation.cs:62-90` (export uses `tenant.BrandId` from the caller's own JWT, never a parameter); only the purge caller is `RetentionSweepService.cs:241` (worker) | The mechanics are exactly as reported, and the grant does contradict the migration's stated intent. Severity is lower for two reasons. (1) No HTTP path passes a foreign brand id. (2) Any party able to run SQL as app_user can already self-set `app.bypass_rls` and DELETE or SELECT every tenant (T3), so the DEFINER functions add no new capability. Keep the fix: REVOKE plus an internal brand check. Removing the grant breaks `RetentionSweepService` unless the worker gets its own role. Group with SA-TEN-007 / SA-DB-015. |
| SA-DB-004 | H / V | **Confirmed – severity corrected (to Medium)** | M / V | T7a, T7b | DB behaviour reproduced. A reachable app instance exists: the pickup `address_id` (SA-MOB-005), where cross-brand was accepted at the DB once the 0031 scope is fixed. Cross-tenant **reads** of the linked row are still blocked by RLS on the parent. Defence-in-depth gap: Medium. Canonical with SA-TEN-011 (L); QA settles on Medium. |
| SA-DB-005 | H / V | **Confirmed** | H / V | T4; `0029…up.sql:147-155` (memberships RLS deliberately left off); `GrantMembership.cs:47-49,144-165` | The specific vector (granting a **brand-B role**) is blocked in the app, because `_db.Roles.FindAsync` runs under roles RLS and B's role is not found. The reachable app vector is a **foreign user id** (SA-AUTHZ-003). The PII and token-hash exposure needs SQL execution. Keep High as part of the SA-AUTHZ-003 group. |
| SA-DB-006 | H / V | **Confirmed** | H / V | T5; `payment_idempotency.sql:29-80` (no lock; SECURITY INVOKER); `AdminPaymentHandlers.cs:100-118` (SUM outside tx), `:148-231` (gateway call before INSERT) | Today the race is reachable only by platform admins (bypass): for brand staff on the commerce host the payment is invisible (SA-DB-001 scope above). It is High once that is fixed. Canonical: SA-API-009. |
| SA-DB-007 | H / V | **Confirmed – severity corrected (to Medium)** | M / V | Live uniques: `expenses_expense_number_key`, `pickup_requests_request_number_key`, `warehouse_batches_batch_number_key` are all `UNIQUE (number)`; `ExpenseCommands.cs:204-205`; `CreateWarehouseBatch.cs:48-49`; `PickupCommands.cs:61-63` | A cross-tenant collision needs an equal brand-wide count on the same day (PKP also needs equal 4-hex prefixes). The impact is a functional DoS of back-office creation plus a weak existence oracle, with no data exposure. QA found a more frequent **intra-brand** variant caused by 0031 → SA-QC-002. Canonical with SA-TEN-012. |
| SA-DB-008 | H / PV | **Confirmed** | H / V | T6; grep: no `IsConcurrencyToken`/xmin/`DbUpdateConcurrencyException` anywhere; `CustomerWalletHandlers.cs:180-181` | Canonical: SA-API-005 |
| SA-DB-009 | M / V | **Confirmed (additional defect)** | M / V | `OutboxEventRelayService.cs:74-118,120-200` | Beyond the unlocked claim: when the in-transaction re-check finds the row **already claimed** (`current is null` → return from the lambda), `ProcessEventAsync` **still publishes** (L120-137 run unconditionally). A second relay that loaded the same batch republishes even without a race. Canonical: SA-API-014. |
| SA-DB-010 | M / PV | Not re-verified | — | — | Accepted on specialist evidence; overlaps SA-SOLID-006 |
| SA-DB-011 | M / PV | **Duplicate of SA-API-004** (orders) + SA-SUB-016 (capture) | H (per SA-API-004) | — | Severity contradiction: QA sides with High for POS duplicate orders, since duplicates carry side effects on balances and coupons |
| SA-DB-012 | H / V | **Confirmed** | H / V | T7c; live policy `custident_tenant` `TO public` with `current_setting('app.current_brand_id', true)::uuid`; `core.WebApi/Program.cs:620-646` (`/api/v1/customer/auth/google` in the bypass list); `HttpContextCurrentTenant` has no brand override, so the brand is `''`; `CustomerGoogleSignInHandler.cs:88` | |
| SA-DB-013 | M / V | **Confirmed** | M / V | T9 | Canonical: SA-DB-013 (SA-TEN-013 L) |
| SA-DB-014 | M / V | **Confirmed** | M / V | T10; `NpgsqlAbacStore.cs:40-47` (claims "role has SELECT only on these two tables": false, since app_user has CRUD); `DecisionLogWriter.cs:128-140` (binary COPY) | |
| SA-DB-015 | M / V | **Confirmed** | M / V | T3 | Canonical: SA-TEN-007 |
| SA-DB-016 | L / PV | Not re-verified | — | — | |
| SA-DB-017 | M / V (synthetic) | Not re-run | — | Read `0031…up.sql:60-110` (plpgsql, per row) | Plausible; numbers are synthetic |
| SA-DB-018 | M / V | Not re-verified | — | — | |
| SA-DB-019 | L / V | Not re-verified | — | — | |
| SA-DB-020 | L / V | Not re-verified | — | — | Part of the SA-TEN-004 group |
| SA-DB-021 | L / V | Not re-verified | — | — | |
| SA-DB-022 | L / PV | **Duplicate of SA-MOB-002** | H (per MOB-002) | T11 | The offer-accept race is one sub-case. The deterministic manual double-assign drives severity. |

### SA-MOB-*

| ID | Specialist sev/status | QA verdict | Corrected sev/status | Evidence QA read/ran | Notes |
|---|---|---|---|---|---|
| SA-MOB-001 | H / V | **Confirmed** | H / V | `RiderSelfEndpoints.cs:52,82` (RiderOnly + `rider.tasks.update`); `UpdateMyTaskStatus.cs:34-42` (allow-list only), `:96` (unconditional assign), `:152-176` (`o.Status="delivered"`, `FromStatus="out_for_delivery"` hard-coded, gated only on `DeliveredAt==null`), `:263-264` (load decrement each call); T11 (no DB trigger or transition guard) | Additional: the sibling endpoint `PATCH /rider/assignments/{id}/status` → `UpdateMyAssignmentStatus.cs:44` writes **any** string to `rider_assignments.status`, with only the DB CHECK as a limit. Group with SA-API-006 / SA-SOLID-001. |
| SA-MOB-002 | H / V (manual) | **Confirmed** | H / V | `PickupCommands.cs:204-279`: no `pr.Status` check, no existing-leg check; `pr.Status="assigned"` is written unconditionally, so assigning a **cancelled or completed** pickup revives it; T11 (no partial unique) | Supersedes SA-DB-022 |
| SA-MOB-003 | H / V | **Confirmed** | H / V | grep: the only writer is `AssignPartnerDispatch.cs:67` (partner lane); `OtpPurpose.DeliveryOtp` is unused; `VerifyTaskOtp.cs:40-52`; DDL `04_bc4…sql:44-45` has no default and no trigger | The attempt-limit half is latent until OTPs exist |
| SA-MOB-004 | H / V | **Confirmed** | H / V | grep: `CreatePoint` only at `BatchLocationPing.cs:53,85`; no `ST_MakePoint`/`GeoLocation =` writer; `GetFareQuoteQuery.cs:56-58` throws; no lat/lng in customer-mobile, admin-web or pos-web | The partner/RaaS lane DTOs carry decimal `Lat`/`LastKnownLat` (`PartnerBookingDtos.cs:9`, `PartnerDispatchDtos.cs:15,33,75`). These are not PostGIS and do not feed customer addresses or legs. |
| SA-MOB-005 | M / PV | **Confirmed** | M / V (DB layer) | `PickupCommands.cs:103`; the customer handler `:338-470` has no `CustomerAddresses` lookup; T7b | In the production role config the customer INSERT is currently blocked by SA-DB-001 (T1). The IDOR becomes live the moment 0031 is fixed, so the fixes must ship together. A cross-brand address id is also accepted by the FK (T7b); the rider's read of it is then RLS-hidden. |
| SA-MOB-006 | M / V | Not re-verified | — | — | |
| SA-MOB-007 | M / V | Not re-verified | — | — | |
| SA-MOB-008 | M / V | **Confirmed** | M / V (code) | `rider-mobile/app/(app)/tasks/[id].tsx:392-402` (any error → enqueue `completed` + "No connection"); `useOfflineQueueFlush.ts:42-58` (replays via `updateTaskStatus`, `break` on the first error) | |
| SA-MOB-009 | M / S | Not verifiable here | M / S | — | Needs a device |
| SA-MOB-010 | M / V | **Confirmed** | M / V (code) | `BatchLocationPing.cs:42-60` (no duty/status gate, `PingedAt` from the client); no validator for `LocationPingInput` (grep); `RiderSelfEndpoints.cs:126-135` (empty-batch check only) | |
| SA-MOB-011 | M / PV | **Confirmed – evidence corrected** | M / V | `DeactivateRider.cs:39-40` sets `Status=Terminated` **and `DeletedAt`**; `RiderConfiguration.cs:101` `HasQueryFilter(DeletedAt == null)`; rider-self handlers resolve through `_db.Riders` without `IgnoreQueryFilters` | **The deactivate path is a false positive.** A deactivated rider no longer resolves, so ping returns 0 and task and status calls return 404. The gap is real for **`UpdateRider` with `Status=suspended|terminated`** (`UpdateRider.cs:53,87-89`): no DeletedAt and no status filter in self-resolve, so a suspended rider keeps operating. Open legs and `IsOnDuty` are left untouched on both paths. |
| SA-MOB-012 | M / PV | **Confirmed – mechanism corrected** | M / V | T12; `99_cross_cutting…sql:75-91`; `db/tools/run_partman_maintenance.sh` (`CALL partman.run_maintenance_proc()`) | With pg_partman installed, which the documented build requires, 14-day drop retention **is** configured. partman **does** drop function-created `_pYYYYMMDD` partitions, which answers MOB's open question. Retention fails because (a) nothing schedules maintenance in deploy (SA-OPS-004) and (b) `run_maintenance_proc` aborts on a stale `order_lifecycle.process_logs` entry (SA-QC-003). DEFAULT-partition rows are never purged, as MOB said. |
| SA-MOB-013 | M / V | **Confirmed** | M / V | `useServiceability` is defined at `useCatalog.ts:172` with no caller (grep); no serviceability check in `PickupCommands.cs` | The parcel path uses `ServiceableStoreId` (`CreateParcelOrderCommand.cs:74`) |
| SA-MOB-014 | M / V | Not re-verified | — | — | |
| SA-MOB-015 | M / V | Not re-verified | — | — | Group with SA-AUTHZ-012 / SA-VERT-005 / SA-ONB-003 / SA-FE-006 (backend vertical gate) and SA-ONB-008 / SA-FE-008 (build-time brand) |
| SA-MOB-016 | M / PV | **Confirmed** | M / V | `CancelOrderByCustomerCommand.cs:40-113` (no DeliveryAssignments/RiderLoad); `StateMachineStrategyBase.cs:62-63` | |
| SA-MOB-017 | M / V | **Confirmed (partially re-checked)** | M / PV | `rider-mobile/package-lock.json`: react **19.2.3**, react-dom **19.2.8** with peer `react ^19.2.8`; `ci.yml` mobile matrix runs `npm ci` | I did not run `npm ci` or `tsc`. Duplicate of SA-FE-011 (CI) and SA-OPS-016 (release) |
| SA-MOB-018 | L / V | **Duplicate of SA-API-018** | — | — | |
| SA-MOB-019 | L / V | Not re-verified | — | — | |
| SA-MOB-020 | L / PV | Not re-verified (moot today) | — | T1 | Under 0031 a customer sees 0 slots anyway |
| SA-MOB-021 | L / V | Not re-verified | — | — | |

### Location-data authorization (requested checks)

| Question | Answer | Evidence |
|---|---|---|
| Can a customer read rider location for an order that is not theirs, or after delivery? | **No, because no customer endpoint exposes rider location at all.** | Every reader of `LastKnownLocation` / `RiderLocationPings` (grep) is `BatchLocationPing`, `GetRidersLive`, `GetRiderTrack`, `GetRiderStats` (admin, `permission:rider.read`, `RidersAdmin.cs:60-61`) or the worker (AutoDispatch, retention, partitions). The customer group is `CustomerOnly` (`CustomerOrderEndpoints.cs:32`). |
| Can rider X read or act on rider Y's tasks? | **No at the app layer.** At the DB layer, isolation is **app-only**. | Self-resolve `Riders(UserId, BrandId)` → `DeliveryAssignments(Id, RiderId==self, BrandId)` (`UpdateMyTaskStatus.cs:44-53`); no rider id in routes. In the DB, `rider_location_pings` has only `rls_brand`, and `delivery_assignments`/`riders` have `rls_brand` plus a restrictive subbrand scope (live `pg_policies`). Nothing at the DB layer separates riders of the same franchise. |
| Can staff read the tracks of riders outside their store? | Yes, within their franchise | `GetRiderTrack.cs:27-33` checks brand plus `_user.FranchiseId`. Store tokens carry franchise (`ScopeResolver.cs:74-84`). `riders` has no `store_id` column (it uses `primary_store_id`), so the DB restrictive policy cannot narrow by store either. |

## Dedupe groups (proposed canonical IDs)

| Group | Canonical | Members | Severity |
|---|---|---|---|
| D1 0031 restrictive policy denies non-scope principals | **SA-TEN-001** | SA-DB-001 (C), SA-AUTHZ-006 (H) | Critical |
| D2 Commerce host omits CustomerId/subject GUCs | **SA-TEN-002** | SA-AUTHZ-005 (reproduced T1b: customer sees 2 wallets) | High |
| D3 Schema not reproducible | **SA-DB-002** | SA-ARCH-008, SA-SUB-020, SA-VERT-010, SA-OPS-007 | High |
| D4 DB trusts app: bypass GUC + DEFINER functions | **SA-TEN-007** | SA-DB-015, SA-DB-003 | Medium |
| D5 Membership/identity tables without RLS; foreign-user grant | **SA-AUTHZ-003** | SA-TEN-004, SA-DB-005, SA-DB-020 (identity uniqueness aspect) | High (per AUTHZ) |
| D6 Composite tenant FKs | **SA-DB-004** | SA-TEN-011; the instance is SA-MOB-005 | Medium |
| D7 Global business-number uniques | **SA-DB-007** | SA-TEN-012; related SA-QC-002 | Medium |
| D8 Refund cap race / gateway inside tx | **SA-API-009** | SA-DB-006; related SA-QC-001 | High |
| D9 No concurrency tokens / lost updates | **SA-API-005** | SA-DB-008, SA-DB-010 (coupon counters) | High |
| D10 Order idempotency / capture check-then-act | **SA-API-004** | SA-DB-011, SA-SUB-016 (capture) | High |
| D11 Worker claims / multi-replica | **SA-API-014** | SA-DB-009, SA-OPS-005, SA-ARCH-007, SA-OPS-015 | Medium (High before scale-out) |
| D12 Leg/order state machine bypass | **SA-MOB-001** (leg) with **SA-API-006** (order) | SA-SOLID-001; SA-MOB-016 is the propagation facet | High |
| D13 Double assignment | **SA-MOB-002** | SA-DB-022, SA-SOLID-011 (drift) | High |
| D14 Mobile CI/release | **SA-FE-011** (CI) / **SA-OPS-016** (release) | SA-MOB-017 | Medium |
| D15 Customer booking idempotency key | **SA-API-018** | SA-MOB-018 | Low |
| D16 Vertical gating not server-side | **SA-AUTHZ-012** | SA-MOB-015 (backend half), SA-VERT-005, SA-ONB-003, SA-FE-006 | per AUTHZ |
| D17 Build-time brand in mobile | **SA-ONB-008** | SA-MOB-015 (client half), SA-FE-008, SA-ONB-012 | Medium |
| D18 Partition maintenance | **SA-OPS-004** | SA-MOB-012, SA-QC-003 | High (ops) |
| D19 Analytics MVs | **SA-DB-013** | SA-TEN-013 | Medium |

## Contradictions

1. **The DB idempotency matrix calls pickup creation "truly idempotent"; MOB and API say duplicates are possible.** Both are right, about different things:
   - DB-enforced idempotency holds only when a key is sent, and the customer app sends none (SA-MOB-018 / SA-API-018).
   - In the production role config, customers cannot insert pickups at all (T1).
   - *Assignment* is not idempotent (SA-MOB-002), which the matrix rates only as the offer-accept sub-case at Low (SA-DB-022). QA reconciles this at High.
2. **"Customer-vs-customer is DB-enforced on the 8 `rls_brand_or_customer` tables" (08b cross-cutting summary)** is false for the commerce host, which serves those tables. It never sets `app.current_customer_id` (T1b: 2 wallets visible). 08b's SA-DB-001 also scopes the 0031 outage to customers, but brand staff on the commerce host are equally blocked (T1b).
3. **SA-MOB-011 is contradicted by code.** `DeactivateRider` soft-deletes, and the global filter blocks rider self-service. The real gap is via `UpdateRider` suspend/terminate.
4. **SA-MOB-012 vs runtime.** With partman installed, retention is configured and functional per table. The break is the scheduler (SA-OPS-004) and a stale `part_config` row (SA-QC-003). 11-devops treats `process_logs` as a live partman table; in the rebuilt DB its config row points at a schema it no longer lives in.
5. **SA-DB-003 recommendation vs runtime.** "REVOKE from app_user" contradicts `RetentionSweepService.cs:241`, which needs it, as the DB agent itself noted. The fix must be sequenced with a dedicated maintenance role.
6. **Severity spreads:**
   - 0031 outage: C/C/H (D1). QA: Critical.
   - Order idempotency: SA-API-004 H vs SA-DB-011 M. QA: High.
   - Composite FKs: SA-DB-004 H vs SA-TEN-011 L. QA: Medium.
   - Global uniques: SA-DB-007 H vs SA-TEN-012 L. QA: Medium.
   - DEFINER functions: SA-DB-003 H vs SA-TEN-007 M. QA: Medium.
7. **DB-Q8.** 02 says Fully (EF); 08b and 11 say Partially. QA: Partially. The EF path is safe, but raw `NpgsqlDataSource` paths (ABAC store, `BrandExportService`) run without the interceptor, and GUCs are session-level.
8. **The integration test suite encodes the defect as intended behaviour.** `SubBrandScopeRlsTests.cs:264-273` asserts that an unresolved `scope_nodes` "denies everything". No test models a customer, API-key or commerce-host principal, which is exactly the population that always produces `'?'`.

## New findings

### SA-QC-001 — Admin refund API contract ("gateway"/"wallet") violates the `refund_type` CHECK; the Razorpay refund is issued before the failing INSERT
- Category: Financial integrity / API–DB contract
- Severity: High
- Status: Partially Verified (DB constraint reproduced T7d; handler traced; HTTP not executed)
- Related area: API, DB
- Evidence:
  - `commerce.Application/Commerce/Common/Dtos/CommerceDtos.cs:438-449`: `IssueRefundRequest.RefundType // "gateway" or "wallet"`.
  - `AdminPaymentHandlers.cs:132` copies `req.RefundType` into the row. `:150` branches on `"wallet"`. `:217-221` calls `_gateway.InitiateRefundAsync` (the real `RazorpayPaymentGateway.cs:117` via `SettingsFirstPaymentGateway`) **before** `_db.PaymentRefunds.Add` + `SaveChangesAsync` (`:227-230`).
  - `database_scripts/06_bc6_commerce.sql:418-419`: `refund_type CHECK IN ('full','partial','goodwill','dispute_loss')`, unchanged by any patch or migration (grep). It matches `SharedDataModel/Enums/RefundType.cs`.
  - No validator constrains `RefundType` (grep), and validators do not run anyway (SA-API-003).
  - `POST /api/v1/admin/payments/refunds` (`PaymentsAdmin.cs:28`, `permission:payment.refund`). No admin-web or pos-web caller (grep).
- Observed behaviour:
  - Any caller that follows the documented contract gets a 23514 CHECK violation at SaveChanges:
    - `"gateway"` → Razorpay has **already** refunded the money; the DB transaction rolls back, leaving no refund row and no audit row, and a retry refunds again;
    - `"wallet"` → the wallet credit rolls back, so the wallet-refund feature can never persist.
  - A caller that sends a DB-valid value such as `"full"` always takes the gateway branch, so wallet refunds are unreachable.
- Reproduction: T7d. HTTP test: issue a gateway refund with `refundType="gateway"` and expect 201. Today: 500, with a Razorpay refund recorded at the gateway.
- Impact: money leaves with no ledger record, and the cap check (app SUM and trigger) cannot see it, so repeated attempts over-refund without limit. The wallet-refund feature is dead.
- Recommended remediation (smallest):
  - Split the API field into `RefundMethod` (`original|wallet`), mapped to `refund_method`, and `RefundType` (`full|partial|…`), validated against `RefundType` constants.
  - Insert the refund row as `processing` and commit **before** calling the gateway; update it afterwards. This also fixes SA-API-009.
- Regression tests required: handler unit test mapping each documented value; integration test that a refund with each method persists; a fake gateway asserting zero calls when the row insert fails.
- Dependencies / priority: P1 (blocked behind SA-TEN-001 for brand staff today; reachable by platform admins).

### SA-QC-002 — Sub-brand RLS (0031) makes the per-brand `COUNT(*)+1` number generators scope-blind: routine intra-tenant unique violations
- Category: Integrity / RLS side-effect
- Severity: Medium
- Status: Verified (DB, T8) for warehouse batches; Partially Verified (code) for expenses
- Related area: DB, TEN
- Evidence:
  - `CreateWarehouseBatch.cs:48-49` (`COUNT(b.BrandId==brandId)+1`, `WB-{date}-{n}`) and `ExpenseCommands.cs:204-205` (`EXP-{date}-{n}`) assume the count sees the whole brand.
  - Under the 0031 RESTRICTIVE policy (`0031…up.sql:144-200`; both tables are in the list and carry `warehouse_id` / `franchise_id`), a warehouse- or franchise-scoped user sees only its own rows.
  - The uniques are global (`UNIQUE (batch_number)`, `UNIQUE (expense_number)`).
- Observed behaviour: the brand had 1 batch (W1). W2-scoped staff counted **0** and generated `WB-20261009-0001`, which raised `duplicate key … warehouse_batches_batch_number_key`. Because the count does not change when the insert fails, W2 stays blocked for the rest of the day.
- Impact: in any multi-warehouse or multi-franchise brand, scoped staff hit 500s on batch and expense creation whenever their visible count equals a sibling's on the same day. That is far more frequent than the cross-tenant case in SA-DB-007. Expenses become affected once SA-TEN-001 is fixed for the commerce host.
- Recommended remediation: replace COUNT+1 with a per-brand counter row using the `next_order_number` upsert pattern, executed in a SECURITY DEFINER function or under a brand-only predicate. Make the uniques `(brand_id, number)` (SA-DB-007).
- Regression tests required: two warehouse-scoped users in one brand create batches on the same day; two franchise-scoped users create expenses.
- Dependencies / priority: P2, with SA-DB-007.

### SA-QC-003 — Stale pg_partman config for `order_lifecycle.process_logs` makes `partman.run_maintenance_proc()` abort, so no partman table gets premake or retention
- Category: Database operations / data retention
- Severity: Medium
- Status: Verified on the rebuilt schema (T12). The production `part_config` state is unknown.
- Related area: OPS, MOB (location retention)
- Evidence:
  - `99_cross_cutting_schema_qualified.sql` registers `order_lifecycle.process_logs` with partman.
  - `db/patches/phase1_slice_c_laundry_fulfillment.sql:24-28,56-90` moves `process_logs` and its partitions to `laundry_fulfillment` but never updates `partman.part_config` (grep: the only part_config writer outside the base scripts is `0024…down.sql:8`).
  - The only scheduled entry point, `db/tools/run_partman_maintenance.sh` (and the plist), runs `CALL partman.run_maintenance_proc()`.
- Observed behaviour:
  - `CALL partman.run_maintenance_proc()` → `ERROR: Given parent table not found in system catalogs: order_lifecycle.process_logs`; a 38-day-old ping partition survived.
  - `SELECT partman.run_maintenance('logistics.rider_location_pings')` dropped it.
  - `part_config` lists `order_lifecycle.process_logs` with 0 children.
- Impact:
  - Even after a scheduler is added (SA-OPS-004), every run fails. So:
    - no new monthly partitions for orders, audit_logs, notifications_log and decision_log (rows fall into the DEFAULT partitions);
    - **no 14-day deletion of rider GPS history** (DPDP, SA-MOB-012).
  - The failure is silent unless the cron output is monitored.
- Recommended remediation: a migration that does `UPDATE partman.part_config SET parent_table='laundry_fulfillment.process_logs' WHERE parent_table='order_lifecycle.process_logs'` (or `undo`/re-`create_parent`). Add a CI/startup assertion that every `part_config.parent_table` resolves, and call maintenance per table so one bad row cannot block the rest.
- Regression tests required: an integration test that runs `run_maintenance_proc()` on the migrated schema, and a retention test that drops a partition older than 14 days.
- Dependencies / priority: P1 (with SA-OPS-004).

## Missing tests (concrete)

| Area | Missing test | Why it matters | Existing coverage |
|---|---|---|---|
| RLS × principals | Customer-token GUCs (`scope '?'`, `customer_id` set) SELECT/INSERT on orders, pickup_requests, payments, delivery_slots and audit_logs | Would have caught SA-TEN-001 | `SubBrandScopeRlsTests.cs:264-273` asserts the opposite intent |
| RLS × tenant adapters | `CommerceHostCurrentTenant` and `HttpContextCurrentTenant` publish identical GUCs for the same staff and customer principal | SA-TEN-001/002 | No test references `CommerceHostCurrentTenant` |
| RLS × anonymous paths | brand `''` + bypass `true` against every table touched by auth paths (customer_identities) | SA-DB-012 | none |
| DB privileges | `has_function_privilege('app_user', f, 'EXECUTE')` = false for purge/export; each brand-taking DEFINER raises on a foreign brand | SA-DB-003 | none |
| Identity RLS | An A session cannot insert a membership for a B user/role/scope; app_user cannot read otp_codes/refresh_tokens of another brand | SA-DB-005 / SA-AUTHZ-003 | `UsersBrandRlsTests` covers users only |
| Concurrency: money | Two-connection refund race (the cap); parallel wallet top-up + debit; parallel coupon redemption | SA-DB-006/008/010 | none (no commerce test project, SA-ARCH-010) |
| Refund contract | Each documented `refundType` persists; gateway not called if the row cannot be written | SA-QC-001 | none |
| Idempotency | Parallel POS order create with the same key; parallel pickup create with the same key (the reference pattern); customer app sends `Idempotency-Key` (jest) | SA-API-004, SA-MOB-018 | none |
| Dispatch | assign-after-assign → 409; auto-dispatch vs manual race; accept vs expire race; assign of a cancelled pickup → 409 | SA-MOB-002 | none |
| Leg state machine | cancelled→completed rejected; double completed decrements load once; completing a delivery of a cancelled order → 409; `/assignments/{id}/status` allow-list | SA-MOB-001 | none |
| Location authz | Rider X cannot read or PATCH rider Y's leg (404); suspended rider ping/status → 403; customer endpoints never return rider coordinates; store-scoped admin cannot read another store's rider track | M9, SA-MOB-011 | `GetRidersLiveTests` only |
| Location ingestion | Ping validator bounds; off-duty ping ignored; future timestamp clamped | SA-MOB-010 | none |
| Retention | `run_maintenance_proc()` succeeds on the migrated schema; a ping partition older than 14 days is dropped | SA-QC-003, SA-MOB-012 | none |
| Number generators | Two brands' first expense on the same day; two warehouse-scoped users' batches | SA-DB-007, SA-QC-002 | none |
| Schema build | CI job: build_from_scratch + patches + `migrate.sh up` + `verify`; assert `kernel.rls_bypass()` honours `'true'` | SA-DB-002 | none (CI uses hand-trimmed fixtures) |

## Verdict challenges

### 08b DB-Q1…Q10

| Q | 08b | QA | Why |
|---|---|---|---|
| DB-Q1 | Partially | **Agree** | Live catalog confirms PK/FK correctness; FK-index/redundancy numbers not re-counted |
| DB-Q2 | Partially | **Agree** | Plans synthetic only; not re-run |
| DB-Q3 | Not Supported | **Agree** | Refund over-cap (T5) and wallet lost update (T6) reproduced independently; double-assignment has no DB guard (T11) |
| DB-Q4 | Partially | **Agree, with caveat** | The pickup TI pattern is real but unused by the customer app and currently unreachable for customers (0031). Refunds are worse than "SD" because of SA-QC-001. |
| DB-Q5 | Fully | **Agree** | |
| DB-Q6 | Partially | **Agree, but understated** | 0031 breaks the customer, API-key **and commerce-host staff** lanes (T1b), so in the production role config RLS is "correct" only for core/ops staff |
| DB-Q7 | Partially (bypass possible) | **Agree** | Self-set bypass (T3), DEFINER (T2), RLS-off identity tables (T4), MVs (T9) |
| DB-Q8 | Partially | **Agree** | EF path safe; raw NpgsqlDataSource paths and session-level GUCs |
| DB-Q9 | Not Supported | **Agree** | T7a; global uniques plus scope-blind generators (T8) |
| DB-Q10 | Partially | **Agree, one statement corrected** | Customer-vs-customer is not DB-enforced on the commerce host (T1b) |

### 12 M1…M12

| Q | 12 | QA | Why |
|---|---|---|---|
| M1 | Partially | **Disagree in degree → Partially, blocked** | Against the documented app_user + 0031 configuration, the customer app cannot list orders or slots, place orders or schedule pickups (T1). It is functional only after SA-TEN-001 is fixed. |
| M2 | Partially | Agree | |
| M3 | Partially | Agree | |
| M4 | Not Supported | Agree | |
| M5 | Not Supported | Agree | |
| M6 | Partially | Agree | Geocoding absence independently confirmed |
| M7 | Not Supported | Agree | |
| M8 | Not Supported | Agree | MOB-001/002/016 confirmed |
| M9 | Partially | **Agree, rationale corrected** | Customers cannot read rider location (confirmed). Rider-vs-rider isolation is app-only at the DB. The address IDOR is confirmed. "Terminated riders keep access" is wrong for deactivate and right for suspend. |
| M10 | Partially | **Agree, rationale corrected** | Retention is configured; it fails through the missing scheduler and the stale partman row (SA-QC-003) |
| M11 | Not Supported | Agree | |
| M12 | list R1–R14 | **Agree, two additions** | Add SA-TEN-001 as step 0, a prerequisite for any customer journey. Add SA-QC-003 alongside R8. Ship R3 (address ownership) in the same release as the 0031 fix. |

## Not verified

- **HTTP and runtime.** Every endpoint, middleware and handler behaviour is static. No .NET SDK and no Docker, so no xUnit, Testcontainers or HTTP smoke tests.
- **Production database state.** Unknown: patch order, `kernel.rls_bypass()` body, `part_config` contents, and app_user function grants. All DB repros are on a schema rebuilt from the repo with the noted workarounds (verify-block removal in two patches, demo seeds skipped, harden re-applied).
- **Mobile.** Device behaviour (SA-MOB-009, background tasks, push). `npm ci`, `tsc` and jest were not run by me.
- **Specialist evidence accepted without re-verification:**
  - SA-DB-010, 016, 017, 018, 019, 020, 021;
  - SA-MOB-006, 007, 014, 015, 019, 020, 021.
- **External systems.** Razorpay refund and idempotency behaviour (SA-QC-001 assumes `InitiateRefundAsync` performs a real refund, per `RazorpayPaymentGateway.cs:117`).
- **Performance.** Figures (SA-DB-017, E1–E7) were not reproduced.
