# Fix tasks — remediation of `docs/AUDIT_REPORT.md`

One row per issue in the audit report. Ordered by dependency, then severity: security and data-layer
work lands before UI. Status is one of `Todo` / `In Progress` / `Blocked` / `Done`.

**A task is `Done` only when:** the real DB → backend → API → frontend path is wired; no hardcoded or
mock data in the touched code; live-tested in Chrome with zero console errors; live-tested on mobile
where the area has a mobile surface; loading/empty/error states handled; full build and test suite
green; evidence recorded below.

---

## Order of execution

`T1 → T2 → T5 → T3 → T4 → T6 → T7 → T8`

T1 is a live privilege-escalation path and goes first. T2 and T5 are data-layer (seed catalogue, RLS)
and precede the UI work that reads through them. T3/T4 add the missing read endpoints and the panels
that consume them. T6–T8 are model-consistency and copy defects.

---

## Tasks

| ID | Title | Severity | Area | Dependencies | Acceptance criteria | Status | Evidence |
|---|---|---|---|---|---|---|---|
| **T1** | `SetRoleCells` missing the system-role, brand-isolation and rank-escalation guards that `AssignPermission` enforces (audit A-1) | **Critical** | backend + db | — | The cells endpoint refuses, for a non-platform-admin caller: (a) editing a system role, (b) editing a role outside the caller's brand, (c) editing a role of higher rank than the caller's own. Platform admin unaffected. Existing self-lockout guard retained. Tests cover all three refusals plus positive controls. Live: escalation 403s and writes nothing; legitimate role management still saves. | **Done** | See T1 in the evidence log |
| **T2** | Permission catalogue drift — codes live in the DB that `IdentitySeeder.PermissionDefs` does not declare (audit A-5) | High | backend/db | — | Every code any repo SQL file inserts is declared in `PermissionDefs` with the module/action/name/risk that SQL wrote. A drift test fails the build if the two diverge again. Seeder idempotent. Live: deleting a folded-back code and restarting recreates it identically. | **Done** | See T2 in the evidence log |
| **T5** | RLS does not reach the franchise/store boundary — GUCs published, read by zero policies (audit A-6) | High | db/backend | — | 39 tables carry a RESTRICTIVE policy reading the `scope_nodes` GUC, matching `IsWithinScope` semantics including the unresolved-vs-empty sentinel. Migration with rollback. Real-PostgreSQL tests as non-owner prove in-scope allowed, out-of-scope denied, brand caller unchanged, bypass unaffected, writes controlled. Zero regression, measured. | **Done** | See T5 in the evidence log |
| **T3** | No endpoint lists a person's existing scope memberships; the panel is add-only and says so (audit A-4, part 1) | High | backend/web | — | `GET .../people/{id}/memberships` returns live memberships with resolved scope names, brand-scoped, gated on `users.read`. Panel renders them with loading/empty/error states; revoke works on any membership. Disclaimer gone. | **Done** | See T3 in the evidence log |
| **T4** | No endpoint lists a person's existing permission overrides; the panel is add-only and says so (audit A-4, part 2) | High | backend/web | T3 | `GET .../people/{id}/permission-overrides` returns live overrides with permission name, effect, resolved scope, reason and expiry; brand-scoped, gated on `users.read`. Panel lists them with loading/empty/error states and one-click edit/clear. Disclaimer gone. | **Done** | See T4 in the evidence log |
| **T6** | Self-signup brand owner is typed `staff` while holding `brand_admin`; franchise owner is typed to match its role (audit A-2) | Medium | backend/db | — | Both flows derive `user_type` from the primary role via one shared rule; 12 existing owners migrated. Live: fresh signup produces a matching pair, and the settings page the mismatch was blocking now loads. | **Done** | See T6 in the evidence log |
| **T7** | `UserType.franchise_owner` vs `NotificationRecipientType.franchisee` name the same person differently (audit A-3) | Low | backend/db | — | One spelling (`franchise_owner`) across both enums and the notifications CHECK constraint, with a migration for stored values. Legal `franchisee_*` contract columns deliberately unchanged. | **Done** | See T7 in the evidence log |
| **T8** | rider-mobile auth header asserts password login "not OTP"; shipped login is 100% OTP and the password path is unreachable (audit A-7) | Low | mobile | — | Header corrected; `passwordLogin`, its request type and two further stale comments removed. OTP login re-verified end to end on a real Android emulator. | **Done** | See T8 in the evidence log |

---

## Evidence log

Appended per task as it completes: what was tested, commands run, API responses, and confirmation of
zero console errors.

### T1 — guard asymmetry on `SetRoleCells` · **Done**

**What was wrong.** Two endpoints write `identity_access.role_permissions`, both behind
`permissions.assign`. `AssignPermission` carried three authority guards inline; `SetRoleCells` — the
matrix save, which flips many permission codes per cell — carried none. Duplication is what let them
drift.

**Changed.**
- `core.Application/Identity/Common/RoleEditGuard.cs` (new) — one implementation of "may this actor
  change what this role is allowed to do": system-role, brand-isolation and rank guards.
- `AssignPermission.cs` — inline guards replaced by the shared call.
- `SetRoleCells.cs` — now calls it, before any cell is resolved.
- `db/migrations/0030_roles_global_visibility.{up,down}.sql` (new) — see "the second defect" below.

**The second defect, found by the fix.** With the rank guard added, the *negative control* failed:
a brand admin could no longer edit their own junior role either. Cause: `identity_access.roles`
carried the generic `rls_brand` policy, whose `brand_id = kernel.current_brand_id()` is right for a
NOT NULL brand column and wrong here, where NULL means "global". All 17 system roles were therefore
invisible to every brand session, so the guard could not resolve the caller's own rank (their
`brand_admin` row was hidden) and refused everybody. The application layer already stated the
opposite rule in `GetAccessRoles` — "System roles (BrandId == null) are global" — so handler and
policy disagreed and the policy silently won. Migration 0030 splits the policy per command: SELECT
admits `brand_id IS NULL`, INSERT/UPDATE/DELETE stay strict. `FOR ALL` would not do — one USING
clause covers reads *and* deletes, and WITH CHECK does not cover DELETE, so a relaxed `FOR ALL`
let a brand session delete the global `brand_admin` row (measured, before the split).

**Live test — API.** Constructed the realistic exploit: a `support_lead` principal (rank 28) in a
brand that had delegated `permissions.assign` to that role, editing `operations_manager` (rank 25).

| | pre-fix build | post-fix build |
|---|---|---|
| rank-28 edits rank-25 role | **HTTP 200**, grants 31 → 34 incl. `royalty.override` | **HTTP 403** "You cannot modify a role with higher privileges than your own", grants 31 → 31 |
| brand admin (20) edits rank-25 role | 200 | **200**, grants appear then revert cleanly |
| brand admin edits global `auditor` | 404 (hidden by RLS) | **403** "Only a platform administrator may modify a system role", 0 rows written |
| platform admin edits `auditor` | 200 | **200** |

**Live test — RLS (`psql` as non-owner `app_user`, brand-scoped session).** Global roles visible 17;
other brands' custom roles 0; `UPDATE` a global role → `UPDATE 0`; `DELETE` a global role →
`DELETE 0`; `INSERT` a global role → `42501`; own-brand role `UPDATE` → `UPDATE 1` (control).

**Live test — Chrome** (`http://localhost:5174/access-control?tab=roles`, signed in as the brand
admin). Roles list went from 4 roles to **21** — the system catalogue is reachable for the first
time, with the Franchise group no longer empty. Saving a cell on the system role `Auditor` shows the
step-up modal, then a toast reading "Only a platform administrator may modify a system role"; saving
a cell on `Operations Manager` succeeds and persists. Network tab: `POST .../roles/68d4918f…/cells`
→ **403**, `POST .../roles/be8add39…/cells` → **200**. Console: zero application messages (the one
entry is a Chrome extension's own content script). Loading, saved and error states all render.

**Live test — mobile:** not applicable, and verified rather than assumed — a grep of
`customer-mobile/src` and `rider-mobile/src` finds no reference to `access-control`, `/cells` or
`permissions.assign`. Only `admin-web` reaches this endpoint.

**Tests added.** `RoleEditGuardTests.cs` (10, real PostgreSQL) — three refusals, revoked-membership
and no-membership fail-closed, three positive controls, the escalation driven through
`SetRoleCellsCommandHandler` proving zero rows written, and 404-not-403 for a missing role.
`RolesGlobalVisibilityRlsTests.cs` (8, migration applied verbatim from disk, driven as non-owner) —
global read, cross-tenant isolation preserved, bypass, and the three write refusals plus the
own-brand write control. Test bodies were verified to actually execute against PostgreSQL by
temporarily inverting an assertion and confirming the failure.

**Build & tests.** Backend `dotnet build` 0 errors; `dotnet test` **799 passed / 0 failed**
(baseline 781, +18 new). admin-web `npm run build` clean; `npm run lint` 0 errors, 12 pre-existing
warnings, none in touched files.

**Decisions made autonomously.**
- The guard was extracted to one shared type rather than copied into the second handler, because
  copying is what produced the finding.
- Migration 0030 was written even though the audit did not name it: without it the A-1 fix cannot
  function, and the disagreement it resolves is between two layers already in the repo.
- The write path was deliberately not relaxed, and the policy split per command once measurement
  showed a `FOR ALL` form would have widened DELETE.
- The audit's framing of A-1 as a system-role hole is **partly overstated and is recorded as such**:
  RLS was already refusing that case with a 404. The live-exploitable half was rank escalation
  *within* a brand, which no layer covered. Both are now closed, at both layers.

### T2 — permission catalogue drift · **Done**

**What was wrong — and it was larger than the audit said.** The report describes one drifted code
(`dispatch.mode.manage`, "165 live vs 164 seeded"). Measured against the live database there were
**seven**, and the live catalogue holds **171** codes, not 165:

```
$ comm -23 <db codes> <PermissionDefs codes>
api_keys.manage · dispatch.mode.manage · domains.manage · domains.read
impersonation.approve · impersonation.request · white_label.read
```

Each entered through a SQL file — `0014_impersonation_grants`, `0016_api_keys`,
`0019_premium_feature_modules`, `patches/dispatch_permissions.sql` — and was never folded back, so a
database built from the C# seeder alone was missing all seven.

**Changed.**
- `IdentitySeeder.cs` — the seven added to `PermissionDefs` with the module/action/name/risk their
  SQL files wrote (verified field-by-field against the live rows), each commented with its origin
  file; `PermissionDefs` made `internal` with `InternalsVisibleTo` on core.Infrastructure.
- `tests/core.Tests/Rbac/PermissionCatalogueDriftTests.cs` (new).

**The drift test.** Reads every `*.sql` under `db/` and `database_scripts/`, locates the `code`
column by name in each `INSERT INTO identity_access.permissions (…)` header, and reads it
**positionally** out of every row — handling both the `VALUES (…), (…)` and
`SELECT … WHERE NOT EXISTS` shapes this repo uses. Positional parsing is not incidental: the
`action` column legitimately holds dotted values such as `booking.create` and `invoice.read` (the
`partner_*` family splits its codes that way), so a regex for "quoted strings that look like codes"
reports twelve codes that do not exist. It also asserts the parse is non-empty, so a renamed file
cannot make the test vacuously pass.

Two further tests: no duplicate codes (the seeder keys by code, so a duplicate is silently
idempotent until two entries disagree on risk), and every declared `risk_level` satisfies the
table's `CHECK (risk_level = ANY (ARRAY['low','normal','high','critical']))` — a typo there is not a
compile error, it is a 23514 on a fresh environment.

**A test I wrote and removed.** I first asserted `Code == $"{Module}.{Action}"`. It failed on nine
rows, and the rows were right and the assertion was wrong: the `partner_*` family deliberately sets
`module='partner_booking', action='booking.create'`, and `PermissionMatrix` parses module/action out
of the code string itself rather than those columns. Asserting a false invariant is worse than
asserting none, so it was replaced by the risk-level check above.

**Live test — the fold-back actually works.** Deleted `dispatch.mode.manage` and its grants from the
live database (171 → 170), restarted `core.WebApi`, and the C# seeder recreated it:

```
dispatch.mode.manage | dispatch | manage | Manage dispatch mode | high | t     ← identical
total permissions: 171
```

**Live test — idempotency.** A second consecutive restart logged only `Seeding complete.` with no
`Seeded N` lines, and `role_permissions` grouped by minute shows zero rows written on that run.
Duplicate codes in the table: 0.

**Live test — the drift guard catches drift.** Deleting one folded-back line from `PermissionDefs`
fails the test with the code named:
`These permission codes are inserted by a SQL file but are not declared in
IdentitySeeder.PermissionDefs … - dispatch.mode.manage`. Restored, green again.

**Live test — API.** `platform_admin`'s token carries all seven codes and **171** permissions total;
`GET /admin/access-control/roles` → 200, 21 roles.

**Live test — Chrome.** `/access-control?tab=roles` signed in as platform admin: 15 roles under this
brand's vertical/entitlement gate, matrix renders every module row with correct ticks, Licensing tab
present. Console: zero application errors (only a Chrome extension's own content script).

**Live test — mobile:** not applicable, verified by grep — neither `customer-mobile/src` nor
`rider-mobile/src` references any of the seven codes, and neither reads a permissions claim.

**A real behaviour change, recorded deliberately.** Folding the codes in put them inside the
seeder's `Deny("auditor", mutatingCodes)` sweep for the first time, so the restart wrote four new
`auditor: deny` rows (`api_keys.manage`, `dispatch.mode.manage`, `domains.manage`,
`impersonation.approve`). This is a tightening in the intended direction: previously auditor held no
row for those codes, so absence-of-grant was the only thing stopping them — and absence loses to an
allow from a second role, whereas a deny wins. No role gained a permission; the only `allow` written
was the one I had deleted to run the test.

**Also observed (not an audit finding, recorded for the final report).** The seeder runs on every
`core.WebApi` start in Development, so migration `0023` — which revoked `auditor`'s deny-propagated
`pricing.slab.manage` — is partly undone on the next restart, which re-adds it as an explicit
`deny`. The effective grant is the same either way, so this is drift rather than a defect, but the
migration and the seeder do disagree about whether that row should exist.

**Build & tests.** `dotnet build` 0 errors; `dotnet test` **802 passed / 0 failed** (was 799, +3).

### T5 — the franchise/store boundary reaches the database · **Done**

**What was wrong — measured, and worse than reported.** The audit says the boundary "is enforced
only in application code (roughly 95 call sites), not the database". Before changing anything I
asked four principals at four scope levels for the same brand's orders through the live API:

| principal | scope_nodes | orders returned |
|---|---|---|
| platform_admin | `platform brand` | 6 |
| brand_admin | `brand:5b37…` | 6 |
| store_admin | `store:db41…` | 6 |
| warehouse_staff | `warehouse:0e70…` | 6 |

Identical. The 95 call sites are *mutating* handlers, so a read never meets the rule — the boundary
was absent from the database **and** from the list path. Migration 0031 is the first layer of any
kind to express it on reads.

**Changed.** `db/migrations/0031_subbrand_scope_rls.{up,down}.sql`:
- `kernel.within_scope_cols(brand, franchise, store, warehouse)` — a column-argument sibling of
  `ICurrentUser.IsWithinScope` and the ABAC engine's `authz.within_scope(jsonb)`, reading the same
  `app.current_scope_nodes` GUC with the same three-state handling.
- A **RESTRICTIVE** `rls_subbrand_scope` policy on **39 tables**. Restrictive matters: the existing
  `rls_brand` policies are permissive and PostgreSQL ORs those, so another permissive policy could
  only widen access. Restrictive is ANDed, leaving all 136 existing policies untouched and unable to
  grant anything. The effective rule per row is `(brand policy) AND (bypass OR within-scope)`.

**The one deliberate difference from `IsWithinScope`, and why.** A row whose column for the caller's
scope *level* is NULL does not constrain that caller. `IsWithinScope` denies there — it can afford
to, because it is only ever handed a resource that has those ids. A policy sees every row of every
table. Measured on live data, the strict rule would have hidden **977 of 977** `audit_logs`, **16 of
16** `system_settings`, **4 of 4** `price_lists` and **3 of 3** `fulfillment_unit_tags` from every
franchise- and store-scoped user — and every order from warehouse staff, because
`orders.warehouse_id` is NULL on all 9 rows and `store_warehouse_mappings` is empty, so no
data-model path from a warehouse membership to an order exists at all. The arm costs nothing that
was ever protected (a row naming no store is not another store's row) and keeps the whole point: a
row naming a *different* franchise or store is now refused by the database.

**Live test — the boundary, at the database, as non-owner `app_user`.**

| session scope_nodes | orders | settings | audit | price_lists | stores |
|---|---|---|---|---|---|
| `brand:5b37…` | 9 | 16 | 101 | 4 | 2 |
| `franchise:77e7…` | 9 | 16 | 101 | 4 | 1 |
| `store:db41…` | 9 | 16 | 101 | 4 | 2 |
| `warehouse:0e70…` | 9 | 16 | 101 | 4 | 2 |
| `platform` | 9 | 16 | 101 | 4 | 2 |
| **a different store** | **0** | 16 | 101 | 4 | 2 |
| **a different franchise** | **0** | 16 | 101 | 4 | **0** |
| empty claim | 0 | 0 | 0 | 0 | 0 |
| GUC never set (unresolved) | 0 | — | — | — | — |
| bypass | 9 | — | — | — | 4 |

Every legitimate level unchanged; a foreign store or franchise drops to zero; both fail-closed arms
hold; bypass unaffected.

**Live test — the API, re-run identically after the change.** platform_admin 6 · brand_admin 6 ·
store_admin 6 · warehouse_staff 6. **Zero regression.**

**Live test — every schema 0031 touches still serves reads** (platform admin, HTTP 200 each):
users, stores, warehouses, franchises, orders, pickup-requests, customers, price-lists, riders,
payments, cash-books, expenses, royalty-invoices, analytics dashboard, navigator.

**Live test — Chrome**, signed in as the **store-scoped** user (the case most affected):
`/orders` renders 5 active orders plus history from real data; `/warehouse/board` renders with its
empty state ("No garments in flight right now"); `/riders` lists 2 real riders with franchise names.
Zero application console errors — the only console entries are a Chrome extension's own content
script. The 403s visible in the network tab (`entitlements/…/features`, `settings/`) are the
permission layer denying a store_admin who holds none of `saas.read` / `settings.read`, confirmed
against `role_permissions`; `tenancy_org.brands` carries no `rls_subbrand_scope` policy, so 0031
cannot be their cause.

**Live test — mobile, on a real Android emulator.** This is the leg that matters most for T5,
because a rider is a **franchise-scoped principal** reading franchise/store-scoped tables through
the new policy. rider-mobile launched in Expo Go against the standalone hosts (`adb reverse` on
5056/5015/5242/9092), signed in with the real 6-digit phone OTP, and rendered live data:
`QA-RIDER-001`, the before-you-ride checklist with real vehicle-doc status, and
**"1 task waiting in your zone (Laundry Ghar Mumbai Central)"** — a store name resolved through the
franchise scope under the new restrictive policy. Earnings screen renders its empty state correctly
(₹0.00 last 30 days; the ₹50 settlement is older). Rider self endpoints checked directly:
`/rider/me` 200, `/rider/assignments/today` 200, `/rider/balance` 200 (₹50.00 real), `/rider/documents`
200. The empty `assignments/today` is data, not policy — the single assignment row is dated
2026-06-13 and a franchise session sees it fine (`count = 1`). Metro logs carry no application
error; the one ERROR is Expo Go's documented removal of push notifications in SDK 53, which needs a
development build and is unrelated. `npm run typecheck` clean, `npm test` **91 passed**.

**Tests added.** `SubBrandScopeRlsTests.cs` — 15 tests, migration applied verbatim from disk to real
schema-qualified tables (`order_lifecycle.orders`, `kernel.system_settings`, `tenancy_org.stores`,
three different column shapes), every query as non-owner `app_user`, with the real permissive
`rls_brand` policies in place so the composition under test is the real one. Covers: no regression
for brand/warehouse/store/platform/bypass; the hole closed for a foreign store, franchise and
warehouse; unresolved vs. resolved-empty; an unrecognised scope type neither granting nor vetoing;
and the write path (cross-store INSERT → 42501, own-store INSERT → 1 row, cross-store UPDATE → 0 rows).

**Rollback verified:** `migrate.sh down 1` removes all 39 policies and the function; `up` restores
39 and 1.

**Build & tests.** `dotnet build` 0 errors; `dotnet test` **817 passed / 0 failed** (was 802, +15).

**Left deliberately undone, and why.** The warehouse arm is implemented and tested, but it cannot
bind on `order_lifecycle.orders` in practice because the application never populates
`orders.warehouse_id` (0 of 9 rows) and `tenancy_org.store_warehouse_mappings` is empty. Enforcing
it strictly would show warehouse staff nothing. Closing that properly is a data-model change —
populate `warehouse_id` on routing, or resolve warehouse→store through the mapping table — and is
out of scope for an audit finding about RLS coverage. **Recorded as outstanding in
`docs/FIX_REPORT.md`.**

**Also observed (not an audit finding).** `scripts/smoke.sh` probes ports 5050/5002/5005/8080 and
fails all 16 checks with connection-refused on any current machine; the hosts moved to
5056/5015/5242 in `docs/PORTS.md` on 2026-08-25 and the script was never updated. The equivalent
probes were run by hand instead (all green, above).

### T3 — a person's memberships can be read · **Done**

**What was wrong.** Grant and revoke existed; nothing could list. The panel could only offer to
revoke what the current browser session had just granted, and said so in its own copy: *"Existing
memberships aren't listed here yet — the API has no per-user memberships read endpoint."* Anything
granted yesterday was invisible and unrevokable from the UI.

**Changed.**
- `GetPersonMemberships.cs` (new) + `PersonMembershipDto` — live memberships with the scope **name**
  resolved server-side, so the panel can label a scope without holding every franchise/store/
  warehouse list in memory.
- `GET /api/v1/admin/access-control/people/{id}/memberships`, gated on `users.read` (the same gate
  `GetUserById` uses).
- admin-web: `getPersonMemberships`, `usePersonMemberships` (invalidated by both grant and revoke),
  and `MembershipsPanel` rewritten to render the server list. The disclaimer is deleted, not
  reworded.

**Tenancy.** The target is resolved through `UserBrandScope.ScopedToCallerBrand` — the shared
predicate from the F-1/F-2 fix — rather than by id alone, and memberships are then filtered to the
caller's brand as well, since one person may hold memberships under two brands.

**A bug my own first version introduced, caught by testing it.** Granting `Auditor` through the UI
succeeded, and the new membership did not appear. It was not a refresh failure: `Auditor` is a
platform-scoped role, so the membership had `scope_type = 'platform'` and no brand — and my brand
filter could never match it. That is the *same* blind write A-4 describes, reintroduced by the fix
for it. Worse, `GrantMembership` only permits a platform-scoped grant from a platform admin (a
platform target resolves to an all-null ancestor chain, which only `IsWithinScope`'s platform arm
satisfies), so the one caller who could create such a membership was the one caller who could not
see it. The query now returns platform-scoped memberships to a platform admin and to nobody else —
brand admins still do not see platform staff structure, matching `UserBrandScope`'s own exclusion.

**Live test — API.**

| caller | target | result |
|---|---|---|
| platform admin | brand admin (own brand) | 200, 1 membership, `scopeName: "Laundry Ghar"` |
| brand admin | store admin (own brand) | 200, 1 |
| brand admin | a user in another brand | 200, **0** |
| brand admin | a fabricated uuid | 200, **0** (byte-identical — no existence oracle) |
| platform admin | store admin, after granting Auditor | 200, 2 — `store_admin @ store`, `auditor @ Platform` |
| brand admin | same person | 200, **1** — the platform row stays hidden |
| after revoking via the API | same person | 200, **1** |

**Live test — Chrome.** Opened the store admin's drawer: "Additional memberships" now lists
**Store Administrator ⭐ Primary · Laundry Ghar Mumbai Central** — a membership granted in July,
which the old panel could never have shown. Granted `Auditor` through the real UI (step-up modal →
OTP → grant); after the platform-scope fix it appears as **Auditor · Platform**. Revoked it and
reloaded: the row is gone, proving the list is read from the server rather than held in component
state. Empty state checked on a person with no memberships: *"No additional memberships yet."*
Zero application console errors.

Revoke was exercised through the API rather than the button because the panel's revoke path calls
`window.confirm`, and a native browser modal blocks the automation extension entirely. The button
is wired to the same mutation; the reload proves the resulting state.

**Live test — mobile:** not applicable, verified by grep — no membership or access-control route
appears in `customer-mobile/src` or `rider-mobile/src`.

**Tests added.** `PersonMembershipsQueryTests.cs` — 7 tests on real PostgreSQL: resolved scope names
and primary-first ordering, revoked excluded, expired excluded, another brand's membership excluded,
a foreign person yields nothing, a foreign person and a fabricated id are indistinguishable, and the
platform-membership visibility rule in both directions.

**One EF trap worth recording.** The first version ordered *after* projecting into the DTO. That
compiles and throws at runtime — `The LINQ expression … could not be translated`, because EF cannot
order by a constructed record. Ordering now happens on the joined rows before the `Select`.

**Build & tests.** `dotnet build` 0 errors; `dotnet test` **824 passed / 0 failed** (was 817, +7).
admin-web build clean, lint clean on touched files.

### T4 — a person's permission overrides can be read · **Done**

**What was wrong.** The panel could set and clear an override but never show one, and said so:
*"Current overrides aren't listed here yet — the API has no per-user overrides read endpoint."* The
ABAC plan had recorded the same gap independently as task A8.2. An override is the sharpest
instrument in the model — one allow or deny on one person, where deny beats every role grant — so
being unable to read one back meant nobody could answer "what exceptions does this person carry"
without opening the database.

**Changed.**
- `GetPersonPermissionOverrides.cs` (new) + `PersonPermissionOverrideDto`, carrying the permission's
  display **name** and module and the resolved scope name, so the panel need not cross-reference the
  catalogue or hold every node list.
- `GET /api/v1/admin/access-control/people/{id}/permission-overrides`, gated on `users.read`.
- admin-web: `getPersonPermissionOverrides`, `usePersonPermissionOverrides` (invalidated by the set
  mutation), and an "Overrides in force" list in `PermissionOverridesPanel` with an **Edit / clear**
  button that loads a row back into the form — so clearing one is a click rather than retyping its
  code and scope.

**Two deliberate rules.** Expired rows are omitted, because permission resolution already ignores
them and listing one would report authority the person does not have. Denies sort first, being the
stronger statement and the one an admin most needs to see.

**Live test — API.** Round trip through the real write endpoint and back:

```
BEFORE                                            count: 0
POST …/permission-override  {orders.refund, deny} HTTP 200
AFTER                                             count: 1
   deny | orders.refund | Refund order | scope: global | reason: T4 verification — suspend refunds
```

Then a franchise-scoped, time-boxed allow, plus an already-expired row inserted directly:

```
listed:  deny  | orders.refund | scope: global                     | expires: None
         allow | royalty.read  | scope: Laundry Ghar Franchise One | expires: 2027-01-01
in the table: 3 rows — catalog.export (expired) present in SQL, correctly absent from the list
```

**Live test — Chrome.** The drawer's "OVERRIDES IN FORCE" section renders both: a red **Denied ·
Refund order · `orders.refund`** with its reason, and a green **Allowed · Read royalty invoices ·
`royalty.read` · 📍 Laundry Ghar Franchise One · until 01/01/2027**. Clicked **Edit / clear** on the
deny — the form prefilled with effect, scope and reason and auto-expanded its advanced section —
then **Clear**, which took the step-up modal, and the panel reported *"Override for 'orders.refund'
cleared"* with the row disappearing from the list on its own via the query invalidation. Empty state
verified: *"None — this person's access comes entirely from their roles."* Zero application console
errors.

**Live test — mobile:** not applicable — no override or access-control route appears in either
mobile app.

**Tests added.** `PersonPermissionOverridesQueryTests.cs` — 5 tests on real PostgreSQL: live rows
with permission and resolved scope detail and deny-first ordering, expired excluded, a person
outside the caller's brand yields nothing, foreign and fabricated ids are indistinguishable, and a
platform admin with no brand selected is unfiltered.

**Build & tests.** `dotnet build` 0 errors; `dotnet test` **829 passed / 0 failed** (was 824, +5).
admin-web build clean, lint clean on touched files. All test data created during verification was
deleted afterwards.

### T6 — the two owner-creation flows agree · **Done**

**What was wrong.** Two flows create an account together with its primary role, and each wrote the
`user_type` next to the role code by hand:

```
CompleteSignup (self-serve)   user_type 'staff'            + role brand_admin      ← disagreed
InviteOwner    (franchise)    user_type 'franchise_owner'  + role franchise_owner
```

**It was not cosmetic, and I measured the harm.** Type and role are independent axes, and code
reading the wrong one gets the wrong answer silently. `AdminSettings.Forbidden` gates on
`UserType == "brand_admin"` rather than on a permission, so a self-signed-up owner holding the
brand_admin *role* was refused their own brand's settings:

```
GET /api/v1/admin/settings/  as owner@lgmain.test
   before:  HTTP 403
   after:   HTTP 200   {"email":{"enabled":true,"host":"smtp.gmail.com",…}}
```

**Changed.**
- `UserType.ForPrimaryRole(roleCode)` (new) — one mapping from a primary role to the type that
  mirrors it. Roles with no distinct tier (vertical operational roles, a brand's own custom roles)
  fall through to `Staff`, which is what they already were.
- `CompleteSignup` and `InviteOwner` both now name their role once as `OwnerRoleCode` and derive the
  type from it, so the pairing cannot drift again. This is the same shape as the A-1 fix: the defect
  was two hand-written copies of one rule, so the rule now has one home.
- `db/migrations/0032_signup_owner_user_type.{up,down}.sql` — corrects existing rows.

**The migration's predicate is deliberately narrow.** Only accounts whose *primary*, live,
brand-scoped membership is `brand_admin` are touched; someone merely holding an extra brand_admin
membership is not an owner, and revoked/expired/soft-deleted rows are excluded. It raises nobody's
authority — a type is not a permission and the role was already there.

**Live test — data.** 12 live accounts held a primary brand-scoped `brand_admin` membership while
typed `staff`. After the migration: `brand_admin | 12`, none left mistyped.

**Live test — a fresh signup, end to end.** `POST /api/v1/signup/start` → 200, then
`POST /api/v1/signup/complete` with the non-prod OTP → 200, provisioning a real tenant (brand,
`bundleCode: pro`, 3 catalog categories, 8 items). The owner it created:

```
+919812340001 | brand_admin | brand_admin | brand | T6 Verify Laundry
```

Type and role agree. Repeated after refactoring both flows onto `ForPrimaryRole` to confirm the
refactor preserved it. Both verification tenants and their owners were deleted afterwards.

**Live test — Chrome.** Signed in as `owner@lgmain.test` and opened `/settings`: the page the
mismatch was blocking now renders fully — Email & SMTP with real SMTP host and credentials state,
plus Payments, WhatsApp, SMS, Maps, Fare & pricing, Dispatch, Business rules, Custom domains and
Rider payouts. Zero application console errors.

**Live test — mobile:** not applicable — neither mobile app reads `user_type` for gating, and
neither has a signup or owner-invite surface.

**Tests added.** `OwnerShapeConsistencyTests.cs` — 15 tests: both owner roles map to their matching
type, every seeded role whose code is also a user type maps to it, roles with no distinct tier fall
back to `staff` (including null and empty), and every value the mapping can return is a valid
`UserType` — the last one matters because the mapping writes straight into a column with a CHECK
constraint, so a bad value is a 23514 at account creation rather than a compile error.

**Build & tests.** `dotnet build` 0 errors; `dotnet test` **844 passed / 0 failed** (was 829, +15).

**Observed, not changed (recorded for the final report).** `AdminSettings.Forbidden` gates on
`user_type` where every other endpoint gates on a permission. Its own comment calls it
"defence-in-depth… the permission policy is the authoritative gate", but it reads the identity axis
rather than the authority one, which is why A-2 could deny a legitimate owner at all. My fix makes
it correct for owners; the check itself is still the wrong shape and is worth replacing with a
permission gate. Outside this finding's scope.

### T7 — one spelling for the franchise owner · **Done**

**What was wrong.** `UserType` spelled the role `franchise_owner`; `NotificationRecipientType`
spelled the same real person `franchisee`.

**It reaches users, which is why it is worth fixing rather than tolerating.** admin-web's
Notification Outbox and Logs tabs render `recipientType` directly into the Recipient column when a
recipient has no phone or email (`NotificationOutboxTab.tsx:69`, `NotificationLogsTab.tsx:73`), so
an operator could read "franchisee" on one screen and "franchise_owner" on every other.

**Measured before deciding which spelling wins.** `franchise_owner` is load-bearing in
`users.user_type`, `roles.code`, the JWT claims and their CHECK constraints. `franchisee` as a
recipient type appears in exactly one enum, has **zero code references anywhere** (the whole
`NotificationRecipientType` class had no usages outside its own file), and matches **zero rows** in
either notifications table — both hold only `customer`. Aligning the unused name onto the
load-bearing one is the cheap and correct direction.

**Changed.**
- `NotificationRecipientType.Franchisee` → `FranchiseOwner = "franchise_owner"`.
- `db/migrations/0033_notification_recipient_franchise_owner.{up,down}.sql` — moves the
  `notifications_outbox` CHECK constraint and migrates stored values in both tables. The UPDATEs are
  no-ops on this database and exist so the migration is correct on one where they are not; the
  constraint is widened before any row is rewritten, since the reverse order cannot work.

**Deliberately not renamed.** `tenancy_org.franchise_agreements.franchisee_legal_name / _pan /
_gstin / _phone / _email` keep their names: there the word names the contracting party in a legal
instrument, which is the correct term for that job. The finding is about two enums naming one person
two ways, not about the contract's vocabulary. Recorded in the migration so the next reader does not
"finish" the rename.

**Live test — the constraint actually moved.** Inserting a real outbox row inside a rolled-back
transaction:

```
[franchisee]       ERROR: new row … violates check constraint notifications_outbox_recipient_type_check
[franchise_owner]  INSERT 0 1
```

**Rollback verified:** `migrate.sh down 1` restores `franchisee` in the constraint, `up` restores
`franchise_owner`.

**Live test — Chrome.** `/cms → Outbox` renders 9 real notification rows (channel, template code,
recipient, attempts, scheduled/sent, status, error) and `/cms` itself lists 35 real templates. The
`recipientType` fallback is not exercised by this data because every stored row has a phone number —
which is the same fact the measurement above found. Zero application console errors.

**Live test — mobile:** not applicable — neither mobile app references a recipient type.

**Tests added.** `NotificationRecipientNamingTests.cs` — 3 tests: the two enums name the person
identically, no constant in `NotificationRecipientType` still spells it `franchisee` (asserted over
the whole type by reflection, so re-adding the old spelling under any name fails too), and every
staff-shaped recipient type matches a real `UserType`.

**Build & tests.** `dotnet build` 0 errors; `dotnet test` **847 passed / 0 failed** (was 844, +3).
admin-web build clean.

### T8 — rider-mobile documents what it actually ships · **Done**

**What was wrong.** `rider-mobile/src/api/auth.ts` opened with *"Riders are SYSTEM users
(user_type='rider') — they log in with a password, not OTP. The endpoint is
POST /api/v1/auth/password/login."* Every clause of that was false: the shipped login screen asks
for a phone number and calls `sendLoginOtp`, and the `passwordLogin` helper the comment described
was **exported and called from nowhere** — confirmed by grep across `src/` and `app/`.

**Changed.** `passwordLogin` removed, along with its now-unused `PasswordLoginRequest` type and its
import. Two further stale comments the audit did not name but which said the same wrong thing were
corrected: the Auth DTO block header in `src/types/api.ts` ("rider uses the shared password login
endpoint") and the `identityClient` comment in `src/api/client.ts`.

**Why removed rather than wired up.** The acceptance criterion allowed either, and wiring a password
screen would be inventing a feature nobody asked for and contradicting the shipped design — the app
is deliberately OTP-only, which the login screen's own copy ("Use your registered partner number")
states. The backend endpoint still exists and other clients use it, so a rider password flow can be
added deliberately later rather than lingering as something that looks supported and is not. The new
header records that reasoning so the next reader does not "restore" it.

**Live test — mobile, on a real Android emulator.** rider-mobile relaunched in Expo Go after the
removal: bundled in 711 ms, the sign-in screen rendered, and the full 6-digit phone OTP login
completed — landing on the rider home with live data (`QA-RIDER-001`, before-you-ride checklist,
*"1 task waiting in your zone (Laundry Ghar Mumbai Central)"*). Metro logs contain no application
error; the one ERROR is Expo Go's documented removal of push notifications in SDK 53, which needs a
development build and fires *after* a successful login — itself evidence the login worked.

`npm run typecheck` clean · `npx eslint` clean on the touched files · `npm test` **91 passed**.

**Live test — web:** not applicable; the change is confined to rider-mobile.

**Build & tests.** Backend untouched by this task and still **847 passed / 0 failed**.
