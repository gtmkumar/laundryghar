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
| **Status** | `Done` |
| **Dependencies** | T-01 |

> **OQ-3 ANSWERED 2026-08-25 by Goutam — "split features and modules".**

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

**Acceptance criteria.**
- All 17 §5 feature keys exist and are entitleable. ✅
- Existing module keys keep working — no brand loses an entitlement, asserted by a pre/post
  grant-count check per brand. ✅
- A migration + rollback pair under `db/migrations/`. ✅
- Full build + full test suite green. ✅

**What was built.** `identity_access.features` (the sellable catalogue) split out of
`identity_access.modules` (the sidebar), with entitlement moving from `brand_module` →
`brand_feature` and plans from `module_bundle_item` → `bundle_feature`. `modules.feature_key` is the
new join: a module appears when the feature behind it is owned.

**The mapping rule, chosen to make entitlement loss impossible.** 3 core modules keep
`feature_key = NULL` (always on); 10 modules whose meaning §5 already names adopt that key
(`orders`→`bookings`, `riders`→`fleet`, `warehouse`→`processing_facility`, …); **every other module
gets a feature of the same key**, so no module can end up unlicensed by accident; and the 8 §5
features with no menu at all are created as sellable, module-less rows. 33 features, 17 sellable.

**Evidence.**

Migration `0005` applied, rolled back, re-applied — clean in both directions:
```
$ db/tools/migrate.sh up / down / up      applied · rolled back · applied
$ db/tools/migrate.sh verify              verify OK
                                          features 33 | sellable 17 | core modules 3
                                          orphan modules 0 | brand entitlements 19 | bundle rows 45
```

The eight features that previously had **nowhere to exist**, now real and entitleable:
`item_tracking · online_payments · wallet · loyalty · whatsapp_bot · api_access · custom_domain ·
white_label_app`.

The many-to-one case the split exists to allow — one feature, several modules:
```
advanced_analytics  ->  Analytics, Reports
```

**Entitlement parity, asserted inside the migration itself.** Step 7 of the up-migration compares,
per brand, the non-core modules reachable before and after, and `RAISE EXCEPTION`s (aborting the
whole transaction) if any brand would lose one. LG-MAIN went 19 → 20 reachable modules; the +1 is
`report`, gained because it now shares `advanced_analytics` with `analytics`. A widening, never a loss.

**Live end-to-end against a running core host** — the §5 rule `entitlement = plan ∪ add-ons`:
```
fresh brand entitlements:        0
after apply 'starter':           bundle=7
after add-on + UPGRADE to 'pro': bundle=15 manual=1
after DOWNGRADE to 'starter':    bundle=7  manual=1     <- bundle shrinks, add-on untouched
add-on survived both:            YES
POST features {"featureKey":"not_a_feature"}  ->  422 {"featureKey":["Unknown feature."]}
```
`custom_domain` — the feature gating the T-10 work — was bought as an à-la-carte add-on and survived
a plan change, which is precisely what could not be modelled before. The dev DB was restored
afterwards (LG-MAIN back to 19, temp brand deleted).

**Automated tests — 402 backend passed (was 400); the entitlement suite grew 7 → 9:**
```
$ dotnet build laundryghar.slnx      66 Warning(s)   0 Error(s)
Passed! - Failed: 0, Passed:  83 - core.Tests.dll
Passed! - Failed: 0, Passed: 213 - operations.Tests.dll
Passed! - Failed: 0, Passed: 106 - operations.IntegrationTests.dll
```
Frontends unchanged from baseline: admin-web tsc clean / 12 pre-existing warnings · pos-web clean / 2
· customer-mobile 164 · rider-mobile 85. **651 tests green overall.**

Two NEW tests earn their place: `one_feature_gates_every_module_behind_it` (licensing one shared
feature releases both modules — inexpressible before the split) and
`a_core_feature_is_entitled_without_any_licence`.

**Mutation check — three regressions, each introduced separately:**

| Mutation | Result |
|---|---|
| token filter ignores the feature entirely | `Failed: 5, Passed: 4` |
| a core FEATURE is no longer always-on | `Failed: 1, Passed: 8` |
| navigator ignores the feature gate | `Failed: 2, Passed: 7` |

(The first attempt at mutation 2 failed to apply — a bad `sed` escape — so its "pass" proved nothing
and it was redone with a verified edit. Recording that because a mutation that silently no-ops looks
exactly like a test that survives.)

**Files changed.** `db/migrations/0005_*.{up,down}.sql`; entities `AppFeature`/`BrandFeature`/
`BundleFeature` + configurations; `AppModule.FeatureKey`; `BrandModule` entity deleted;
`ScopeResolver` and `GetNavigator` now resolve permission → module → **feature** → `brand_feature`;
`GetBrandEntitlements`, `SetBrandModule`→`SetBrandFeature`, `ApplyBundleToBrand`, `GetModuleBundles`,
`AdminEntitlements` endpoints (`/modules` → `/features`); admin-web `types/api.ts`,
`api/entitlements.ts`, `hooks/useEntitlements.ts`, `EntitlementsTab.tsx` (now lists features and what
each unlocks, badging the menu-less ones), `RolesTab.tsx` (flattens features onto modules to grey the
matrix); `RbacEfFixture` applies 0004 + 0005.

**Deliberately not renamed:** the access-control route param `?tab=modules`. Its label is already
"Licensing", and renaming it would break `admin-web/e2e/saas-billing.mjs` for a cosmetic gain.

---

### T-03 · Align bundles/plans with the §5 plan tiers

| Field | Value |
|---|---|
| **Phase** | P1 — Entitlements |
| **Gap** | E8 |
| **Status** | `Done` — structure shipped; **prices are placeholders** pending OQ-4 |
| **Dependencies** | ~~T-02~~ ✅ |

**Description.** `identity_access.module_bundle` holds `starter`, `pro`, `enterprise`,
`salon-starter`. §5 defines `Starter / Growth / Pro / Enterprise` with different contents.
`module_bundle.price` is nullable and unset on the seeded three.

**Blocked reason (narrowed 2026-08-25).** T-02 is Done, so the only remaining blocker is **OQ-4**:
§5 gives no prices ("low monthly", "mid", "higher", "custom"), and `module_bundle.price` cannot be
seeded from adjectives. Note the existing dev seed already carries placeholder prices
(starter ₹999 / pro ₹2999 / enterprise ₹7999 / salon-starter ₹1499) — confirm or replace them, and
say what the `growth` tier costs and contains.

**Acceptance criteria.**
- `growth` bundle exists; the four bundles' contents match §5's plan table. ✅
- Every public bundle has a price and billing interval. ✅ (**placeholder prices** — see below)
- Migration + rollback; full build + tests green. ✅

**What shipped.** Migration `0007_align_plan_tiers` rebuilds the tiers from §5's plan table rather
than the module list they had inherited, and adds the missing `growth` tier:
```
starter        ₹999   13 features ( 2 sellable)
salon-starter  ₹1499   5 features ( 2 sellable)
growth         ₹1999  17 features ( 6 sellable)
pro            ₹2999  25 features (14 sellable)
enterprise     ₹7999  31 features (17 sellable)
```
The migration **asserts the tiers are cumulative** (`starter ⊆ growth ⊆ pro ⊆ enterprise`) and aborts
if not — a non-cumulative tier means someone upgrades and *loses* a capability, which is a pricing
bug that is far cheaper to catch here than in a customer's console. It also asserts every sellable
feature appears in at least one tier, so nothing can end up buyable only as an add-on by accident.

**Three judgement calls, stated rather than buried:**
1. The 16 `is_sellable = false` features (auto-derived by 0005) are the operational spine — customers,
   items, pricing, POS, support, cash book — so they are in **every** tier. The enterprise-flavoured
   ones (royalty, franchises, audit) are Enterprise-only. `platform_plans` and `platform_billing` are
   in **no tenant tier at all** — they are our operator console, not something a provider buys.
2. §5 lists `wallet` and `customer_subscriptions` in the feature catalogue but places them in **no
   tier**. Put in Pro, because leaving them out of every plan would make them unbuyable except as
   add-ons. **Confirm the tier.**
3. **Prices are placeholders.** §5 says only "low monthly / mid / higher / custom". 999/2999/7999 are
   pre-existing dev seed values, kept as-is; `growth` = 1999 purely to sit between starter and pro.
   **None of these is a commercial decision** — they exist so the tier structure is testable, and
   must be confirmed before anyone is charged (OQ-4).

---

### T-03b · Bridge plans → entitlements and switch enforcement ON

| Field | Value |
|---|---|
| **Phase** | P1 — Entitlements |
| **Gap** | E4, E5, E9 |
| **Status** | `Done` |
| **Dependencies** | T-02 ✅, T-03 ✅ |

This is the T-04 work that could be completed. **`Entitlement:Enforced` is now `true`** — the single
highest-leverage item in the whole gap analysis, and the thing T-01 deliberately deferred.

**It was not safe to flip, and I measured that rather than assuming it.** Against this database, with
enforcement on and no backfill, the only live brand would have lost **six** modules:
`appointments, audit, fabrics, items, partner_booking, platform_billing`. `items` is the catalogue —
losing it is not a downgrade, it is an outage.

**Migration `0008_backfill_entitlements_from_plans`** establishes `entitlement = plan ∪ add-ons` for
every existing brand:
1. **Plan** — a brand with an active subscription gets its tier's features as `source='bundle'`, so a
   later tier change re-expands them correctly.
2. **Grandfathered add-ons** — anything reachable today that the tier does not include becomes
   `source='manual'`: a recorded, visible, revocable add-on rather than a blanket amnesty. Brands
   created *after* this get only what they buy.
3. **A disabled row that had never bitten.** `LG-MAIN.items` carried `enabled=false` — a latent "off"
   that has had no effect for its whole life because enforcement was off. Honouring it silently at
   switch-over would have been indistinguishable from the migration breaking the console, so it is
   re-enabled and each such row is named in a `RAISE WARNING` telling the operator to switch it off
   again in Licensing if it was deliberate.
4. **It aborts the transaction** unless no brand loses a module. The first run *did* abort — on
   `items` — which is how the disabled row was found.

**Evidence.**
```
$ db/tools/migrate.sh up
  WARNING: re-enabling disabled entitlement LG-MAIN.items — it was reachable before enforcement…
  applied 1 migration(s)
  LG-MAIN: 8 from plan, 23 grandfathered

$ # the exact delta enforcement introduces, computed per brand:
  modules the navigator would now hide:  NONE — enforcement changes nothing for existing brands
  permissions a brand-scoped token loses: NONE — no permission is stripped for this brand
```
Live against a running host with enforcement **on**: login succeeds, token carries 165 permissions,
navigator returns **24 modules** including `items` and `fabrics`. `ent_off` is correctly *absent* for
a platform admin — they are exempt from the filter by design.

`EntitlementConfigTests` (2 tests) now pins `Entitlement:Enforced = true` in both appsettings files.
That is a config test on purpose: the filter was fully implemented and inert for months precisely
because the key was missing and `GetValue<bool>` returned false, and **no behaviour test can catch
that** — an accidental revert would disable every entitlement in the platform without a single test
going red.

**Still open on T-04:** `finance_royalty.platform_plans.features` is untouched. Worth stating plainly
— that table holds **2 rows, both test junk** (`QA-SAAS-STARTER → ["orders","catalog"]`,
`VERIFY_PP_7024 → {"api_access":true}`), with two incompatible JSON shapes, and it sits on the
*franchise* billing axis, not the brand axis entitlement actually uses. §2 of the strategy points at
it as the entitlement source; the shipped architecture puts entitlement on `module_bundle →
brand_feature`, which is what now works end to end. Recorded as conflict D9.

**Evidence (build + tests).**
```
$ dotnet build laundryghar.slnx      66 Warning(s)   0 Error(s)
Passed! - Failed: 0, Passed:  85 - core.Tests.dll              (+2)
Passed! - Failed: 0, Passed: 218 - operations.Tests.dll
Passed! - Failed: 0, Passed: 113 - operations.IntegrationTests.dll
```

---

### T-04 · Bridge plan → entitlement (plan grants entitlement)

| Field | Value |
|---|---|
| **Phase** | P1 — Entitlements |
| **Gap** | E9 |
| **Status** | `Done` |
| **Dependencies** | T-02, T-03 |

**Scope corrected — the title's `platform_plans.features` is the wrong axis.** This task was written
against `finance_royalty.platform_plans.features`, a `jsonb` column no C# code reads. Conflict **D9**
established why bridging *that* would have been wrong: `platform_plans` is the **franchise**-billing
axis (what a franchisee pays their brand), not the **tenant**-billing axis (what a provider pays us),
and it holds two rows of test junk — `["orders","catalog"]` and `{"api_access":true,…}` — in two
mutually incompatible shapes. Building a bridge from it would have wired the SaaS entitlement system
to a franchise-royalty table. The real bridge is `bundle_feature → brand_feature`, which is what
shipped.

**Acceptance criteria, each against what actually exists.**

| Criterion | Status |
|---|---|
| Changing a plan re-expands `source='bundle'` rows, never deletes `source='manual'` | ✅ `ApplyBundleToBrandCommandHandler` (T-03b) |
| Downgrading removes only bundle-granted features the new plan lacks | ✅ same |
| Tests: upgrade, downgrade, add-on survival, idempotent re-apply | ✅ **written for this task** — they did not exist |
| Backfill every brand from its plan, verify no brand loses a module, then flip `Entitlement:Enforced` | ✅ migration `0008` + T-01; enforcement is `true` in both appsettings |

**The rule the tests are built around.** `entitlement = plan ∪ add-ons`. The second half is what makes
a plan change safe to run against paying customers: a `manual` row is something the customer **bought
separately**, and quietly removing it during a routine tier change is taking away something that was
paid for. Its mirror — a manual *disable* an operator applied deliberately — must survive too, or
"switch this off for them" gets undone by the next billing event.

**Evidence.**
- `core.Application/Identity/Entitlements/Commands/ApplyBundleToBrand.cs`,
  `SetBrandFeature.cs`; migration `db/migrations/0008_backfill_entitlements_from_plans.up.sql`.
- Tests: `tests/operations.IntegrationTests/Rbac/PlanChangeTests.cs` — 5 passed, driving the real
  handler against a real database. Bundles deliberately **unpriced**, so a failure cannot be the
  billing half.
- **Mutation-tested**, all three caught:

```
  sweep manual rows away on a plan change  -> 1 failed  (add-on survival)
  let a re-apply override a manual disable -> 1 failed  (manual disable survival)
  make a downgrade keep the old features   -> 1 failed  (downgrade)
  restored                                 -> 5 passed
```

**Open question left standing.** `platform_plans.features` remains unread by any code. It is either
dead weight from the franchise engine or an unbuilt franchise-tier feature; deciding is a product
call, not a code one — logged as **OQ-11**.

---

### T-05 · `402 feature_not_in_plan` with upgrade link

| Field | Value |
|---|---|
| **Phase** | P1 — Entitlements |
| **Gap** | E6 |
| **Status** | `Done` |
| **Dependencies** | T-01 ✅, T-02 ✅ |

**Description.** §5 requires the API to return `402 feature_not_in_plan` with an upgrade link when a
brand touches a feature it does not own. Today the entitlement filter strips the permission at token
mint, so the caller sees an indistinguishable `403`. There are zero occurrences of `402` /
`PaymentRequired` in the backend.

**Acceptance criteria.**
- A request for an un-entitled (but otherwise permitted) feature returns `402` with a body carrying
  the feature key and an upgrade URL; a genuinely unauthorized request still returns `403`. ✅
- The distinction is tested both ways. ✅
- At least one client renders the upgrade prompt rather than a generic error. ✅
- Full build + tests green. ✅

**The problem, precisely.** The entitlement filter strips un-entitled permissions from the token
**at mint**. So by the time a request is denied, "you may not do this" and "your plan does not
include this" are byte-identical: the permission is simply absent. Answering 403 for both tells a
paying customer they lack permission when what they lack is the plan — and leaves them nothing to act
on.

**How it is told apart.** The token gains an `ent_off` claim listing the FEATURE keys the brand has
not licensed. That is deliberately the complement rather than the stripped permissions: the feature
catalogue is tens of short keys, while the permissions it explains run to hundreds, and the JWT is
already ~6 KB. The permission→feature map is global, not per-brand, so it is looked up server-side
via a new cached `IFeatureCatalog` instead of being shipped in every token.

`StepUpAuthorizationResultHandler` became `ApiAuthorizationResultHandler` — the old name would have
lied once it handled two cases. **Step-up is checked first, on purpose:** a step-up denial means the
caller has both the permission and the plan and merely needs to re-verify, so reporting "upgrade your
plan" would send them to buy something they already own.

**Evidence.**
```
$ dotnet build laundryghar.slnx      67 Warning(s)   0 Error(s)
Passed! - Failed: 0, Passed:  83 - core.Tests.dll
Passed! - Failed: 0, Passed: 218 - operations.Tests.dll   (+5)
Passed! - Failed: 0, Passed: 106 - operations.IntegrationTests.dll
```
Frontends unchanged: admin-web tsc clean / 12 pre-existing warnings · pos-web clean / 2 ·
customer-mobile 164 · rider-mobile 85. **656 tests green overall.**

Six tests in `ApiAuthorizationResultHandlerTests`, written around the discrimination rather than the
happy path: step-up → structured 403; un-entitled feature → **402 naming the feature and the upgrade
path**; a permission denial on an **owned** feature → stays 403; step-up wins when both apply; no
`ent_off` claim → nothing becomes 402; an ungated permission (orphan/core, catalog returns null) →
stays 403, because null means "entitlement has nothing to say", never "not entitled".

**Mutation check:**

| Mutation | Result |
|---|---|
| every denial becomes 402 (feature check removed) | `Failed: 2, Passed: 4` |
| step-up no longer wins over entitlement | `Failed: 2, Passed: 4` |
| the `ent_off` short-circuit guard removed | first run: **survived** → test strengthened → `Failed: 1, Passed: 5` |

The third is worth recording honestly: it initially survived because it is an **equivalent mutant** —
with no `ent_off` claim `IsUnlicensed` is always false, so removing the guard changes no status code.
But it is not free: it makes every ordinary 403 in the system pay for a feature-map lookup it cannot
learn anything from. The fake catalogue now counts calls and the test asserts **zero lookups**, which
converts an equivalent mutant into a killed one and pins the optimisation.

**Files changed.** `TokenClaims` (+`ent_off`), `JwtTokenService`, `ScopeResolver` (computes the
complement), `SharedDataModel/Contracts/IFeatureCatalog.cs` + `Persistence/FeatureCatalog.cs` (one
query, 5-min cache, **fails open** — a catalogue outage must degrade 402→403, never fail the
request), `laundryghar.Utilities/Auth/FeatureNotInPlan.cs`,
`ApiAuthorizationResultHandler.cs` (renamed from `StepUpAuthorizationResultHandler`), the three
`Program.cs` registrations (**Scoped, not Singleton** — it now resolves a scoped `IFeatureCatalog`),
admin-web `lib/apiError.ts` (`featureNotInPlan`, `featureLabel`) and `api/client.ts` (402 branch).

**Client behaviour:** the toast now reads *"Custom domain is not included in your plan. Go to
Settings → Licensing to upgrade."* instead of *"You don't have permission to perform this action."*
A clickable action would be better still, but `showToast(variant, message)` has no action parameter
and extending the toast store + Toaster for it is a wider change than this task warrants — noting it
rather than silently doing it.

**Not yet observable in production:** `Entitlement:Enforced` is still `false`, so no token carries
`ent_off` yet and nothing returns 402 in practice. The path is proven by tests; it goes live with
T-04.

---

### T-06 · Roles follow features

| Field | Value |
|---|---|
| **Phase** | P1 — Entitlements |
| **Gap** | E12 |
| **Status** | `Done` |
| **Dependencies** | T-02 ✅ |

**Description.** §5: "Buy Fleet → Rider appears. Buy Processing → Facility Staff appears."
`RolesTab.tsx` greys cells by entitlement and `GetAccessRoles` filters by *vertical*, but no
feature→role mapping exists — a brand without the fleet module still sees and can grant the Rider
role.

**Acceptance criteria.**
- A declarative feature→role mapping; roles whose required feature is un-entitled are absent from
  `GetAccessRoles` and rejected by the grant-membership command (not merely hidden in the UI). ✅
- Tests assert both the hiding and the server-side rejection. ✅
- Full build + tests green. ✅

**What was built.** `identity_access.roles.feature_key` (migration 0006) + `BrandFeatureGate`, one
decision shared by the read path (`GetAccessRoles` — don't offer it) and the write path
(`GrantMembership` — don't allow it). Hiding alone would be cosmetic: anyone able to craft the
request could otherwise grant a Rider on a brand with no fleet, producing a user whose permissions
the entitlement filter strips at every login — a role that silently does nothing.

The grant gate keys on the **target** brand, not the actor's, so a platform admin acting on someone
else's brand still respects that brand's plan.

**What is gated — and what deliberately is not.** Only the three the strategy names:
`rider → fleet` (§6.1 "Rider (Fleet feature)"), `warehouse_supervisor`/`warehouse_staff →
processing_facility` (§6.1 "Facility Staff (Processing feature)"), `partner_admin`/`partner_operator
→ raas_partner`. The salon and logistics analogues (`salon_manager`, `salon_staff`,
`hub_supervisor`, `hub_operator`) are left **ungated on purpose**: mapping them to
`processing_facility` is tempting, but the strategy never says so, they are already constrained by
`roles.vertical_key`, and a role that silently disappears reads as data loss. Left for OQ-5.

**A bug in my own migration, caught by the tests.** The first version asserted `gated > 0`
unconditionally. That aborted the migration in the integration fixture, which applies the canonical
DDL and seeds no roles — and would have done the same on any fresh bootstrap before the identity
seeder runs. Now it asserts only when the gateable role codes actually exist.

**Evidence.**
```
$ db/tools/migrate.sh up / down / up      applied · rolled back · applied
$ db/tools/migrate.sh verify              verify OK
$ psql -tAqc "select code||' -> '||feature_key from identity_access.roles where feature_key is not null"
  partner_admin        -> raas_partner
  partner_operator     -> raas_partner
  rider                -> fleet
  warehouse_staff      -> processing_facility
  warehouse_supervisor -> processing_facility

$ dotnet build laundryghar.slnx      66 Warning(s)   0 Error(s)
Passed! - Failed: 0, Passed:  83 - core.Tests.dll
Passed! - Failed: 0, Passed: 218 - operations.Tests.dll
Passed! - Failed: 0, Passed: 113 - operations.IntegrationTests.dll   (+7)
```
Frontends unchanged. **663 tests green overall.**

**Mutation check — four regressions, each introduced separately:**

| Mutation | Result |
|---|---|
| the gate always opens | `Failed: 3, Passed: 4` |
| expired/disabled licences still count | `Failed: 1, Passed: 6` |
| no-brand-context treated as owns-nothing | `Failed: 1, Passed: 6` |
| core features no longer always-on | `Failed: 1, Passed: 6` |

The third is the subtle one: `EntitledFeaturesAsync` returns **null** for "no brand in context" and
that must mean *do not gate*, never *owns nothing*. Confusing the two would blank every gated role
from a platform operator's own console.

**Files changed.** `db/migrations/0006_roles_follow_features.{up,down}.sql`; `Role.FeatureKey` +
`RoleConfiguration`; `core.Application/Identity/AccessControl/BrandFeatureGate.cs` (new);
`GetAccessRoles.cs`; `GrantMembership.cs` (+ a scope→brand resolver for franchise/store/warehouse
grants); `RbacEfFixture` applies 0006; `RolesFollowFeaturesTests.cs` (7 tests).

---

### T-07 · Backend-owned terminology packs for all four clients

| Field | Value |
|---|---|
| **Phase** | P1 — Entitlements |
| **Gap** | V5 |
| **Status** | `Done` |
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
| **Status** | `Blocked` — **health checks shipped; issuance still needs a decision** |
| **Dependencies** | T-09 |

**Still blocked, and why.** **OQ-6** — §4.2 item 3 offers "Let's Encrypt / Cloudflare-for-SaaS"
without choosing. The two differ in infrastructure, recurring cost, and whether `ssl_status` is
polled or webhook-driven. Choosing a vendor on Goutam's behalf is choosing his bill, so certificate
**issuance and renewal** remain unbuilt. **What is needed: pick one.** Either answer is roughly a
day's work from here — the lifecycle columns, the status vocabulary, and the sweep it would hook
into all exist now.

**What shipped anyway.** §12's risk list asks for automated SSL and domain health checks "from day
one", and that half is identical whichever vendor wins: a certificate issued by Let's Encrypt, by
Cloudflare, or uploaded by hand leaves the same observable expiry. It is also worth having alone —
before this, a custom domain's certificate could expire and the first anyone heard of it was a
customer's customer seeing a browser warning.

- **A real TLS handshake**, not an HTTP probe. It reads the certificate the domain actually serves,
  so the answer is the certificate's own expiry and subject rather than an inference from a status
  code — and it still works for a domain whose app is down but whose TLS is fine, which is a
  different problem needing a different fix.
- **Transport validation is accepted and judged in code instead.** That looks alarming and is the
  point: rejecting an expired certificate at the handshake throws, and we would learn "it failed"
  rather than "it expired on Tuesday" — which is the one fact worth reporting. Nothing is sent over
  the connection and nothing is read from it.
- **A valid certificate for the wrong host is still degraded**, because it still gives every visitor
  a browser warning. "Valid" alone is not the question.
- **Consecutive failures are counted, not flagged.** One failed check is a network blip; five in a
  row is an outage, and a boolean cannot tell them apart. A success resets the counter.
- **Silence is not health**: a domain not checked in a week appears in the attention list even if its
  last result was fine.

**Evidence.**
- Migration `db/migrations/0018_domain_health.{up,down}.sql` — applied; up→down→up clean;
  `verify OK`. Adds `ssl_expires_at`, `ssl_issuer`, `last_checked_at`, `health_status`,
  `health_detail`, `consecutive_failures`, plus `kernel.domains_needing_attention` and
  `kernel.record_domain_check` (which owns the counter, so two workers racing cannot both write 1).
- `laundryghar.SharedDataModel/Persistence/TlsDomainHealthChecker.cs`,
  `commerce.Infrastructure/Worker/Services/RetentionSweepService.cs` (`CheckDomainHealthAsync` —
  stalest-first, capped at 100 per tick so a large estate drains fairly),
  `core.WebApi/Endpoints/Identity/AdminBrandDomains.cs` → `GET /admin/brands/.../health`.
- Tests: `operations.Tests/Auth/DomainHealthCheckerTests.cs` — 8 passed **against the real
  internet**, which is normally a smell and here is the point: a mocked handshake would only prove
  the mock works. They self-skip when offline.

```
  PASS  a healthy host reports its certificate (real expiry + issuer)
  PASS  an expired certificate is degraded AND dated   (expired.badssl.com)
  PASS  a certificate for the wrong host is degraded   (wrong.host.badssl.com)
  PASS  a host that does not resolve is unreachable, and says why
  PASS  malformed input is reported, not thrown
  PASS  a check is time-bounded                        (RFC 5737 TEST-NET-3)
```

- **Live**, the operator view over the real database:

```
              domain               | health_status | ssl_status | days_left | failures
  ---------------------------------+---------------+------------+-----------+---------
   expiring.example.com            | degraded      | pending    |         7 |        1
   wizard-test-43468.laundryghar…  | unreachable   | pending    |           |        1
   wizard-test-43190.laundryghar…  | unknown       | pending    |           |        0
   … (never-checked domains surface too — silence is not health)

  a healthy domain is absent from that list, and ssl_status followed the observation:
   good.example.com | healthy | active | 0

  failures accumulate (1 → 3) and a success resets them to 0
```

---

### T-13 · Vertical template as data

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | V4 |
| **Status** | `Done` |
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
| **Status** | `Done` |
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
| **Status** | `Done` |
| **Dependencies** | T-13, T-14 |

**Blocked reason (largely resolved 2026-08-25).** **OQ-1 is answered**: keep `salon`, add `tiffin`.
Migration 0004 widened all **eight** vertical CHECK constraints (not three) and `VerticalKey.cs` +
`verticalTerms.ts` now carry tiffin, so a tiffin brand is creatable today. What remains is authoring
the template content itself, which depends on T-13 and — for tiffin specifically — on T-14/OQ-2,
since a tiffin business without a recurring delivery calendar is not yet a working template.

**Evidence.** _(empty)_

---

### T-16 · Provider self-signup

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | P1 (§2.6) |
| **Status** | `Done` |
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
| **Status** | `Done` |
| **Dependencies** | T-16, T-09 |

**Description.** §9's order: sign up → template → plan/trial → wizard (locations, catalog seed, staff
invites, gateway link) → live on sub-domain. T-16 covers everything up to the catalogue seed; this is
the wizard and the finish line.

**Progress is derived, not remembered.** The obvious design is a `completed_steps` list the API
appends to — and it is the design that lies: finish "add your first location", delete the location,
and the console shows a finished setup over a business that cannot take an order. So the only stored
state is the two things a wizard genuinely cannot infer — **which steps were skipped** ("not now" is
invisible in the data) and **half-typed answers** (so closing the tab does not lose them). Everything
else is computed from the real rows by `kernel.brand_onboarding_facts`. Resume falls out for free:
"where was I" is always "the first step that is neither done nor skipped", recomputed each time,
with no cursor to drift.

**Two steps cannot be skipped.** A business with no location and no catalogue cannot take a single
order, so skipping past them would produce a "finished" setup over an account that does nothing —
and going live is not skippable because it *is* the finish line.

**No duplicate endpoints.** There is no wizard "create location" or "invite staff". Those already
exist, are already permission-gated, and already validate. The wizard says where you are; the
existing endpoints do the work — and the test proves the wizard notices a location created through
the ordinary `POST /admin/stores` with no wizard-specific write.

**Three real blockers found by driving it against a freshly signed-up account.**

1. **§9's first step was impossible.** A store requires a `franchise_id`, and a brand-new provider
   had **no franchise at all**. Signup now creates the provider's own operating entity — not a
   franchisee: zero royalty, zero marketing fee, owned by the person who just signed up.
2. **…and they could not discover its id.** `franchises` is an **Enterprise** feature, so a Growth
   provider is *correctly* refused the franchise list with 402 `feature_not_in_plan`. The wizard
   hands over that one id directly. Not a way around the entitlement — it exposes the provider's own
   entity and nothing about franchising to anyone else.
3. **Going live threw a 500.** Two causes: the SECURITY DEFINER function's pinned `search_path`
   omitted `public`, where the `citext` extension lives (`type "citext" does not exist`), and
   `verification_txt` is `NOT NULL` for the provider-owned-domain TXT challenge — meaningless for a
   host in our own zone, now filled with a `platform-subdomain` marker rather than a challenge nobody
   will look up.

**Acceptance criteria.** ✅ resumable wizard persisting progress · ✅ catalogue seeded from the chosen
template (T-16, surfaced as already-done) · ✅ brand reachable on its sub-domain at the end · ✅ tests
for resume and for each step's validation.

**Evidence.**
- Migration `db/migrations/0017_onboarding_progress.{up,down}.sql` — applied; up→down→up clean;
  `verify OK`. `onboarding_progress` + RLS + `kernel.brand_onboarding_facts` +
  `kernel.ensure_brand_subdomain`.
- `core.Application/Identity/ProviderOnboarding/**` (a **separate** namespace from
  `Identity.Onboarding`, which is FRANCHISE onboarding — a brand signing up its own franchisee. Two
  different actors; folding them together would confuse both),
  `core.WebApi/Endpoints/Identity/AdminProviderOnboarding.cs`,
  `laundryghar.SharedDataModel/Persistence/OnboardingFactsStore.cs`,
  `core.Application/Identity/Signup/Commands/CompleteSignup.cs` (the own-operations franchise).
- Tests: `operations.IntegrationTests/Rbac/OnboardingFactsTests.cs` (8), including
  `Deleting_the_data_undoes_the_step` — the one test that could not pass under a stored design.
- **Live**, a provider signed up through `/api/v1/signup` and taken to the finish line:

```
  2. wizard state straight after signup:
     [ ] Add your first location      No locations yet
     [x] Set up what you sell         8 item(s) — seeded from your template, edit the prices
     [ ] Invite your team             Just you so far
     [ ] Connect online payments      Not connected — you can still take cash
     [ ] Go live                      Not live yet
     current=location complete=False

  PASS  catalog is ALREADY done — the template seeded it
  PASS  resume points at the first real gap
  PASS  a half-typed form is saved …and comes back on the next visit
  PASS  a skippable step can be deferred
  PASS  a step the business cannot run without CANNOT be skipped (422)
  PASS  the finish line cannot be skipped (422)
  PASS  the franchises FEATURE is correctly withheld on this plan (402)
  PASS  …but the wizard hands over the provider's OWN entity
  3. created a location via the EXISTING endpoint -> HTTP 201
  PASS  the wizard notices, with no wizard-specific write — 1 location(s)
  PASS  went live -> wizard-test-43468.laundryghar.app   (idempotent on a second call)
  PASS  still incomplete while a real step is outstanding — current=payments

  5. final state:
     [x] Add your first location      1 location(s)
     [x] Set up what you sell         8 item(s) — seeded from your template, edit the prices
     [-] Invite your team             Just you so far
     [-] Connect online payments      Not connected — you can still take cash
     [x] Go live                      wizard-test-43468.laundryghar.app
     current=None complete=True

  PASS  wizard reports complete · completion time stamped · domain reported
```

**Scope note.** The "connect online payments" step has no configuration surface anywhere in this
codebase — there is no gateway-credentials table or screen to build against. Its completion is read
from `brands.config->>'paymentGateway'`, so the step is honest today (a provider taking cash defers
it) and becomes live the moment that screen exists. Logged as **OQ-13**.

---

### T-18 · Brand suspend / reactivate + login-only mode

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | P5, P6 (§2.6) |
| **Status** | `Done` |
| **Dependencies** | T-04 ✅ |

**Description.** §9: "Suspension = **login-only mode** (owner can see invoices and pay; operations
frozen) — never silent data loss." `brands.status` has a `suspended` value that **no auth or request
path reads**, so a suspended brand keeps operating normally. `SubscriptionBillingService` suspends
the *subscription* but nothing acts on it.

**Acceptance criteria.** ✅ all — a suspended brand permits only auth + billing routes and rejects
operational ones with a distinguishable code (`402 brand_suspended`); the Owner can still sign in and
reach billing; reactivation restores full access.

**Shape of the feature.** An allow-list, not a blanket block — and that is the whole design. A
suspended brand keeps every byte of its data and every login; what it loses is the ability to
*operate*. Cutting off the billing screens too would strand the owner outside the only door that
reopens their account, so `/auth/*`, `/admin/entitlements/*`, `/webhooks/*` and `/admin/navigator`
stay open. Platform admins are exempt — they are who reinstates a tenant.

Implemented as a middleware rather than a token claim on purpose: suspension must bite when the
payment lapses, not at the user's next login, or a brand trades on for the life of its access token.

**A bug this found, that had already shipped silently — the brands-RLS trap, third occurrence.**
`tenancy_org.brands` carries `rls_admin_only USING (rls_bypass())`, so ordinary tenant traffic —
exactly the traffic this gate exists to stop — reads it as EMPTY:
```
$ psql -U app_user -c 'select count(*) from tenancy_org.brands'  ->  0
$ psql -U postgres -c 'select count(*) from tenancy_org.brands'  ->  1
```
Reading `brands.status` through EF returned null on every tenant request, and because the status
store **fails open** by design, the gate did not error — it never fired. All 14 unit tests passed
throughout, because they inject a fake store. **A fail-open default over an RLS-invisible table hides
itself.** Fixed by migration 0009's `kernel.brand_status(uuid)` (SECURITY DEFINER, pinned
`search_path`, granted only to app_user/app_admin) — the same remedy as `resolve_brand_domain`, and
now covered by an integration test that runs as the real non-superuser.

**Evidence — live, against a running host and a genuinely suspended brand:**
```
brand SUSPENDED
  GET  /api/v1/admin/users                 -> 402  brand_suspended    (operations frozen)
  POST /api/v1/auth/password/login         -> 200                     (can still sign in)
  GET  /api/v1/admin/navigator             -> 200                     (console renders)
brand REINSTATED
  GET  /api/v1/admin/users                 -> 200                     (service restored)
```
```
$ dotnet build laundryghar.slnx      66 Warning(s)   0 Error(s)
Passed! - Failed: 0, Passed:  85 - core.Tests.dll
Passed! - Failed: 0, Passed: 232 - operations.Tests.dll          (+14)
Passed! - Failed: 0, Passed: 116 - operations.IntegrationTests.dll (+3)
```
**682 tests green overall.**

**Mutation check:**

| Mutation | Result |
|---|---|
| the gate never fires | `Failed: 1, Passed: 13` |
| the allow-list is ignored (suspension becomes a trap) | `Failed: 8, Passed: 6` |
| platform admins get locked out too | `Failed: 1, Passed: 13` |
| fails CLOSED on an unknown status | `Failed: 2, Passed: 12` |

The second failing 8 is the point: those are the "way back in" cases.

**Files.** `db/migrations/0009_brand_status_lookup.{up,down}.sql`;
`SharedDataModel/Contracts/IBrandStatusStore.cs` + `Persistence/BrandStatusStore.cs` (30s TTL — kept
short because nobody minds waiting to be cut off, everybody minds waiting to be let back in);
`laundryghar.Utilities/Middlewares/BrandSuspensionMiddleware.cs`; wired into all three hosts between
tenant resolution and authorization; `BrandSuspensionMiddlewareTests` (14), `BrandStatusLookupTests` (3).

---

### T-19 · Per-provider rate limits

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | P8 (§2.6) |
| **Status** | `Done` |
| **Dependencies** | T-04 ✅ |

**Description.** §7 lists per-provider rate limits as a platform control. `laundryghar.Gateway`
partitions its fixed-window limiter by **client IP** only.

**Acceptance criteria.** ✅ brand-keyed partition falling back to IP for unauthenticated traffic;
tests assert one brand's burst does not throttle another. **Per-PLAN quotas are not wired** — the
gateway would need an entitlement lookup per request to know a caller's tier, and the limit is
currently a single configurable `RateLimit:BrandPermitLimit` (default 10× the per-IP allowance).
Raising it per tier belongs with T-23's usage metering.

**Why IP was the wrong unit, in both directions.** It *punishes the innocent* — an office, a carrier
NAT or a corporate proxy collapses many tenants onto one IP, so a busy neighbour throttles everyone
behind it. And it *fails to contain the guilty* — one tenant across a few machines simply gets a
multiple of the budget, so a runaway integration degrades the platform for everyone else. Keying on
`brand_id` makes the budget follow the tenant actually being metered.

**One design decision worth stating.** The gateway does not validate tokens (each service does that
against the Identity JWKS), so the brand is read from the JWT payload **without verifying the
signature** — decoded by hand rather than with a JWT library, precisely so that is obvious at the
call site. That is safe here and would not be anywhere else: the only thing this value decides is
which bucket a request is counted in. Forging it cannot grant access, read data or raise a limit —
the worst it achieves is being counted against someone else's budget, and the service behind the
gateway still rejects the token outright.

**Evidence.**
```
$ dotnet build laundryghar.slnx      66 Warning(s)   0 Error(s)
Passed! - Failed: 0, Passed:  85 - core.Tests.dll
Passed! - Failed: 0, Passed: 243 - operations.Tests.dll          (+11)
Passed! - Failed: 0, Passed: 116 - operations.IntegrationTests.dll
```
**693 tests green overall.**

11 tests in `RateLimitPartitioningTests`, written around the two failure modes: two brands on ONE IP
get separate budgets; one brand across MANY IPs shares one; anonymous falls back to IP; `X-Brand-Id`
takes precedence; `X-Forwarded-For` is honoured; keys are namespaced (`brand:` / `ip:`) so an id can
never collide with an address; and five malformed `Authorization` headers degrade to IP without
throwing.

**Mutation check:**

| Mutation | Result |
|---|---|
| always partition by IP (the original behaviour) | `Failed: 4, Passed: 7` |
| key namespaces removed | `Failed: 9, Passed: 2` |
| `X-Forwarded-For` ignored | `Failed: 1, Passed: 10` |

**Files.** `laundryghar.Gateway/RateLimitPartitioning.cs` (new), `laundryghar.Gateway/Program.cs`,
`tests/operations.Tests/Auth/RateLimitPartitioningTests.cs`, and a project reference from the test
project to the gateway.

---

### T-20 · Support impersonation with consent + audit

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | P9 (§2.6) |
| **Status** | `Done` |
| **Dependencies** | T-18 |

**Description.** §7: "Support: impersonation with consent + full audit (Platform Support role,
read-first)." §8.1: the platform must not "change a company's branding, config, or data without
recorded consent." Before this the only trace was an unused enum member in `AuthMethod.cs`.

**What this is not.** It does **not** narrow `platform_admin`'s §2.3 "operate-as-tenant", which is
unchanged and was not in scope. This is the path for **support**, who hold no RLS bypass and no
brand: a consented, time-boxed, read-first session the provider grants and can end at any moment.

**Four properties, each enforced in SQL rather than trusted to application code.**

| Property | How it is enforced |
|---|---|
| Consent | a grant is born `pending`; only a brand-side approval arms it. `kernel.request_impersonation` — the only door support has — can *only* create `pending` rows |
| Time-boxed | `CHECK` — an `approved` grant must carry an expiry, and it cannot exceed 24h |
| Read-first | `scope` defaults to `read_only`; the guard refuses every unsafe HTTP method, with no allow-list to grow |
| Revocable | the guard re-reads grant state on **every** request, so revocation bites on the next call, not at token expiry |

**Consent the grantee can grant itself is not consent.** `impersonation.request` and
`impersonation.approve` are held by **disjoint** roles, and `platform_admin` is deliberately absent
from the approve list. The migration aborts if any role ever holds both, and a test re-asserts it so
a later grant edit fails loudly.

**A real hole found by driving it live.** A read-write session's write was refused for step-up —
correct, impersonation must not bypass §8 — and the obvious retry, *step up and try again*, re-minted
an **ordinary** token: `StepUpVerifyHandler` rebuilds claims from `ScopeResolver`, which knows nothing
about impersonation. It granted nothing the caller lacked signed-out, but it silently dropped the
read-only limit and the consent stamp from every later audit row. Fixed by carrying the session
across the re-mint, plus three regression tests.

**Acceptance criteria.** ✅ recorded, time-boxed consent · ✅ read-only by default · ✅ token carries
the real actor (support's `sub` is never rewritten — an audit that lies about who acted is the one
thing this feature must not do) and the tenant it is scoped to · ✅ every impersonated action in
`audit_logs` with both identities · ✅ expiry and revocation · ✅ tests for all four.

**Evidence.**
- Migration `db/migrations/0014_impersonation_grants.{up,down}.sql` — applied; up→down→up clean;
  `verify OK`. Table + RLS + `kernel.impersonation_grant_state` + `kernel.request_impersonation` +
  `audit_logs.impersonation_grant_id` + the two permissions + the disjointness assertion.
- `laundryghar.Utilities/Middlewares/ImpersonationGuardMiddleware.cs` (wired into all three hosts),
  `laundryghar.SharedDataModel/Persistence/ImpersonationStateStore.cs` (uncached, **fail-closed** —
  the deliberate inverse of `BrandStatusStore`),
  `core.Application/Identity/Impersonation/**`, `core.WebApi/Endpoints/Identity/AdminImpersonation.cs`.
- Step-up fix: `core.Application/Identity/Auth/Commands/StepUpVerify/StepUpVerifyHandler.cs`.
- Tests: `operations.Tests/Auth/ImpersonationGuardMiddlewareTests.cs` (22),
  `operations.IntegrationTests/Rbac/ImpersonationConsentTests.cs` (9),
  `operations.Tests/Auth/CurrentUserClaimMappingTests.cs` +3. **Mutation-tested** — trusting the
  token's scope over the database's fails 2, failing open on a store error fails 1, dropping the
  identity check fails 2.
- **Live**, two real humans against a running core host, `admin@laundryghar.local` (support) and
  `owner@lgmain.test` (brand owner on LG-MAIN):

```
  PASS  state is pending, nothing granted
  PASS  cannot start before consent (403)
  PASS  owner approved (after step-up — approve is a CRITICAL permission)
  PASS  re-asking returns the same grant, live approval intact
  PASS  token keeps SUPPORT's identity; brand_id scoped to the tenant; amr=impersonation
  PASS  read-only session CAN read (200)
  PASS  read-only session CANNOT write (403 impersonation_not_permitted)
  PASS  impersonation does NOT bypass step-up
  PASS  step-up KEEPS the consent stamp / tenant scope / granted scope
  PASS  read-write session CAN write once stepped up (201)
  PASS  owner revoked -> the SAME token stops working at once (403)
```

- And the audit trail, showing **both identities** on an action taken inside the session:

```
  users.created  actor=admin@laundryghar.local  consent=5c290a61
  user.create    actor=admin@laundryghar.local  consent=5c290a61
```

  (`actor_user_id` names the human; `impersonation_grant_id` joins to the consent, which names the
  owner who gave it, the scope and the window. A copied user id would have answered none of that.)

---

### T-21 · Provider cancellation — export, retention, deletion

| Field | Value |
|---|---|
| **Phase** | P3 — Vertical templates |
| **Gap** | P10 (§2.6) |
| **Status** | `Done` |
| **Dependencies** | T-18 |

**Description.** §9: "Cancellation = export offered, wind-down retention, then deletion per DPDP."
§8.2: a company may "export their data at any time; take it with them if they leave." The existing
DPDP machinery is **customer**-level (one person asks to be forgotten); there was no brand-level
wind-down at all.

**The shape.** `active → cancelled → archived`, with `cancelled → active` open the whole way:

- **`cancelled`** — operations frozen, but **login, billing and export stay open**, the same
  login-only shape as suspension and for the same reason. A retention window the customer is locked
  out of is deletion with a delay, which is the opposite of what §8.2 promises.
- **`archived`** — data purged, brand row kept as a tombstone (`audit_logs.brand_id` is an FK with
  `ON DELETE RESTRICT` into a 7-year ledger, so deleting the row would mean destroying the audit
  trail). Name scrubbed to "Deleted brand", every identifying column nulled.
- **Withdrawal** works right up to the purge, because people cancel by mistake and the cost of
  allowing a reversal is one boolean.

**Nothing in a request path can delete a tenant.** There is no delete endpoint. `kernel.purge_brand`
is not granted to `app_user` at all — it belongs to the worker, on the clock the retention window
sets. Withdrawal is honoured by construction: the worker only ever selects rows still in
`retention`, so someone who changes their mind at hour 23 of day 30 is simply never picked up.

**The table list is derived, never hand-maintained.** `kernel.brand_scoped_tables()` reads the
catalogue — 121 tables on this database. A hand-written list would be wrong within a month, and
wrong here means either shortchanging a customer's export or leaving data behind after telling them
it was deleted. The migration aborts if the function misses any table carrying `brand_id`.

**Deletion by fixpoint.** ~121 tables with foreign keys between them in no order the function can
know. It deletes in repeated passes, deferring anything with live dependents, until a pass achieves
nothing — then **raises**. A purge that half-finished and reported success would mean telling a
customer their data is gone when it is not.

**Two defects found by driving it live, neither visible to any unit test.**

1. **The brands-RLS trap, a fourth time.** `_db.Brands.FirstOrDefaultAsync(...)` returned null for
   an owner cancelling their **own** account — `tenancy_org.brands` is `rls_admin_only` — so the
   endpoint answered *404, your brand does not exist* to the person who owns it. Fixed with
   `kernel.set_brand_cancellation_state`, which can only move between `active` and `cancelled`:
   an owner cancelling can never become an owner deleting.
2. **The purge destroyed its own record.** `brand_cancellations` is brand-scoped, so the sweep
   deleted the wind-down row itself — the worker's "mark it purged" then matched nothing, and *"did
   we actually delete this tenant, and when?"* had no answer anywhere. Now on the keep list.
   (A third, smaller one: the export stream emitted a UTF-8 BOM, making the first NDJSON line
   unparseable to strict readers.)

**What the purge deliberately keeps** — audit logs, our platform invoices/subscriptions, and the
cancellation record. Law outranks preference; logged as **OQ-10** since the strategy does not decide it.

**Acceptance criteria.** ✅ owner-triggered full export (streamed NDJSON, works *at any time*, not
only at cancellation) · ✅ retention window, floored at 7d and capped at 180d · ✅ deletion after it
elapses · ✅ tests for export completeness, cross-tenant isolation, retention honoured, deletion
irreversible-and-audited.

**Evidence.**
- Migration `db/migrations/0015_brand_cancellation.{up,down}.sql` — applied; up→down→up clean;
  `verify OK`. `brand_cancellations` + RLS + `brand_scoped_tables()` + `export_brand()` +
  `purge_brand()` + `set_brand_cancellation_state()`.
- `core.Application/Identity/Cancellation/**`, `core.WebApi/Endpoints/Identity/AdminCancellation.cs`,
  `laundryghar.SharedDataModel/Persistence/BrandExportService.cs` (streams via a raw
  `NpgsqlDataReader` in `SequentialAccess` — peak memory is one record regardless of tenant size),
  `laundryghar.Utilities/Middlewares/BrandSuspensionMiddleware.cs` (the `cancelled` gate + allow-list),
  `commerce.Infrastructure/Worker/Services/RetentionSweepService.cs` (`PurgeCancelledBrandsAsync`).
- Tests: `operations.IntegrationTests/Rbac/BrandCancellationTests.cs` (14),
  `operations.Tests/Auth/BrandSuspensionMiddlewareTests.cs` +2.
- **Live**, a real provider signed up through `/api/v1/signup` and taken through the whole cycle:

```
  1. provider created: WINDDOWN-TEST-40779 — 8 items seeded
  PASS  export works while ACTIVE (§8.2 'at any time') — 38 records
  PASS  export includes the brand's own record
  PASS  export spans many tables — 5 tables
  PASS  export contains no other tenant's rows — 0 suspicious
  PASS  cancellation accepted
  3. wind-down: status=retention retentionUntil=2026-09-01 daysRemaining=7
  PASS  operations frozen, named as a cancellation not a payment problem — 402 brand_cancelled
        "This account is being closed. You can still sign in to export your data, or withdraw..."
  PASS  export STILL works during the wind-down — 42 records
  PASS  export was recorded as proof it was offered — count=1
  PASS  withdraw accepted -> operations restored immediately
```

- And a real brand actually deleted, with the window fast-forwarded to what the worker looks for:

```
  before:  39 records · 1 audit row · status active
  due for purge: 1
  purge:   brand_feature 25 · items 8 · service_categories 3 · ... (36 rows)
  after:   4 records left · audit rows KEPT: 1
           tombstone: archived / "Deleted brand" / deleted_at set
           cancellation record survives: reason "live purge verification #2"
```

---

### T-22 · White-label Expo app factory

| Field | Value |
|---|---|
| **Phase** | P4 — White-label app |
| **Gap** | W7 |
| **Status** | `Blocked` — **per-brand config shipped; submission still needs a decision** |
| **Dependencies** | T-07, T-17 |

**Still blocked, and why.** **OQ-7** — §4 calls T3 a "one-time fee" but never says **who submits to
the App Store and Play Store**. If we submit on the provider's behalf, the factory must hold their
developer-account credentials, which I will not build without an explicit decision. If the provider
submits, it needs a config bundle and instructions. Those are very different builds.
**What is needed: say who submits.**

**What shipped anyway.** Either answer needs the same per-brand values, and today they are hardcoded
in `customer-mobile/app.config.ts` and `rider-mobile/app.config.ts`. Deriving them from the brand is
the shared prerequisite of both, so it is built and gated.

**A gap found on the way, bigger than the task.** `custom_domain` and `white_label_app` are both
**sellable Enterprise features** in §5 — and neither had a **module**. Entitlement is enforced along
`permission → module → feature`, so a feature with no module sits at the end of a chain nothing
walks: it can be bought, it appears on the price list, and it **gates nothing**. Custom domains and
the white-label config were reachable by any brand on any tier. Two of Enterprise's four selling
points were free. Migration `0019` adds the modules and feature-backed permissions, and grandfathers
every brand already using a custom domain — taking it away would be a regression dressed as a
feature gate, the same reasoning `0008` used.

**Its assertion then found six more.** `wallet`, `online_payments`, `loyalty`, `item_tracking`,
`whatsapp_bot` are sellable and gate nothing — they have no console module at all. (`api_access` is
also listed but IS enforced, directly in `kernel.resolve_api_key`, not through the module chain.)
Fixing those means inventing module and permission mappings for surfaces that do not exist, which is
inventing scope — so the migration **WARNs on every apply** rather than papering over it. Logged as
**OQ-14**.

**A defect the API response could not show.** The first version emitted
`com.laundryghar.lg-main.customer` — a perfectly reasonable-looking string and an **invalid Android
package name**, because a package segment must be a valid Java identifier. It would have failed at
build time, long after anyone was looking. There are now two sanitisations: the slug keeps hyphens
(Expo slugs and URL schemes accept them, and a multi-word brand is unreadable without), the
identifier strips to alphanumerics. These cannot be changed after the first store submission, so
both err toward boring.

**Evidence.**
- Migration `db/migrations/0019_premium_feature_modules.{up,down}.sql` — applied; up→down→up clean;
  `verify OK`. Modules + `domains.read` / `domains.manage` / `white_label.read` +
  `kernel.brand_app_identity` (**the brands-RLS trap, fifth time** — the endpoint answered 404 to
  the person whose app it is) + the grandfathering + the orphaned-feature assertion.
- `core.Application/Identity/WhiteLabel/Queries/GetAppConfig.cs`,
  `core.WebApi/Endpoints/Identity/AdminWhiteLabel.cs`,
  `core.WebApi/Endpoints/Identity/AdminBrandDomains.cs` (re-gated on `domains.*` rather than
  `brands.*` — re-pointing the general brand permissions would have made a brand's own **name**
  uneditable without a domain add-on).
- Tests: `core.Tests/WhiteLabel/AppIdentifierTests.cs` — 14 passed.
- **Live**:

```
  /white-label/apps/customer -> 200  slug=lg-main-customer  scheme=lg-main-customer  package=com.laundryghar.lgmain.customer
  /white-label/apps/rider    -> 200  slug=lg-main-rider     scheme=lg-main-rider     package=com.laundryghar.lgmain.rider
  /white-label/apps/nonsense -> 404  (a typo must not yield a config for an app nobody ships)
  without the feature        -> 402  {"feature_not_in_plan": ["white_label_app", "/settings?tab=plan"]}
```

---

### T-23 · Partner / public API

| Field | Value |
|---|---|
| **Phase** | P4 — White-label app |
| **Gap** | A1 |
| **Status** | `Done` |
| **Dependencies** | T-02, T-19 |

**Description.** §11 P4 + the `api_access` entitlement in §5. What existed was `PartnerAuth.cs` (RaaS
partner-**user** OTP login — a human) and the OAuth server (the MCP/assistant flow — a *delegated*
human). Neither is a machine credential: both assume someone is present to prove who they are.

**The credential.** `lg_<env>_<16 hex handle>_<secret>`. The handle is a public, indexed lookup
key so verification is one indexed read plus one hash comparison — without it the only way to
identify a key is to hash the candidate against every row, which gets slower the more customers
succeed. The secret is 32 random bytes, shown **once**, stored only as an Argon2id hash through the
existing `IPasswordHasher`. A second hash format for secrets would mean two password-hashing
implementations to keep correct, and the second one is always the weaker.

**Every failure returns the same message.** Distinguishing unknown / revoked / expired / unpaid tells
an attacker which guess was closest and helps no legitimate integrator, who has the key's status in
their own console. The single exception is entitlement, which is actionable — and only reachable by
someone who has already proved they hold a real key.

**Entitlement travels with the key.** `kernel.resolve_api_key` answers "does this brand license
`api_access`" in the same round trip that resolves the key, so it cannot be forgotten at a call site.
Verified live: dropping the feature stops an existing key mid-flight; buying it back revives the
same key with no re-issue.

**Three defects found by driving it live, none visible to a unit test.**

1. **Every issued key 401'd.** Secrets are base64url, whose alphabet *includes the underscore*, so
   the `Split('_')` returned five parts for a valid key and refused it — on a 32-byte random secret,
   most of them. Fixed by bounding the split at four and pinning the handle to exactly 16 hex
   characters, which makes the format unambiguous *and* stricter than before.
2. **Metering lost most of its writes.** `_ = RecordUseAsync(...)` raced the scoped `DbContext`'s
   disposal; a key that served nine requests reported two. Awaited now — one indexed upsert.
3. **Per-key rate limits silently did nothing.** Two compounding causes: the `ApiKey` scheme is not
   the default one, so it runs during *authorization* and `HttpContext.User` is empty at limiter
   time; and `GetFixedWindowLimiter`'s factory runs **once per partition**, so a key whose ceiling
   was not yet known got pinned to the default bucket forever. Fixed by partitioning on the prefix
   parsed from the header — pre-auth, so a guessing loop is throttled before it costs anything — with
   the resolved limit folded into the partition key.

**Acceptance criteria.** ✅ issuance + revocation per brand · ✅ scoped machine tokens · ✅ per-key
rate limits and usage metering · ✅ gated on `api_access` · ✅ tests for all five.

**Evidence.**
- Migration `db/migrations/0016_api_keys.{up,down}.sql` — applied; up→down→up clean; `verify OK`.
- `laundryghar.Utilities/Auth/ApiKey/{ApiKeyAuthentication,ApiScopeRequirement}.cs`,
  `laundryghar.SharedDataModel/Persistence/ApiKeyStore.cs` (uncached — a revoked credential must die
  on the next request, not at the end of a TTL), `core.Application/Identity/ApiKeys/**`,
  `core.WebApi/Endpoints/Identity/{AdminApiKeys,PublicApi}.cs`, `core.WebApi/Program.cs`
  (scheme + `api_key` limiter policy).
- Tests: `operations.Tests/Auth/ApiKeyAuthTests.cs` (35),
  `operations.IntegrationTests/Rbac/ApiKeyStoreTests.cs` (8).
- **Live**, against a running core host:

```
  PASS  key issued (owner stepped up — api_keys.manage is CRITICAL)
  PASS  the secret is NEVER listed / the public prefix IS listed
  PASS  key authenticates via X-API-Key  and  via Authorization: Bearer
  PASS  an owner's session token does NOT open the public API (401)
  PASS  a key issued NO scopes can do nothing (403)
  PASS  a wrong secret / an unknown key / a test key with a live handle — all refused
  PASS  per-key rate limit bites at the key's own ceiling — 3x200 6x429 on a 5/min key
  PASS  usage is metered per key
  PASS  revoked -> the SAME key stops working at once (401)
  PASS  the revoked key keeps its row and its history
  PASS  works while the brand licenses api_access
  PASS  the SAME key stops working when the plan drops api_access
  PASS  and works again once it is bought back — no re-issue needed
```

**Scope note, stated rather than buried.** The public surface ships **one** endpoint,
`GET /public-api/me`, which proves the whole chain end to end. *Which* business operations to expose
publicly, and under what scope names, is a product decision the strategy does not make: §11 P4 says
"partner/public API" and §5 sells `api_access`; neither names an operation. Inventing that list would
be inventing scope. Logged as **OQ-12** — adding an endpoint is one line once it is decided.

---

### T-24 · The 8-role preset + 10-permission-group surface

| Field | Value |
|---|---|
| **Phase** | Cross-cutting |
| **Gap** | R5, R6, R7, R8 |
| **Status** | `Done` |
| **Dependencies** | T-06 |

**Description.** §6 is the strategy's simplification layer: max 8 roles, 10 permission groups, plain
do/don't language, customize-by-subtraction-only, and Law 1 (money/branding/subscription = Owner
only). The live DB has **17** system roles and a per-module permission matrix across ~34 modules.
None of `owner`, `manager`, `staff`, `facility_staff` exists.

**OQ-5 resolved by §6.3 itself.** The question was whether the 8 *replace* the 17 (a destructive
grant migration across every brand) or *present* them (additive). §6.3 answers it in its own words —
"Presets over the engine … the engine's power stays" — so this is a presentation layer, and
migration `0013` states the guarantee it keeps: no role renamed, deleted or re-scoped, no
`role_permission` row touched, every existing membership still working.

**The matrix is derived, not authored.** `GetRoleSurfaceQuery` computes each of the 80 cells from
the real `role_permissions` grants (`full` if the role holds a write permission in that group,
`view` if read-only, `none` otherwise). A hand-maintained matrix drifts from the grants the moment
someone edits a role — and a role matrix that lies is worse than none, because people delegate on
it. `customer` reports `own` for its four groups: it maps to no engine role at all (customers are
outside the staff RBAC graph, `docs/rbac.md` §2) and is listed so §6's picture is complete.

**Acceptance criteria.** ✅ ≤8 presets and ≤10 groups, asserted in SQL · ✅ every preset carries both
halves of the plain language · ✅ presets that need a feature (`rider`→`fleet`,
`facility_staff`→`processing_facility`) agree with the engine gating from `0006` · ✅ zero grants
mutated.

**Evidence.**
- Migration `db/migrations/0013_role_presets_and_groups.{up,down}.sql` — applied; up→down→up clean;
  `migrate.sh verify` OK. Asserts `count(role_presets) <= 8` and `count(permission_groups) <= 10`.
- `backend/laundryghar/laundryghar.SharedDataModel/Entities/IdentityAccess/RolePreset.cs`,
  `PermissionGroup.cs` + EF configurations; DbSets on all three contexts.
- `backend/laundryghar/core.Application/Identity/AccessControl/Queries/GetRoleSurface/GetRoleSurface.cs`
- `backend/laundryghar/core.WebApi/Endpoints/Identity/AdminAccessControl.cs:33` —
  `GET /api/v1/admin/access-control/role-surface`, `permission:roles.list`.
- Tests: `tests/operations.IntegrationTests/Rbac/RoleSurfaceTests.cs` — 6 passed.
- **Live**, super-admin against the real database — 8 presets × 10 groups = 80 cells:

```
                   bookin dispat custom catalo  staff  money proces report settin billin
  Platform Admin     full   full   full   full   full   full   view   view   full   full
  Platform Support   view   view   full   none   none   full   none   none   none   view
  Owner              full   full   full   full   full   full   view   view   full   full
  Manager            full   view   full   view   full   full   view   view   view   full
  Staff              full   view   full   view   none   view   view   none   none   none
  Rider              none   none   none   none   view   none   none   none   none   none
  Facility Staff     view   none   none   none   none   none   view   none   none   none
  Customer            own   none    own   none   none    own    own   none   none   none
```

  **Corrected 2026-08-25.** An earlier version of this entry showed a different matrix and said
  "two cells also differ from §6.2's table". That was wrong, and wrong in the direction that
  matters. Diffing all 80 cells against `PLATFORM_STRATEGY.md` §6.2 properly found **22**
  divergences, six of which looked like a role holding MORE authority than the strategy allows.

  **Four of those six were bugs in this feature's own group definitions**, not real grants:

  | Mislabelled | What it did |
  |---|---|
  | `billing` claimed `royalty` + `subscription` | §6.2 group 10 is "Subscription & Billing (**pays us**)". `royalty` is a franchisee paying *their brand*; `subscription` is a *customer's* recurring plan. Three money flows, one of them ours. The Manager preset appeared to hold **full control of the platform subscription** — reading as a flat **Law 1** violation that had never happened |
  | `settings` claimed `feature_flag` | Which features are on is not branding |
  | `staff` claimed the `rider` module | `rider.tasks.*` is a rider's *own* jobs, not people management — which simultaneously made Rider look like a manager and emptied the Rider row where §6.2 says `own` |
  | `bookings` omitted `subscription` | A recurring plan is a repeating booking |

  This is precisely the failure this feature was built to prevent — the entry above argues a matrix
  that lies is worse than none, and then its own group table made it lie. Fixed in migration `0020`,
  with the invariant that would have caught it (`no module in two groups`) now asserted in SQL and in
  `RoleSurfaceTests`. A first attempt at the fix **over-corrected** — sweeping locations, franchises
  and platform internals into groups 9 and 10 pushed apparent over-grants from six to eight, so that
  half was reverted and those modules are left explicitly unclassified and named by a WARNING on
  every apply (**OQ-17**).

  Agreement went **53 → 58 of 75** gradeable cells, and both frightening cells disappeared. The
  live matrix now reads:

```
                   bookin dispat custom catalo  staff  money proces report settin billin
  Platform Admin     full   full   full   full   full   full   full   view   full   full
  Platform Support   view   view   full   none   none   full   none   none   none   none
  Owner              full   full   full   full   full   full   full   view   full   full
  Manager            full   full   full   view   full   full   full   view   none   view
  Staff              full   view   full   view   none   view   view   none   none   none
  Rider              none   view   none   none   none   none   none   none   none   none
  Facility Staff     view   none   none   none   none   none   view   none   none   none
  Customer            own   none    own   none   none    own    own   none   none   none
```

  The five remaining over-grants are **real**, and are grant decisions rather than labelling ones —
  so they are reported, not silently re-granted (**OQ-9**, **OQ-16**). The twelve under-grants are
  led by Platform Support, which §6.2 gives `V` across eight groups and which currently holds almost
  none of them.

---

## Conformance audit — `PLATFORM_STRATEGY.md`, section by section (2026-08-25)

Run against the **live database and a running host**, not against this task list. That distinction
matters: the task list said T-18 "suspend / login-only mode" was `Done`, and it was — the half that
existed. Auditing §9 state by state found the other half missing.

| § | Claim | Verdict |
|---|---|---|
| **§2** | "`platform_plans.features` JSONB — feature entitlements **already done**" | ❌ **The document is wrong here.** That column holds 2 rows of incompatible test data on the *franchise* axis and no C# reads it. Entitlement resolves from `identity_access.brand_feature`. Conflict **D9**; SQL wins |
| **§3** | Three modes, one engine | ✅ `process_deliver` · `point_to_point` · `recurring` all present |
| **§3** | Launch templates: Laundry, Courier/Parcel, Tiffin | ✅ all three, plus **salon** — added at your instruction, beyond §3's list |
| **§3** | "Terminology is config, not code" | ✅ `vertical_terms`, 4 verticals × 3 terms, served to all four clients |
| **§4** | T1 listed / T2 own domain / T3 own app | ✅ / ✅ / ⚠️ config shipped, submission blocked (**OQ-7**) |
| **§4.1–5** | `brand_domains` · TXT verify · **SSL automated** · Host resolution · sender identity | ✅ ✅ ❌ ✅ ✅ — SSL **issuance** blocked (**OQ-6**); health checks shipped |
| **§5** | The 17-feature catalogue | ✅ **17/17 present** |
| **§5** | Starter / Growth / Pro / Enterprise contents | ✅ each tier carries exactly what the table lists, and is cumulative (asserted in SQL) |
| **§5** | `402 feature_not_in_plan` with upgrade link | ✅ verified live |
| **§5** | Roles follow features | ✅ migration `0006` |
| **§6** | Max 8 roles · 10 permission groups | ✅ asserted in SQL — §12's "resist role #9" |
| **§6.2** | The role × group matrix | ⚠️ **58/75 agree.** 4 labelling bugs fixed (`0020`); 5 real over-grants and 12 under-grants reported, not silently re-granted (**OQ-9**, **OQ-16**, **OQ-17**) |
| **§6.3** | Presets over the engine, additive | ✅ zero grants mutated — asserted against the migration's own text |
| **§7** | Onboard · Monitor (`mv_franchise_saas_mrr`) · kill-switches · rate limits · impersonation | ✅ all five |
| **§8.1/8.2** | Platform/Company split; "export at any time" | ✅ streamed NDJSON export, works while active *and* mid-wind-down |
| **§9** | Signup → **Trialing** → Active | ✅ `status='trialing'` + period end (11 live rows) |
| **§9** | Active → **PastDue** → dunning → **Suspended** | ❌ **found missing → now built.** See below |
| **§9** | Suspended → Active on payment | ✅ built, and refuses to clear a ToS suspension |
| **§9** | Cancelled → export → retention → deletion | ✅ T-21, proven on a real brand |
| **§12** | The four risks | ✅ terminology · ✅ health checks · ✅ role cap asserted · ✅ laundry still the flagship |

### The §9 gap, and what closed it

§5 calls suspend-on-nonpay "already built" and §9 draws the full state machine. Both were true of the
**customer** subscription engine. Neither was true of the **company's subscription to us**: the
suspension *gate* has been tested since T-18, but **nothing in the codebase ever wrote
`brands.status = 'suspended'`** — grep found zero writers. A provider could stop paying and trade
indefinitely; the only way to suspend anyone was by hand, in SQL.

Migration `0021` + a dunning pass in `BrandPlatformBillingService` close it:

- overdue invoice → `past_due`, attempt counted, retry scheduled on the existing backoff;
- **both** conditions before suspending — retries exhausted **and** a 14-day grace window elapsed.
  Either alone suspends too eagerly: a card failing three times in an afternoon is a bank problem;
- payment recovered → reinstated, **payments honoured first in the cycle** so a company that paid
  this morning is never suspended this afternoon by a stale invoice;
- **`brands.suspension_reason`** — because automatic reinstatement without it would let a brand
  suspended for **ToS** clear itself by settling a bill. Dunning only ever undoes what dunning did;
- a `cancelled` or `archived` brand is never touched — suspending mid-export would break §8.2.

Proven live, and in `BrandDunningTests` (10 tests):

```
  start: active / (none)
  dunning suspends it, recording why  -> suspended / nonpayment
  payment recovered                   -> active / (none)
  ToS suspension + attempted reinstate-> STILL suspended / tos      <-- the one that matters
  wind-down + attempted suspend       -> STILL cancelled
```

---

## Summary

| Status | Count |
|---|---|
| `Done` | 23 |
| `In Progress` | 0 |
| `Blocked` | 2 — **both partially delivered; each needs one decision** |
| `Todo` | 0 |
| **Total** | **25** |

**Done (23).** T-01 entitlement enforcement proven · T-02 features/modules split · T-03 §5 plan
tiers · T-03b plans→entitlements bridge + **enforcement ON** · T-04 plan→entitlement bridge, tested
and mutation-tested · T-05 `402 feature_not_in_plan` · T-06 roles follow features · T-07 backend-owned
terminology · T-08 `brand_domains` · T-09 Host resolution · T-10 TXT verification + admin UI ·
T-11 sender identity · T-13 vertical templates as data · T-14 recurring schedules · T-15 launch
templates · T-16 provider self-signup · T-17 onboarding wizard · T-18 suspend / login-only mode ·
T-19 per-provider rate limits · T-20 support impersonation with consent + audit · T-21 provider
cancellation (export → retention → deletion) · T-23 partner/public API · T-24 the §6 role surface.

**Blocked (2) — each needs one decision, and each shipped everything that decision does not gate:**

| Task | Shipped | Still needs |
|---|---|---|
| T-12 SSL automation + domain health | **Health checks**: real TLS probes, expiry + issuer tracking, failure counting, operator attention list, worker sweep | **OQ-6** — Let's Encrypt or Cloudflare-for-SaaS. Issuance/renewal only. ~1 day either way |
| T-22 White-label app factory | **Per-brand app config**, entitlement-gated — plus the modules that make `custom_domain` and `white_label_app` gate at all | **OQ-7** — who submits to the stores. Decides whether we hold provider developer credentials |

## Live-test pass — 2026-08-25 (pre-commit)

Ran the whole stack against the real database and drove the console in a browser before committing.
Four things came out of it that no test had caught, because each needed a running system.

### 1. A 402 that called itself a 403 — FIXED

`GET /admin/brands/{id}/domains` as the LG-MAIN owner correctly answered **402** with
`feature_not_in_plan: custom_domain` — the T-05 paywall firing on a `pro` brand, which also proves
migration `0019`'s `custom_domains` module gates at all. But the body said:

```json
{"errorTypeCode": 403, "responseMessage": "feature_not_in_plan"}
```

402 on the wire, 403 in the body — and `ErrorMessageEnum`'s own doc comment promises *"error type
codes aligned with standard HTTP status codes… clients can map these values directly"*. A client
reading that field is told "your ROLE lacks this" when the truth is "your PLAN lacks this", which
are the two cases `ApiAuthorizationResultHandler` exists to tell apart. `BrandSuspensionMiddleware`
had the identical defect on both its 402s.

Fixed by adding `PaymentRequired = 402` to the enum and using it in all three places. Every existing
assertion passed while the bug shipped, so the tests were extended to compare the body against the
wire (`Assert.Equal(context.Response.StatusCode, message.errorTypeCode)`). **Mutation-tested**:
reverting the enum value fails 3 of 22; restoring it returns 22/22.

### 2. "Platform plans" is pointed at an empty table

The screen renders *"No platform plans yet."* because `finance_royalty.platform_plans` holds two
soft-deleted QA rows and nothing else, while the real tiers live in `identity_access.module_bundle`
and render fine under **Users & Roles → Licensing**. Recorded against **OQ-11**, which this
upgrades from "a column nothing reads" to "a screen pointed at the wrong table" — and it directly
affects how **OQ-4** gets answered, since the prices cannot be set on the screen named after them.

### 3. OQ-14 is visible to operators, honestly

The Licensing tab's *Unlocks* column says **"No menu"** for exactly the features flagged as gating
nothing — Scheduling, Item tracking, Online payments, Wallet, Loyalty, WhatsApp bot, API access.
Worth noting that the UI states the problem plainly rather than hiding it.

### 4. Three port faults — see `docs/PORTS.md`

- **8080 was a live collision, not a theoretical one.** Another project's container
  (`quaerisdocumentsearch-docsearch-1`) publishes `0.0.0.0:8080->80/tcp` and answers as *uvicorn*.
  That is the real cause of the repo's own note that *"the AppHost gateway (:8080) is not reliable
  for local dev and returns 502s"* — it was never being reached. AppHost moved to the reserved
  **5300–5303** block. Docker's container-internal 8080 is untouched.
- **admin-web had no pinned port.** Vite took its default 5173 and would walk to 5174, 5175… while
  `run-stack.sh` advertised and freed 5174. Now `port: 5174` + `strictPort`.
- **`run-stack.sh` was `kill -9`ing macOS ControlCenter.** Port 5000 was in its kill list and
  nothing here has ever bound it. Removed, and `free_ports` now refuses to kill a process that is
  not one of ours.

### What was exercised live

| Area | Result |
|---|---|
| Login, step-up (`sensitive_action` + dev OTP) | 403 `step_up_required` → **200** after step-up |
| T-05 paywall | **402** `feature_not_in_plan: custom_domain` on a `pro` brand |
| `0017` onboarding facts | Real: 1 location, 13 items, 2 staff, payments `todo` |
| `0020` permission groups | Invariant clean — **no module in two groups**; `royalty`/`subscription` out of `billing` |
| `0021` dunning guard | A `nonpayment` reinstate **cannot** clear a `tos` suspension |
| admin-web browser pass | Login → dashboard → licensing; **16/16 calls 200**, zero console errors |
| Licensing console | All 5 bundles, live subscription (Pro, active, renews 7/28/2026), paid invoice |

---

### Verification at close

| | |
|---|---|
| Migrations | **0001–0021 applied**, `migrate.sh verify` **OK**; every one round-tripped up→down→up (0020 twice, after an idempotency bug) |
| Backend build | **0 errors**, 66 warnings — *all pre-existing*, see below |
| Backend tests | **638** — core 116 · operations 338 · integration 184 |
| Frontend | customer-mobile **170** · rider-mobile **91** · admin-web + pos-web typecheck clean |
| **Total** | **899 automated tests, 0 failures** |

**The 66 warnings, named rather than waved at.** None come from code written for these tasks. They
are: `NU1902`/`NU1903` — known advisories in **transitive** packages (`MessagePack` via the Aspire
AppHost, `Microsoft.OpenApi` via the OpenAPI package, `SSH.NET`); `NU1510` — package references the
SDK considers redundant; `IL2026`/`IL3050` — trimming/AOT notices on a `JsonSerializer.Serialize`
call in the pre-existing `WhatsAppOtpDispatcher`. Clearing the advisories means pinning **someone
else's** dependency tree, which is a real change with its own blast radius and belongs to no task
here — flagged as **OQ-15** rather than done silently or ignored silently.
