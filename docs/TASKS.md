# LaundryGhar Platform — Execution

> **Single source of truth for execution progress.**
>
> **Why this file and not Notion:** no Notion integration is connected to this session (the
> available MCP servers are claude-in-chrome, Gmail, Google Calendar, Google Drive, Hostinger
> billing/DNS/domains/hosting/reach, and reticle — none of them Notion). Per the fallback
> instruction, `docs/TASKS.md` is the tracker. If a Notion integration is connected later, these
> rows map 1:1 onto a Notion board with the same columns.
>
> **Source:** every task below is a gap from `docs/GAP_ANALYSIS.md` §3. Nothing here is invented
> scope; anything genuinely undecided is in `docs/GAP_ANALYSIS.md` §5 (Open questions), not here.
>
> **Rules:** a task moves to `Done` only when it is implemented **and** the full build + full test
> suite are green with zero new errors, with the passing output pasted into its Evidence field.
> If it cannot be finished, it goes to `Blocked` with the reason — never `Done`.

**Status values:** `Todo` · `In Progress` · `Blocked` · `Done`

---

## Baseline (must stay green after every task)

| Surface | Command | Baseline (2026-08-24) |
|---|---|---|
| Backend build | `cd backend/laundryghar && dotnet build laundryghar.slnx` | 0 errors · 62 pre-existing warnings |
| Backend tests | `cd backend/laundryghar && dotnet test laundryghar.slnx` | 337 passed / 0 failed |
| admin-web | `cd admin-web && npx tsc -b && npx eslint .` | clean · 0 errors, 12 pre-existing warnings |
| pos-web | `cd pos-web && npx tsc -b && npx eslint .` | clean · 0 errors, 2 pre-existing warnings |
| customer-mobile | `cd customer-mobile && npx jest && npx tsc --noEmit` | 164 passed · clean |
| rider-mobile | `cd rider-mobile && npx jest && npx tsc --noEmit` | 85 passed · clean |

**Total baseline: 586 tests green.**

---

## Board

Ordered by dependency — work top to bottom, one at a time.

---

### T-01 · Make entitlement enforcement explicit and prove it

| Field | Value |
|---|---|
| **Phase** | P1 — Entitlements |
| **Gap** | E4, E5 |
| **Status** | `Done` |
| **Dependencies** | none |

**Description.** `ScopeResolver.BuildTokenClaimsAsync` and `GetNavigatorQueryHandler` both implement
entitlement filtering behind the config key `Entitlement:Enforced`. That key exists in no
`appsettings*.json` in the repo, so `GetValue<bool>` returns `false` and enforcement is off in every
environment. No test exercises the enforced path — all three `ScopeResolverTests` call sites pass
`enforceEntitlement: false`. Make the flag explicit in configuration and cover the enforced path
with tests that would fail if the filter regressed.

**Acceptance criteria.**
- `Entitlement:Enforced` is present with an explicit value in `core.WebApi` app settings (both base
  and Development), so the setting is discoverable and intentional rather than an implicit `false`.
- An integration test mints claims with `enforceEntitlement: true` for a brand-scoped user and
  asserts that a permission whose canonical owning module the brand has **not** licensed is absent
  from the resulting permission set.
- A companion test asserts the same permission **is** present when the brand has licensed the
  module, and that a `is_core` module's permissions survive regardless of licensing.
- A test asserts a platform admin is exempt from the filter.
- A navigator test asserts un-entitled non-core modules are hidden and core modules are not.
- Full backend build + full test suite green.

**Outcome note — the flag is explicit `false`, not `true`, and that is deliberate.**
The acceptance criterion was "present with an explicit value". I set it to `false` rather than
turning enforcement on, because turning it on today would break the live system: the backfill in
`brand_module_entitlement.sql` grandfathered brands into the modules that existed *at the time it
ran*, and seven active non-core modules have been added since. Verified against the live
`laundry_ghar_db`: the only brand (`LG-MAIN`) is licensed for **19 of 26** active non-core modules,
missing `appointments`, `audit`, `fabrics`, `items`, `partner_booking`, `platform_billing`,
`report`. Flipping the switch now would strip every user's permissions for those seven. It is also
premature: until T-04 bridges plans to entitlements, subscribing to a plan grants nothing, so
enforcement would deny without any way to buy access.

**Flipping it to `true` is now an explicit acceptance criterion of T-04**, where the bridge and a
brand-by-brand backfill make it safe.

**Evidence.**

Build (unchanged from baseline; the 64 count is the true full-restore figure — the baseline's 62 was
an incremental-build artifact that omitted 2 pre-existing `NU1903 SSH.NET` warnings from
Testcontainers, confirmed pre-existing by stashing the csproj change and rebuilding):
```
$ dotnet build laundryghar.slnx
    64 Warning(s)
     0 Error(s)
```

Full test suite — 344 passed, up from the 337 baseline (+7 new):
```
$ dotnet test laundryghar.slnx --no-build
Passed! - Failed: 0, Passed:  58, Skipped: 0, Total:  58 - core.Tests.dll
Passed! - Failed: 0, Passed: 208, Skipped: 0, Total: 208 - operations.Tests.dll
Passed! - Failed: 0, Passed:  78, Skipped: 0, Total:  78 - operations.IntegrationTests.dll
```

**Mutation check — proof the new tests actually bite.** Passing tests prove nothing on their own, so
the two filters were deliberately broken (`ScopeResolver`'s owner check forced to `return true`;
`GetNavigator`'s entitlement `.Where` forced to `true`) and the suite re-run:
```
$ dotnet test --filter FullyQualifiedName~EntitlementEnforcementTests
Failed! - Failed: 6, Passed: 1, Skipped: 0, Total: 7
```
6 of 7 failed. The 1 survivor is `navigator_without_brand_context_applies_no_entitlement`, which
asserts a module *is* shown — a mutation that shows everything cannot break it. Both mutations were
reverted and the full suite re-confirmed green (output above).

**Files changed.**
- `core.WebApi/appsettings.json`, `appsettings.Development.json` — explicit `Entitlement.Enforced: false`.
- `tests/operations.IntegrationTests/Rbac/EntitlementEnforcementTests.cs` (new) — 7 tests covering:
  unlicensed stripped / licensed survives / `is_core` always entitled / orphan (`module_key IS NULL`)
  always kept; expired and disabled licences do not entitle, `valid_until == today` still does;
  platform admin exempt; enforcement-off keeps everything; navigator hides un-entitled but never
  core; navigator with no brand context applies no entitlement.
- `tests/operations.IntegrationTests/Rbac/RbacEfFixture.cs` — applies the real
  `db/patches/brand_module_entitlement.sql` verbatim (so the tests hit the real FKs, RLS policy
  shape and `source` CHECK, not a stub); full-shape `identity_access.modules`; `brands.vertical_key`;
  a `kernel.current_brand_id()` stub the patch's `CREATE POLICY` resolves at creation time; a
  `SeedBrandAsync` helper.
- `tests/operations.IntegrationTests/Rbac/Fakes.cs` — `FakeCurrentUser.HasPermission` is now
  controllable via an init-only `Permissions` set (defaults empty, so existing usage is unchanged).
- `tests/operations.IntegrationTests/operations.IntegrationTests.csproj` — `Microsoft.Extensions.Configuration`
  for the in-memory config the navigator tests build. Added zero new build warnings.

Frontends were not touched by this task, so their baselines (admin-web, pos-web, customer-mobile,
rider-mobile) stand unchanged.

---

### T-02 · Separate sellable features from nav modules, and complete the feature catalog

| Field | Value |
|---|---|
| **Phase** | P1 — Entitlements |
| **Gap** | E7 |
| **Status** | `Blocked` |
| **Dependencies** | T-01 |

**Description.** `PLATFORM_STRATEGY.md` §5 defines a 17-entry feature catalog
(`bookings`, `scheduling`, `fleet`, `item_tracking`, `processing_facility`, `online_payments`,
`wallet`, `loyalty`, `coupons`, `customer_subscriptions`, `raas_partner`, `whatsapp_bot`,
`multi_location`, `advanced_analytics`, `api_access`, `custom_domain`, `white_label_app`).
`identity_access.modules` holds 29 rows shaped as *navigation entries*. Eleven of the seventeen
have no row at all, and the two concepts are conflated in one table.

**Blocked reason.** Resolving this requires **OQ-3** (`docs/GAP_ANALYSIS.md` §5): keep one table and
add an `is_sellable` flag to `modules`, or split into `features` + `modules` with a mapping table.
The two answers produce different schemas, different migrations, and a different `brand_module`
foreign key. Choosing either one myself would be inventing product scope, and the wrong choice is
expensive to unwind because `brand_module`, `module_bundle_item`, and the `ScopeResolver` filter all
key off `modules.key`.

**What I need from you.** A decision on OQ-3. I recommend the split (`features` + `modules`), because
§5 sells features and §7 navigates modules, and the current conflation is exactly why
`custom_domain` and `api_access` — which are sellable but have no menu — have nowhere to live.

**Acceptance criteria (once unblocked).**
- All 17 §5 feature keys exist and are entitleable.
- Existing module keys keep working — no brand loses an entitlement, asserted by a pre/post
  grant-count check per brand.
- A migration + rollback pair under `db/migrations/`.
- Full build + full test suite green.

**Evidence.** _(empty)_

---

### T-03 · Align bundles/plans with the §5 plan tiers

| Field | Value |
|---|---|
| **Phase** | P1 — Entitlements |
| **Gap** | E8 |
| **Status** | `Blocked` |
| **Dependencies** | T-02 |

**Description.** `identity_access.module_bundle` holds `starter`, `pro`, `enterprise`,
`salon-starter`. §5 defines `Starter / Growth / Pro / Enterprise` with different contents.
`module_bundle.price` is nullable and unset on the seeded three.

**Blocked reason.** Depends on T-02 (blocked), and independently on **OQ-4**: §5 gives no prices
("low monthly", "mid", "higher", "custom"). `module_bundle.price` and the plan catalog cannot be
seeded from adjectives.

**Acceptance criteria (once unblocked).**
- `growth` bundle exists; the four bundles' contents match §5's plan table.
- Every public bundle has a price and billing interval.
- Migration + rollback; full build + tests green.

**Evidence.** _(empty)_

---

### T-04 · Bridge `platform_plans.features` → `brand_module` (plan grants entitlement)

| Field | Value |
|---|---|
| **Phase** | P1 — Entitlements |
| **Gap** | E9 |
| **Status** | `Todo` |
| **Dependencies** | T-02, T-03 |

**Description.** Two disconnected entitlement systems exist:
`finance_royalty.platform_plans.features` (a `jsonb` column no C# code reads) drives the
franchise-billing engine, while `identity_access.brand_module` drives actual entitlement.
Subscribing to a plan therefore grants nothing. §5 specifies
`entitlement = plan.features ∪ purchased add-ons`. Build the resolver that expands a plan/bundle
into `brand_module` rows with `source='bundle'`, leaving `source='manual'` rows (add-ons) untouched.

**Acceptance criteria.**
- Changing a brand's plan re-expands `source='bundle'` rows and never deletes `source='manual'` rows.
- Downgrading removes only bundle-granted modules the new plan lacks.
- Tests cover: upgrade, downgrade, add-on survival across a plan change, idempotent re-apply.
- **Carried over from T-01:** once the bridge exists, backfill every brand's entitlements from its
  current plan, verify no brand loses a module it uses today (the live `LG-MAIN` brand is currently
  missing 7 of 26 active non-core modules), then flip `Entitlement:Enforced` to `true`.
- Full build + tests green.

**Evidence.** _(empty)_

---

### T-05 · `402 feature_not_in_plan` with upgrade link

| Field | Value |
|---|---|
| **Phase** | P1 — Entitlements |
| **Gap** | E6 |
| **Status** | `Todo` |
| **Dependencies** | T-01, T-02 |

**Description.** §5 requires the API to return `402 feature_not_in_plan` with an upgrade link when a
brand touches a feature it does not own. Today the entitlement filter strips the permission at token
mint, so the caller sees an indistinguishable `403`. There are zero occurrences of `402` /
`PaymentRequired` in the backend.

**Acceptance criteria.**
- A request for an un-entitled (but otherwise permitted) feature returns `402` with a body carrying
  the feature key and an upgrade URL; a genuinely unauthorized request still returns `403`.
- The distinction is tested both ways.
- At least one client renders the upgrade prompt rather than a generic error.
- Full build + tests green.

**Evidence.** _(empty)_

---

### T-06 · Roles follow features

| Field | Value |
|---|---|
| **Phase** | P1 — Entitlements |
| **Gap** | E12 |
| **Status** | `Todo` |
| **Dependencies** | T-02 |

**Description.** §5: "Buy Fleet → Rider appears. Buy Processing → Facility Staff appears."
`RolesTab.tsx` greys cells by entitlement and `GetAccessRoles` filters by *vertical*, but no
feature→role mapping exists — a brand without the fleet module still sees and can grant the Rider
role.

**Acceptance criteria.**
- A declarative feature→role mapping; roles whose required feature is un-entitled are absent from
  `GetAccessRoles` and rejected by the grant-membership command (not merely hidden in the UI).
- Tests assert both the hiding and the server-side rejection.
- Full build + tests green.

**Evidence.** _(empty)_

---

### T-07 · Backend-owned terminology packs for all four clients

| Field | Value |
|---|---|
| **Phase** | P1 — Entitlements |
| **Gap** | V5 |
| **Status** | `Blocked` |
| **Dependencies** | none — blocked on a decision, not on other tasks |

**Description.** §12 names terminology leakage as a top risk: "laundry words in a courier UI kills
credibility — the terminology pack must cover *every* user-facing string." Today the only
terminology mapping is `admin-web/src/lib/verticalTerms.ts`, covering three things (on-site location
noun, on-site `user_type`, a designation placeholder) in the admin console alone. `pos-web`,
`customer-mobile` and `rider-mobile` have none, and the i18n bundles are laundry-worded.

**Blocked reason (new — OQ-8).** The *mechanism* is unblocked and I know exactly where it belongs
(below). What is missing is the **vocabulary**: nobody has written down what a salon or a logistics
brand actually calls things. `PLATFORM_STRATEGY.md` §3 gives one illustrative triple
("garment" ⇄ "parcel" ⇄ "meal box") for verticals that are not even the ones we ship, and
`verticalTerms.ts` covers three nouns. The acceptance criterion "a test that fails if a shipped
vertical is missing a required string" cannot be written until someone decides what the required set
*is* — and writing that vocabulary myself would be inventing the product's voice in three industries.

Starting a partial migration across five codebases and stopping halfway is strictly worse than not
starting: it leaves two parallel terminology sources (the new pack and the existing
`verticalTerms.ts` + laundry-worded i18n bundles), which is the exact drift §12 warns about.

**Design work done, so this is ready to build the moment the vocabulary exists.** The seam already
exists and should be extended rather than duplicated: `GET /api/v1/fulfillment-config`
(`operations.Application/Fulfillment/Queries/GetFulfillmentConfigQuery.cs`) already serves
backend-driven, client-consumable descriptors built live from the registered `IFulfillmentStrategy`
set, and all four clients already consume it (`useFulfillmentConfig` in both mobile apps, POS, admin).
It is **mode**-keyed and currently carries stage labels only. Terminology is **vertical**-keyed, so
the change is a vertical-keyed pack alongside it, with `verticalTerms.ts` demoted to a thin client.

**What I need from you.** For each shipped vertical (`laundry`, `salon`, `logistics` — and whatever
OQ-1 settles), the user-facing nouns: the item/unit word (garment / service / parcel), its plural,
the on-site location noun (Warehouse / Studio / Hub — these three already exist), the booking noun
(order / appointment / shipment), and the customer-facing action verb. That list is short; it just
has to come from someone who owns the product's voice.

**Acceptance criteria (once unblocked).**
- A per-vertical terminology pack served by the backend as the single source of truth, hung off the
  existing fulfillment-config seam.
- All four clients consume it; `verticalTerms.ts` becomes a thin client of it rather than a parallel map.
- A test enumerates the user-facing strings a vertical must override and fails if a shipped vertical
  is missing one.
- Full build + tests green across backend and all four clients.

**Evidence.** _(empty)_

---

### T-08 · `brand_domains` schema

| Field | Value |
|---|---|
| **Phase** | P2 — Custom domains |
| **Gap** | W2 |
| **Status** | `Done` |
| **Dependencies** | none |

**Description.** §4.2 item 1 specifies a new table `brand_domains` (`brand_id`, `domain`,
`verification_txt`, `verified_at`, `ssl_status`, `is_primary`). Nothing exists: no table, no entity,
no `domain` column on `brands`. This is the foundation of the entire T2 white-label tier.

**Acceptance criteria.**
- Table with the §4.2 columns, brand-scoped RLS matching the house policy shape, unique constraint
  on `domain`, at most one `is_primary` per brand.
- EF entity + configuration + `DbContext` registration.
- Migration + rollback under `db/migrations/`.
- Full build + tests green.

**Ordering note.** Taken ahead of T-07 (the other dependency-free task). T-07's acceptance criteria
require enumerating the user-facing vocabulary each vertical must override, and the salon/logistics
wording does not exist in any strategy document — writing it would be inventing product scope. T-08
is specified column-by-column in §4.2 and unblocks a whole phase (T-09, T-10, T-11), so it went first.

**Evidence.**

Migration applied, rolled back and re-applied against the live `laundry_ghar_db`, per
`db/migrations/README.md` rule 1 (`up` → `down` → `up` must be clean):
```
$ db/tools/migrate.sh up      applying 0002_brand_domains ...   applied 1 migration(s)
$ db/tools/migrate.sh down    rolling back 0002_brand_domains ... rolled back 1 migration(s)
$ psql -tAc "select count(*) from information_schema.tables
             where table_schema='tenancy_org' and table_name='brand_domains'"
0                                              <- rollback is complete, no residue
$ db/tools/migrate.sh up      applying 0002_brand_domains ...   applied 1 migration(s)
$ db/tools/migrate.sh status  0001 applied · 0002 applied · pending: 0
$ db/tools/migrate.sh verify  verify OK        <- no checksum drift
```

Resulting schema (`\d tenancy_org.brand_domains`) carries every §4.2 column — `brand_id`, `domain`,
`verification_txt`, `verified_at`, `ssl_status`, `is_primary` — plus:
```
"brand_domains_domain_key"      UNIQUE CONSTRAINT, btree (domain)          -- citext: DNS-correct
"idx_brand_domains_one_primary" UNIQUE, btree (brand_id) WHERE is_primary
"idx_brand_domains_verified"    btree (domain) WHERE verified_at IS NOT NULL
CHECK (ssl_status IN ('pending','active','failed','expired'))
FOREIGN KEY (brand_id) REFERENCES tenancy_org.brands(id) ON DELETE CASCADE
POLICY rls_brand USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()))
TRIGGER trg_brand_domains_set_updated_at BEFORE UPDATE ... kernel.set_updated_at()
```
The policy is byte-identical in shape to `tenancy_org.stores` / `franchises` / `territories`.

Full test suite — 350 passed, up from 344 (+6):
```
$ dotnet build laundryghar.slnx      64 Warning(s)   0 Error(s)
$ dotnet test  laundryghar.slnx --no-build
Passed! - Failed: 0, Passed:  58, Total:  58 - core.Tests.dll
Passed! - Failed: 0, Passed: 208, Total: 208 - operations.Tests.dll
Passed! - Failed: 0, Passed:  84, Total:  84 - operations.IntegrationTests.dll
```

**Mutation check.** Two constraints were deliberately weakened in the migration — `domain CITEXT`
→ `VARCHAR(253)`, and `CREATE UNIQUE INDEX idx_brand_domains_one_primary` → non-unique — and the
suite re-run:
```
Failed! - Failed: 2, Passed: 4, Total: 6
```
Exactly the two tests that assert those properties failed. The file was then restored byte-for-byte
(`diff` clean) and `migrate.sh verify` re-confirmed **verify OK**, so the applied migration's
recorded checksum is intact.

**Warning accounting.** 64 warnings, none from any file created or edited in this task — verified by
grepping a full `--no-incremental` build for `BrandDomain*.cs`, `BrandDomainTests.cs`,
`RbacRlsFixture.cs`, `RepoPaths.cs`, `LaundryGharDbContext.cs`, `ICoreDbContext.cs`,
`CoreDbContext.cs` (no matches). The pre-existing inventory is NU1902/NU1903 (vulnerable
`MessagePack`, `Microsoft.OpenApi`, `SSH.NET` transitives), NU1510, IL2026/IL3050 (trim/AOT),
`xUnit2031` in `ImportFileParserTests.cs`, and one `CS8604` in `ExceptionHandler.cs`.

**Files changed.**
- `db/migrations/0002_brand_domains.up.sql` / `.down.sql` (new) — the table, its indexes, the
  brand-scoped RLS policy, grants, the `updated_at` trigger, and column comments; the `.down.sql`
  warns that rollback discards verified provider domains and gives the query to check first.
- `laundryghar.SharedDataModel/Entities/TenancyOrg/BrandDomain.cs` (new) — entity plus a
  `BrandDomainSslStatus` vocabulary class mirroring the DB CHECK (ADR-005), and an `IsResolvable`
  computed property so the "verified_at must be set" rule has one name in code.
- `laundryghar.SharedDataModel/Persistence/Configurations/TenancyOrg/BrandDomainConfiguration.cs` (new).
- `LaundryGharDbContext.cs`, `ICoreDbContext.cs`, `CoreDbContext.cs` — `BrandDomains` DbSet.
- `tests/operations.IntegrationTests/Rbac/BrandDomainTests.cs` (new) — 6 tests: global +
  case-insensitive domain uniqueness (the tenant-hijack guard), one primary per brand with other
  brands unaffected, `ssl_status` vocabulary closed, unverified domains excluded from the resolution
  predicate, RLS isolation for reads **and** writes (42501 on planting a domain on another brand),
  and the `updated_at` trigger.
- `tests/operations.IntegrationTests/Rbac/RbacRlsFixture.cs` — applies migration 0002 verbatim and
  adds the production `kernel.set_updated_at()` the trigger needs.
- `tests/operations.IntegrationTests/RepoPaths.cs` — a `Migration()` resolver alongside `Patch()`/`Script()`.

**Not in scope, by design:** nothing yet *reads* this table. Host resolution is T-09, TXT
verification is T-10. Adding a domain today has no runtime effect, which is the safe order.

---

### T-09 · Host-header brand resolution middleware

| Field | Value |
|---|---|
| **Phase** | P2 — Custom domains |
| **Gap** | W3 |
| **Status** | `Done` |
| **Dependencies** | T-08 |

**Description.** §4.2 item 4: every request's `Host` header resolves via `brand_domains` and sets
`app.current_brand_id`; RLS does the rest, so one deployment serves N domains. `IBrandResolver`
currently resolves `X-Brand-Id` header → `?brandCode=` → default `LG-MAIN`, and never looks at Host.

**Acceptance criteria.**
- Host lookup takes precedence, with the existing header/query/default order preserved as fallback.
- Only **verified** domains resolve.
- Resolution is cached per host and invalidated when a domain changes.
- Tests: verified host resolves; unverified host does not; unknown host falls through; existing
  header/query paths unchanged.
- Full build + tests green.

**The obstacle this task actually had to solve — the brands-RLS trap.** Host resolution runs on
ANONYMOUS requests as the non-superuser `app_user`, and `brand_domains` carries the house policy
`USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())`. Anonymously neither disjunct
holds — bypass is off, `current_brand_id()` is NULL, and `brand_id = NULL` is NULL — so the table
reads as **empty**. Confirmed against the live DB before writing any code:
```
$ psql -U app_user -c 'select count(*) from tenancy_org.brand_domains'   ->  0
$ psql -U app_user -c 'select count(*) from tenancy_org.brands'          ->  0
```
Two ways out. Adding the path to core.WebApi's pre-auth `bypass_rls` allow-list would grant a
**blanket** bypass over every tenant table for the rest of the request. Instead, migration 0003 adds
`kernel.resolve_brand_domain(text)` — SECURITY DEFINER with a pinned `search_path`, granted only to
`app_user`/`app_admin`, revoked from PUBLIC — which grants exactly one capability: "given a hostname,
tell me its brand id". Same pattern and same rationale as the existing `kernel.user_perm_version`.
RLS still blocks every other read of the table.

**A second trap, found by mutation testing.** `citext = text` resolves to the **text** operator and
compares case-SENSITIVELY:
```
SELECT 'ABC'::citext = 'abc'::text;   ->  f
SELECT 'ABC'::citext = 'abc'::citext; ->  t
```
The first version of the case-insensitivity test passed even with the `::citext` cast removed,
because the resolver lowercases the incoming host in C# and the test only ever *stored* lowercase
domains. The cast actually protects the **stored** value's casing: the column is citext so uniqueness
ignores case, but a row keeps the spelling it was inserted with, so a provider registering
`TheirBrand.example` would have become unreachable. The test now stores a mixed-case domain and
resolves the lowercased host, which pins it — and the migration comment was corrected, since it had
described the wrong reason.

**Evidence.**

Migration 0003 up → down → up, and functionally verified as `app_user` with **no session GUCs at
all** (the exact anonymous situation):
```
$ db/tools/migrate.sh up / down / up      applied · rolled back · applied
$ db/tools/migrate.sh verify              verify OK
$ psql -U app_user -tAc "select
    kernel.resolve_brand_domain('demo-verified.example'),      -- 5b375161-…-54848606aa2f
    kernel.resolve_brand_domain('DEMO-VERIFIED.EXAMPLE'),      -- 5b375161-…-54848606aa2f  (citext)
    kernel.resolve_brand_domain('demo-unverified.example'),    -- NULL   (verified-only rule)
    kernel.resolve_brand_domain('nobody.example'),             -- NULL   (unknown host)
    (select count(*) from tenancy_org.brand_domains)"          --    0   (RLS still blocks direct reads)
```
The two probe rows were deleted afterwards; `brand_domains` is back to 0 rows in the dev DB.

Full test suite — 355 passed, up from 350 (+5):
```
$ dotnet build laundryghar.slnx      66 Warning(s)   0 Error(s)
$ dotnet test  laundryghar.slnx --no-build
Passed! - Failed: 0, Passed:  58, Total:  58 - core.Tests.dll
Passed! - Failed: 0, Passed: 208, Total: 208 - operations.Tests.dll
Passed! - Failed: 0, Passed:  89, Total:  89 - operations.IntegrationTests.dll
$ db/tools/migrate.sh verify         verify OK      (checksums intact after the mutation runs)
```

**Mutation check — three regressions, each introduced separately:**

| Mutation | Result |
|---|---|
| A · drop the `::citext` cast (case-sensitivity) | `Failed: 1, Passed: 4` |
| B · drop `verified_at IS NOT NULL` (tenant hijack) | `Failed: 1, Passed: 4` |
| C · remove Host resolution from `BrandResolver` | `Failed: 3, Passed: 2` |

Each file was restored byte-for-byte (`diff` clean) and `migrate.sh verify` re-confirmed **verify OK**.

**Warning accounting.** 66 warnings; the delta from 64 is again NuGet restore surfacing pre-existing
advisories (the added `FrameworkReference` forces a re-restore), not new code. A full
`--no-incremental` build lists exactly 14 code warnings, all pre-existing and none in a file I
touched: `ExceptionHandler.cs` CS8604 ×1, `ImportFileParserTests.cs` xUnit2031 ×7,
`RazorpayLinkClient.cs` / `SettingsMailer.cs` / `WhatsAppOtpDispatcher.cs` IL2026+IL3050 ×6.

**Files changed.**
- `db/migrations/0003_resolve_brand_domain.up.sql` / `.down.sql` (new) — the SECURITY DEFINER lookup.
  Two behaviours are documented because they are load-bearing: unverified domains never resolve, and
  a **suspended** brand still does (§9 login-only mode — the owner must be able to reach their own
  domain to pay the invoice that reinstates them).
- `core.Infrastructure/Services/BrandResolver.cs` — Host lookup ahead of the existing
  `X-Brand-Id` → `?brandCode=` → default chain, with an `IMemoryCache` layer (30s TTL, **caching
  misses as well as hits**, since the common case is one of our own hostnames and every such request
  would otherwise pay a round-trip). `localhost` and bare IPs skip the lookup entirely. A lookup
  failure logs and falls through rather than failing the request, so the worst case is exactly the
  pre-T-09 behaviour.
- `tests/operations.IntegrationTests/Rbac/BrandResolverHostTests.cs` (new) — 5 tests: verified host
  resolves (incoming casing, trailing root dot, and stored mixed case); unverified host falls
  through; unknown host preserves the header/query/localhost/IP chain; a verified host outranks a
  conflicting `X-Brand-Id`; hits and misses are both cached (proven by mutating the DB behind a
  warm cache).
- `tests/operations.IntegrationTests/Rbac/RbacEfFixture.cs` — applies migrations 0002 + 0003 verbatim,
  adds `kernel.set_updated_at()`, and `SeedBrandAsync` now returns the brand code so the
  `?brandCode=` fallback can be exercised.
- `tests/operations.IntegrationTests/operations.IntegrationTests.csproj` — `Microsoft.AspNetCore.App`
  framework reference for `DefaultHttpContext`.

**Design decision worth flagging.** Host resolution is deliberately confined to the **anonymous**
path (`IBrandResolver`). For an authenticated request the brand comes from the JWT, and Host must
never override it — otherwise a signed-in user who opened another provider's domain would be
silently switched into that tenant. §4.2's "sets `app.current_brand_id`" is satisfied for anonymous
traffic here; the authenticated path already derives the brand correctly from the token.

**Pre-existing issue found, not fixed (outside this task's scope).** `/api/v1/public/*`
(`PublicEngagement.cs`: onboarding-slides, app-config, banners) is anonymous and is **not** on the
pre-auth bypass allow-list, so its `?brandCode=` / default-brand lookup against `tenancy_org.brands`
also reads zero rows and returns `{"error":"Brand not found."}` unless the caller supplies
`X-Brand-Id` (which short-circuits before any DB access). The mobile clients do send a brand code
rather than an id, so this path looks broken today. T-09 does not change it either way — the new
Host branch simply falls through for unknown hosts. **Recommend filing this separately**; fixing it
means either extending the allow-list or giving brand-code lookup the same SECURITY DEFINER
treatment, and it is a behaviour change on live endpoints that I should not make inside this task.

---

### T-10 · Domain ownership verification (TXT challenge)

| Field | Value |
|---|---|
| **Phase** | P2 — Custom domains |
| **Gap** | W4 |
| **Status** | `Done` |
| **Dependencies** | T-08 |

**Description.** §4.2 item 2: provider adds a CNAME to our edge and verifies ownership via a TXT
record. No DNS-verification code exists anywhere.

**Acceptance criteria.** Challenge issuance, a verify endpoint that performs the DNS lookup and
stamps `verified_at`, admin UI to add a domain and see its state, tests with a faked resolver
covering success / wrong-value / missing-record, full build + tests green.

---

#### ⚠ A production-blocking bug found while verifying this, and fixed

Driving the flow against a running core host, the very first mutation failed with
`422 "No email on file to verify against."` — from `StepUpVerifyHandler`, not from anything I wrote.

**Cause.** The JWT bearer handler runs with inbound claim mapping ON (the ASP.NET default), which
rewrites registered claims to their long WS-Fed URIs: `sub` → `ClaimTypes.NameIdentifier`,
`email` → `ClaimTypes.Email`. `HttpContextCurrentUser.Email` read the *short* name
(`Claim("email")`) while `UserId` read the *mapped* one — so `Email` was **null for every caller**.
The token genuinely carries `email`; it just no longer had that name by the time it was read.

**Blast radius.** Step-up gates every `high`/`critical` permission (docs/rbac.md §8). With
`ICurrentUser.Email` null, no user could ever complete step-up by email, making **`payment.refund`,
`pricing.publish`, `user.create`, `royalty.override`, `role.manage` and `brands.update` all
unreachable through the API.** This is almost certainly the item my notes recorded as "step-up fix
pending core restart + verify" — it was never actually verified, and it was still broken.

**Fix.** Two lines in `laundryghar.Utilities/Services/HttpContextCurrentUser.cs`: read the raw claim
first, fall back to the mapped one. Behaviour is unchanged wherever it already worked (raw wins),
and it cannot invent a value where the claim is genuinely absent.

**Why I fixed it inside this task rather than filing it:** T-10's own acceptance criteria are
unreachable without it — a domain cannot be added at all when every `brands.update` call is refused.
It is one small isolated file if you would rather revert it and handle it separately.

Proven by mutation: reverting the fallback fails 2 of the 5 new tests.

---

**Evidence.**

**1 · Live end-to-end against a running core host** (core.WebApi on :5056, real Postgres, real
DnsClient — not mocks). Logged in as the super-admin, brand `LG-MAIN`:

```
GET  …/domains                          200  {"data":[]}
POST …/domains  (no step-up)            403  {"step_up_required":["brands.update"]}   <- guard armed
POST /auth/otp/send  purpose=sensitive_action        200
POST /auth/step-up/verify  code=123456               200  (elevated token minted — after the fix)

POST …/domains  {"domain":"https://Shop.AcmeLaundry.Example:8443/pricing","isPrimary":true}
  -> 200 {
       "domain": "shop.acmelaundry.example",          <- URL, port, path and case all normalised
       "verified": false, "verifiedAt": null, "sslStatus": "pending", "isPrimary": true,
       "verificationName":  "_lg-verify.shop.acmelaundry.example",
       "verificationValue": "lg-verify=ac348ed4f5f57e951967754f0162354c120c90753127eead9de37e0d66da8b4a",
       "cnameTarget": "edge.localhost" }

psql: kernel.resolve_brand_domain('shop.acmelaundry.example')  ->  NULL   <- unverified: does NOT resolve

POST …/domains/{id}/verify              200  {"verified":false,"status":"record_not_found",
                                              "message":"No TXT record found at _lg-verify.shop.acmelaundry.example…"}
POST …/domains {"domain":"SHOP.ACMELAUNDRY.EXAMPLE"}  422  "This domain is already registered."
POST …/domains {"domain":"localhost"}                 422  "Enter a valid public domain name…"
GET  …/domains                          200  shop.acmelaundry.example  verified=False primary=True ssl=pending
```

The `record_not_found` result is the real DNS client working: it genuinely queried
`_lg-verify.shop.acmelaundry.example`, got NXDOMAIN, and correctly classified that as
**`record_not_found`** rather than `lookup_failed` — the exact distinction the handler exists to make.

**2 · Browser verification of the admin UI** (Chrome, admin-web on :5173, logged in as Super Admin).
`admin-web/CLAUDE.md` asks for Reticle, but **Reticle cannot run against this app**: its Vite plugin
is commented out in `admin-web/vite.config.ts` — *"disabled: causes an infinite reload loop, see
reticle_feedback filed 2026-08-15"* — so the page never instruments and no session exists. Verified
with Chrome automation instead, and saying so rather than skipping it silently.

Every path was driven in the real browser:
- **Settings → BRANDING → Custom domains** renders, listing the domain with a `Primary` badge, an
  `Awaiting verification` badge, and both DNS records (CNAME → `edge.localhost`, TXT →
  `_lg-verify.…` + the challenge value) with per-record copy buttons.
- **Verify** → the shared `StepUpDialog` appeared automatically (the existing step-up infrastructure
  picked up the new endpoint's `step_up_required` response with zero extra wiring) → chose "Email
  me" → entered the dev master OTP → the verification ran and rendered
  *"No TXT record found at _lg-verify.shop.acmelaundry.example. DNS changes can take a while to
  propagate."* in the neutral amber tone, not as an error.
- **Add domain** with `https://Books.AcmeLaundry.Example/store` → normalised to
  `books.acmelaundry.example`, appended to the list unverified, input cleared, and correctly NOT
  marked primary (checkbox unticked).
- **Delete** → confirm dialog showed the unverified-branch copy (*"This address is not verified yet,
  so nothing is currently being served from it."*) → removed, list refreshed.
- **Console: zero application errors** (the only message was from the Chrome extension itself).

Test data and both dev servers were cleaned up afterwards; `brand_domains` is back to 0 rows.

**3 · Automated tests — 400 backend passed, up from 355 (+45):**
```
$ dotnet build laundryghar.slnx      67 Warning(s)   0 Error(s)
$ dotnet test  laundryghar.slnx --no-build
Passed! - Failed: 0, Passed:  83, Total:  83 - core.Tests.dll              (+25)
Passed! - Failed: 0, Passed: 213, Total: 213 - operations.Tests.dll        (+5)
Passed! - Failed: 0, Passed: 104, Total: 104 - operations.IntegrationTests (+15)
```
Frontends unchanged from baseline: admin-web tsc clean / 12 pre-existing warnings · pos-web tsc
clean / 2 · customer-mobile 164 · rider-mobile 85. **649 tests green overall.**

**4 · Mutation check — five regressions, each introduced separately:**

| Mutation | Result |
|---|---|
| verify accepts ANY TXT record (challenge value unchecked) | `Failed: 1, Passed: 14` |
| verify drops the `BrandId` scope (cross-tenant verify) | `Failed: 1, Passed: 14` |
| add skips hostname normalisation | `Failed: 2, Passed: 13` |
| add skips the duplicate check | `Failed: 1, Passed: 14` |
| the claim-mapping fallback reverted (the step-up bug) | `Failed: 2, Passed: 3` |

Every file restored byte-for-byte (`diff` clean).

**5 · A second citext trap, caught before it shipped.** The duplicate check was first written as
`d.Domain == domain`. EF sends the comparand as a **text** parameter, and `citext = text` resolves to
the case-SENSITIVE text operator — so a row stored as `TheirBrand.example` would not have been seen
as taken, and the request would have fallen through to a raw 23505 instead of a clear message. Now
`d.Domain.ToLower() == domain`, with a test that inserts a mixed-case row directly and asserts a
different brand still cannot claim it.

**Files changed.**
- `core.Application/Common/Interfaces/IDnsTxtLookup.cs` (new) — with `DnsLookupException`, so
  "the lookup failed" is a different thing from "there is no record".
- `core.Application/Identity/TenancyOrg/BrandDomains/` (new) — `BrandDomainChallenge` (normalisation,
  plausibility, 256-bit CSPRNG challenge under a private `_lg-verify` label rather than the apex,
  where SPF/DMARC/other vendors' records live), `BrandDomainSettings`, DTOs, and the
  add / verify / delete / list handlers.
- `core.Infrastructure/Dns/DnsTxtLookup.cs` (new) — DnsClient.NET, caching deliberately OFF (a stale
  negative is worst exactly when a provider has just added the record and is clicking Verify), and
  NXDOMAIN classified as a definitive empty answer rather than a fault.
- `core.WebApi/Endpoints/Identity/AdminBrandDomains.cs` (new) — gated on `brands.read`/`brands.update`
  rather than the platform-side `saas.*`, because §6 Law 1 puts branding and domain with the Owner;
  `brands.update` is risk `high`, so step-up applies to every mutation for free.
- `core.WebApi/Program.cs`, `appsettings*.json` — `BrandDomains:CnameTarget` (per-environment).
- `core.Infrastructure/core.Infrastructure.csproj` — `DnsClient` 1.8.0 (no advisories on restore;
  `System.Net.Dns` resolves only A/AAAA, so a real DNS client is unavoidable).
- `admin-web/`: `api/brandDomains.ts`, `hooks/useBrandDomains.ts`,
  `pages/settings/CustomDomainsPanel.tsx` (new), plus `SettingsPage.tsx` and `types/api.ts`.
  The DNS instruction stays on screen for as long as a domain is unverified — that is the work the
  provider still has to do, and hiding it behind a one-time modal is how people get stuck.
- `laundryghar.Utilities/Services/HttpContextCurrentUser.cs` — the step-up claim-mapping fix above.
- `tests/`: `BrandDomainChallengeTests.cs` (25), `BrandDomainVerificationTests.cs` (15),
  `CurrentUserClaimMappingTests.cs` (5).

**Not done here:** `ssl_status` is still always `pending` — nothing writes it until T-12, which is
blocked on OQ-6 (Let's Encrypt vs Cloudflare-for-SaaS). The UI shows the value rather than pretending
a certificate exists.

---

### T-11 · Per-provider sender identity

| Field | Value |
|---|---|
| **Phase** | P2 — Custom domains |
| **Gap** | W6 |
| **Status** | `Done` — **already satisfied; no code written** |
| **Dependencies** | T-10 |

**Description.** §4.2 item 5. My gap analysis recorded this as Partial, claiming "no verified-sender-
domain concept and **no per-brand SMS sender ID**".

**Outcome: the second half of that claim was simply wrong, and the task is not a gap.** I checked
before building, and all three channels are already per-provider:

| Channel | Evidence |
|---|---|
| Email | `SettingsMailer.LoadAsync(brandId)` reads a **brand-scoped** `email/smtp` row from `kernel.system_settings` — the provider's own SMTP host, credentials, from-address and from-name. |
| SMS | `UpdateSmsHandler` resolves the brand via `SettingsStore.ResolveBrandIdAsync(_user, _db, ct)` and writes a brand-scoped `sms/provider` row carrying **`SenderId`** and `DltTemplateId`. `RoutingChannelSender.cs:141` reads `sms.SenderId` when sending. |
| WhatsApp | brand-scoped `whatsapp/cloud` settings row, plus `tenancy_org.brands.whatsapp_number`. |

Confirmed against the live DB — every one of the three rows is brand-scoped, not platform-wide:
```
$ psql -c "select category, setting_key, brand_id is null as platform_wide
           from kernel.system_settings where category in ('email','sms','whatsapp')"
 category | setting_key | platform_wide
----------+-------------+---------------
 email    | smtp        | f
 sms      | provider    | f
 whatsapp | cloud       | f
```

**The remaining sliver — verified sender domain (SPF/DKIM) — is not ours to verify.** Each provider
supplies their own SMTP credentials and from-address, so DKIM signing and SPF alignment happen on
their mail infrastructure, not ours. It would only become a real gap if we ever sent on their behalf
from **our** infrastructure, which is not the architecture that exists.

**Building anything here would have been inventing work.** `docs/GAP_ANALYSIS.md` W6 has been
corrected from Partial to Done with the same evidence, and P2-4 withdrawn from the phase plan.

**Evidence.** The table and query above. No files changed; the 649-test suite is untouched by this task.

---

### T-12 · SSL automation + domain health checks

| Field | Value |
|---|---|
| **Phase** | P2 — Custom domains |
| **Gap** | W5 |
| **Status** | `Blocked` |
| **Dependencies** | T-09 |

**Blocked reason.** **OQ-6** — §4.2 item 3 offers "Let's Encrypt / Cloudflare-for-SaaS" without
choosing. The two paths differ in infrastructure, cost, and whether `ssl_status` is polled or
webhook-driven. §12 also calls for automated SSL/domain health checks "from day one". This is an
infrastructure and cost decision, not a code decision.

**Evidence.** _(empty)_

---

### T-13 · Vertical template as data

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | V4 |
| **Status** | `Blocked` |
| **Dependencies** | T-02, T-07 |

**Description.** §3: "A **vertical template** = mode + terminology pack + preset catalog structure +
default feature set." No such object exists. The salon vertical was landed as a bespoke SQL patch
(`db/patches/phase4_salon_pack.sql`), so adding a vertical today means hand-writing another one.

**Blocked reason.** Depends on T-02 (blocked on OQ-3) for the "default feature set" half, and on
**OQ-1** for which templates are in scope at all.

**Evidence.** _(empty)_

---

### T-14 · Mode 3 — recurring schedule

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | M3 |
| **Status** | `Blocked` |
| **Dependencies** | T-13 |

**Description.** §3 Mode 3 (tiffin / milk / water cans): a repeat-delivery calendar. The canonical
`orders_fulfillment_mode_check` allows only `process_deliver`, `appointment`, `point_to_point`;
there is no `recurring` strategy under `operations.Application/Fulfillment/`.
`commerce.customer_subscriptions` is a billing subscription, not a delivery calendar.

**Blocked reason.** **OQ-2** — undesigned. Does a recurring booking materialise one order per
occurrence, or one long-lived order with occurrence rows? That decides whether this is a new
`fulfillment_mode` on the existing aggregate or a new aggregate entirely, which in turn decides
whether the `orders` range-partitioning still works. This is precisely the class of decision
`MULTI_VERTICAL_BLUEPRINT.md` §8 flags as a blocking gate.

**Evidence.** _(empty)_

---

### T-15 · Launch templates per the resolved vertical decision

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | V6 |
| **Status** | `Blocked` |
| **Dependencies** | T-13, T-14 |

**Blocked reason.** **OQ-1** — the strategy names Laundry / Courier / Tiffin; the canonical SQL
allows `laundry` / `salon` / `logistics`. Whether `salon` is first-class and whether `tiffin` joins
or replaces determines a widening of three CHECK constraints.

**Evidence.** _(empty)_

---

### T-16 · Provider self-signup

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | P1 (§2.6) |
| **Status** | `Todo` |
| **Dependencies** | T-13 |

**Description.** §9: "Signup → business details + phone OTP (GSTIN optional) → pick vertical
template + plan (trial N days)". `core.Application/Identity/Onboarding/` is **franchise** onboarding
*inside* an existing brand; a brand is created only by a platform admin via `AdminBrands.cs`. The
entry point of the whole funnel does not exist.

**Acceptance criteria.** Anonymous signup creating a brand in `trialing` state with an Owner user,
phone-OTP verified, optional GSTIN; abuse-resistant (rate limited, no tenant leakage — note the
brands-RLS trap that 422s new anonymous auth endpoints); tests for happy path, duplicate, and rate
limit; full build + tests green.

**Evidence.** _(empty)_

---

### T-17 · Provider onboarding wizard

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | P2 (§2.6) |
| **Status** | `Todo` |
| **Dependencies** | T-16, T-09 |

**Description.** §9 onboarding order: sign up → template → plan/trial → wizard (locations, catalog
seed, staff invites, gateway link) → live on sub-domain.

**Acceptance criteria.** Resumable wizard persisting progress; catalog seeded from the chosen
template; brand reachable on its sub-domain at the end; tests for resume and for each step's
validation; full build + tests green.

**Evidence.** _(empty)_

---

### T-18 · Brand suspend / reactivate + login-only mode

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | P5, P6 (§2.6) |
| **Status** | `Todo` |
| **Dependencies** | T-04 |

**Description.** §9: "Suspension = **login-only mode** (owner can see invoices and pay; operations
frozen) — never silent data loss." `brands.status` has a `suspended` value that **no auth or request
path reads**, so a suspended brand keeps operating normally. `SubscriptionBillingService` suspends
the *subscription* but nothing acts on it.

**Acceptance criteria.** A suspended brand permits only auth + billing routes and rejects operational
ones with a distinguishable code; the Owner can still view and pay invoices; reactivation on payment
restores full access; tests cover suspend → blocked, suspend → billing still reachable, reactivate →
restored; full build + tests green.

**Evidence.** _(empty)_

---

### T-19 · Per-provider rate limits

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | P8 (§2.6) |
| **Status** | `Todo` |
| **Dependencies** | T-04 |

**Description.** §7 lists per-provider rate limits as a platform control. `laundryghar.Gateway`
partitions its fixed-window limiter by **client IP** only.

**Acceptance criteria.** Brand-keyed partition with a per-plan quota, falling back to the IP
partition for unauthenticated traffic; tests assert one brand's burst does not throttle another;
full build + tests green.

**Evidence.** _(empty)_

---

### T-20 · Support impersonation with consent + audit

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | P9 (§2.6) |
| **Status** | `Todo` |
| **Dependencies** | T-18 |

**Description.** §7: "Support: impersonation with consent + full audit (Platform Support role,
read-first)." §8.1: the platform must not "change a company's branding, config, or data without
recorded consent." The only trace of impersonation is an unused enum member in `AuthMethod.cs`.

**Acceptance criteria.** Recorded, time-boxed provider consent; an impersonation token that is
read-only by default and carries both the real and impersonated actor; every impersonated action in
`audit_logs` with both identities; expiry and revocation; tests for consent-required, read-only
default, audit completeness, expiry; full build + tests green.

**Evidence.** _(empty)_

---

### T-21 · Provider cancellation — export, retention, deletion

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | P10 (§2.6) |
| **Status** | `Todo` |
| **Dependencies** | T-18 |

**Description.** §9: "Cancellation = export offered, wind-down retention, then deletion per DPDP."
§8.2: the company can "export their data at any time; take it with them if they leave." The DPDP
pipeline (`dpdp_erasure_pipeline.sql`, `CustomerErasureService`, `RetentionSweepService`) is
**customer**-level only; there is no brand-level wind-down.

**Acceptance criteria.** Owner-triggered full provider export; a retention window after
cancellation; deletion after it elapses; tests for export completeness, retention honoured, deletion
irreversible-and-audited; full build + tests green.

**Evidence.** _(empty)_

---

### T-22 · White-label Expo app factory

| Field | Value |
|---|---|
| **Phase** | P4 — White-label app |
| **Gap** | W7 |
| **Status** | `Blocked` |
| **Dependencies** | T-07, T-17 |

**Description.** §4 tier T3 + §11 P4. `customer-mobile/app.config.ts` hardcodes `name`, `slug`,
`bundleIdentifier`, `package`, and the brand colour; `rider-mobile` is the same shape.

**Blocked reason.** **OQ-7** — §4 calls T3 "one-time fee" but does not say who submits to the
stores. If we submit on the provider's behalf the factory needs developer-account credential
handling (which I will not build without an explicit decision); if the provider submits, it needs
only a config bundle and instructions. Very different builds.

**Evidence.** _(empty)_

---

### T-23 · Partner / public API

| Field | Value |
|---|---|
| **Phase** | P4 — White-label app |
| **Gap** | A1 |
| **Status** | `Todo` |
| **Dependencies** | T-02, T-19 |

**Description.** §11 P4 + the `api_access` entitlement in §5. No API-key table, no key middleware.
`PartnerAuth.cs` is RaaS partner-**user** OTP login; `oauth_authorization_server.sql` + `OAuth.cs`
serve the MCP/assistant flow. Neither is a machine API for providers.

**Acceptance criteria.** API key/secret issuance and revocation per brand; scoped machine tokens;
per-key rate limits and usage metering; gated on the `api_access` entitlement; tests for issuance,
scope enforcement, revocation, rate limiting, and entitlement gating; full build + tests green.

**Evidence.** _(empty)_

---

### T-24 · The 8-role preset + 10-permission-group surface

| Field | Value |
|---|---|
| **Phase** | Cross-cutting |
| **Gap** | R5, R6, R7, R8 |
| **Status** | `Blocked` |
| **Dependencies** | T-06 |

**Description.** §6 is the strategy's simplification layer: max 8 roles, 10 permission groups, plain
do/don't language, customize-by-subtraction-only, and Law 1 (money/branding/subscription = Owner
only). The live DB has **17** system roles and a per-module permission matrix across ~34 modules.
None of `owner`, `manager`, `staff`, `facility_staff` exists.

**Blocked reason.** **OQ-5** — mapping 17 shipped roles onto 8 presets is lossy:
`regional_manager`, `auditor`, `support`, `partner_admin`, `partner_operator`, the
`warehouse_supervisor`/`warehouse_staff` split, and the salon/logistics variants have no home in the
8. Are the 8 a **new naming that replaces** the 17 (a destructive grant migration across every
existing brand — exactly the lockout risk `MULTI_VERTICAL_BLUEPRINT.md` §8 Risk #6 warns about), or
a **display grouping over** them (additive, safe)? The blast radii are not comparable, and I will
not guess with live role grants.

**Evidence.** _(empty)_

---

## Summary

| Status | Count |
|---|---|
| `Done` | 5 |
| `In Progress` | 0 |
| `Blocked` | 9 |
| `Todo` | 10 |

**Every one of the 10 remaining `Todo` tasks is transitively blocked.** Each depends, directly or
through one hop, on a `Blocked` task — so there is no task I can pick up and finish today:

| Todo task | Blocked behind |
|---|---|
| T-04 Plan → entitlement bridge | T-02, T-03 |
| T-05 `402 feature_not_in_plan` | T-02 |
| T-06 Roles follow features | T-02 |
| T-16 Provider self-signup | T-13 (→ T-02, OQ-1) |
| T-17 Onboarding wizard | T-16 |
| T-18 Suspend / login-only mode | T-04 |
| T-19 Per-provider rate limits | T-04 |
| T-20 Support impersonation | T-18 |
| T-21 Provider cancellation | T-18 |
| T-23 Partner / public API | T-02, T-19 |

The whole remaining plan funnels through **T-02** (the feature catalog, OQ-3) and **OQ-1** (which
verticals). Answer those two and most of the board reopens at once.
| **Total** | **24** |

**Blocked tasks and what unblocks them:**

| Task | Blocked on |
|---|---|
| T-02 Feature catalog | OQ-3 — one table with `is_sellable`, or split `features` + `modules`? |
| T-03 Plan tiers | T-02, plus OQ-4 — real prices for Starter/Growth/Pro/Enterprise |
| T-12 SSL automation | OQ-6 — Let's Encrypt via our edge, or Cloudflare-for-SaaS? |
| T-13 Vertical templates | T-02, plus OQ-1 — which verticals are in scope? |
| T-14 Mode 3 recurring | OQ-2 — one order per occurrence, or one order with occurrences? |
| T-15 Launch templates | OQ-1 |
| T-22 App factory | OQ-7 — who submits to the app stores? |
| T-24 8-role presets | OQ-5 — replace the 17 roles, or group over them? |

Open questions are stated in full in `docs/GAP_ANALYSIS.md` §5.
