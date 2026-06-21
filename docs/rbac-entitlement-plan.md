# Implementation plan — brand-module entitlement (the PaaS dimension)

Adds the missing `entitlement` axis so effective access = **entitlement ∩ authorization**.
Mirrors the reference's `tenant_module`, adapted to LaundryGhar (tenant = brand).

Grounded in the real schema: `identity_access.modules` (global, flat, each row carries a
`permission_modules` text[]), `identity_access.permissions` (each has a `module` tag),
`tenancy_org.brands`, and the token/nav flow in `ScopeResolver` + `GetNavigator`.

## 0. Key idea — the `permission_modules` bridge

A navigator module already maps to the permission "module" tags it covers:

```
orders     → {orders}
warehouse  → {warehouse, garment, qc, stockrecon, store_warehouse}
riders     → {rider, delivery, pickup}
```

So once we know **which navigator modules a brand has licensed**, we can derive both:
- **Nav**: show module iff licensed.
- **Permissions**: keep a permission iff its `module` tag belongs to a licensed module's
  `permission_modules` set.

One entitlement table drives both. No change to `HasPermission` (the token is filtered at
login), and a small filter added to `GetNavigator`.

## 1. Schema

```sql
-- The authoritative entitlement: which modules a brand has licensed (mirrors tenant_module).
CREATE TABLE identity_access.brand_module (
    brand_id    uuid    NOT NULL REFERENCES tenancy_org.brands(id) ON DELETE CASCADE,
    module_key  varchar NOT NULL REFERENCES identity_access.modules(key) ON DELETE CASCADE,
    enabled     boolean NOT NULL DEFAULT true,
    valid_until date,                                  -- NULL = perpetual
    source      varchar NOT NULL DEFAULT 'manual'      -- 'bundle' | 'manual'
                    CHECK (source IN ('bundle','manual')),
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (brand_id, module_key)
);
CREATE INDEX ix_brand_module_brand ON identity_access.brand_module (brand_id) WHERE enabled;

-- Always-on modules bypass entitlement (admin tooling a brand can never "unbuy").
ALTER TABLE identity_access.modules ADD COLUMN is_core boolean NOT NULL DEFAULT false;
-- seed: UPDATE ... SET is_core = true WHERE key IN ('dashboard','settings','users');

-- Plan→module bundles: a catalog used to EXPAND into brand_module rows at
-- onboarding / plan change. NOT joined on the hot path.
CREATE TABLE identity_access.module_bundle (
    code        varchar PRIMARY KEY,                   -- 'starter','pro','enterprise'
    name        varchar NOT NULL,
    description text
);
CREATE TABLE identity_access.module_bundle_item (
    bundle_code varchar NOT NULL REFERENCES identity_access.module_bundle(code) ON DELETE CASCADE,
    module_key  varchar NOT NULL REFERENCES identity_access.modules(key) ON DELETE CASCADE,
    PRIMARY KEY (bundle_code, module_key)
);
```

RLS — use the repo's existing helpers (`kernel.rls_bypass()` / `kernel.current_brand_id()`),
the same shape every other brand-scoped table uses:

```sql
ALTER TABLE identity_access.brand_module ENABLE ROW LEVEL SECURITY;
CREATE POLICY rls_brand ON identity_access.brand_module
    USING (kernel.rls_bypass() OR (brand_id = kernel.current_brand_id()));
GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.brand_module TO app_user, app_admin;
-- module_bundle / module_bundle_item are platform-global catalogs → no RLS (like `modules`).
```

> **Phase 1 status: DONE.** Shipped as `db/patches/brand_module_entitlement.sql`
> (tables + `is_core` + bundles + RLS + backfill), applied & verified idempotent.
> The resolver intersections below (Phases 2–3) are the remaining work, gated behind
> the `entitlement.enforced` feature flag.

## 2. Resolution changes

### 2a. Bake entitlement into the token — `ScopeResolver.BuildTokenClaimsAsync`

After the permission set is collected and `brandId` is resolved, intersect with the
brand's licensed modules. Platform admins are exempt (cross-brand operators).

```csharp
// after: foreach (var rp in activeMembership.Role.RolePermissions) permissions.Add(rp.Permission.Code);
// and after brandId is resolved from scope:

if (brandId is { } bid && user.UserType != UserType.PlatformAdmin)
{
    // licensed (or core) modules → their permission_modules tags
    var entitledTags = await (
        from m in db.Modules.AsNoTracking()
        where m.Status == "active" && (
            m.IsCore ||
            db.BrandModules.Any(bm => bm.BrandId == bid && bm.ModuleKey == m.Key
                && bm.Enabled && (bm.ValidUntil == null || bm.ValidUntil >= today)))
        select m.PermissionModules            // text[]
    ).ToListAsync(ct);

    var tagSet = entitledTags.SelectMany(t => t).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // permission.Module is the tag; keep only permissions whose module is entitled.
    var permByCode = await db.Permissions.AsNoTracking()
        .Where(p => permissions.Contains(p.Code))
        .Select(p => new { p.Code, p.Module }).ToListAsync(ct);
    permissions = permByCode.Where(p => tagSet.Contains(p.Module))
                            .Select(p => p.Code)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
```

Effect: an unlicensed module's permissions never reach the token → **API endpoints reject
them automatically** (they already check `HasPermission`). No hot-path change.

### 2b. Filter the menu — `GetNavigator.HandleAsync`

Add a brand-entitlement clause to the visibility filter. Resolve the active brand from the
JWT `brand_id` or the platform-admin's `X-Brand-Id` override.

```csharp
var brandId = _user.BrandId ?? _user.ActiveBrandIdOverride;   // override = X-Brand-Id (Items["brand_id_override"])
var entitled = brandId is { } bid
    ? await _db.BrandModules.Where(bm => bm.BrandId == bid && bm.Enabled
            && (bm.ValidUntil == null || bm.ValidUntil >= today))
        .Select(bm => bm.ModuleKey).ToHashSetAsync(ct)
    : null;   // no brand context (pure platform view) → don't entitlement-gate

var visible = mods.Where(m =>
    (m.IsCore || entitled == null || entitled.Contains(m.Key))          // ← entitlement
    && (string.IsNullOrEmpty(m.RequiredPermission)
        || _user.IsPlatformAdmin
        || _user.HasPermission(m.RequiredPermission)));                  // existing authorization
```

## 3. Plans → bundles → brand_module

Keep the existing billing tables (`finance_royalty.platform_plans`,
`commerce.subscription_plans`) for **money/quotas**; they are NOT the module catalog.
Map a plan tier to a `module_bundle` and expand at provisioning / plan change:

```
platform_plans.tier  ──maps to──▶  module_bundle.code  ──expands to──▶  brand_module rows
   (starter/pro/ent)                  (bundle items)        (source='bundle')
```

- Add an optional `module_bundle_code` column to `platform_plans` (the seam), or keep a
  static `tier → bundle` map in the apply-command.
- Command `ApplyBundleToBrand(brandId, bundleCode)`:
  delete `source='bundle'` rows for the brand, insert the bundle's items as
  `brand_module(enabled=true, source='bundle')`. Manual rows (`source='manual'`) are
  preserved — they are per-brand add-ons/exceptions on top of the plan.

Effective entitlement = `bundle rows ∪ manual(enabled) − manual(disabled)`.

## 4. Migration & backfill (no breakage)

1. Create tables + `is_core` column; seed `is_core` for `dashboard, settings, users`.
2. Seed `module_bundle` (`starter/pro/enterprise`) + `module_bundle_item`.
3. **Backfill existing brand(s):** insert `brand_module(enabled=true, source='manual')`
   for every active non-core module, for every existing brand — so current behaviour is
   unchanged on day one.
4. Ship resolver changes behind a flag (`kernel.feature_flags`: `entitlement.enforced`)
   so nav/token filtering can be toggled per brand during rollout.

## 5. Admin surface

| Command | Effect |
|---|---|
| `SetBrandModule(brandId, moduleKey, enabled, validUntil)` | upsert a `manual` `brand_module` row |
| `ApplyBundleToBrand(brandId, bundleCode)` | expand a bundle into `bundle` rows |
| `UpsertBundle / UpsertBundleItem` | edit the platform catalog |

- Endpoints under `/api/v1/admin/entitlements` (platform-admin only).
- UI: a **"Modules / Plan"** tab on the brand (Franchises/brand admin area) — toggle
  modules, show which came from the plan vs manual. Menu hiding is UX; the token filter +
  endpoint checks are the real enforcement.

## 6. Decisions / edge cases

- **Platform admin is exempt** from entitlement in the token (cross-brand). In nav they
  see the active brand's entitlement when an `X-Brand-Id` is set, else the full catalog.
- **Core modules** (`dashboard, settings, users`) always show — a brand can't lock its
  own admins out.
- **Expiry**: `valid_until < today` ⇒ treated as not entitled.
- **Freshness**: entitlement is baked into the token at login (changes apply on
  re-login). Pair with the `perm_version` work (P0 #3 in the gap doc) for near-real-time
  plan changes; entitlement changes are rare so this is acceptable interim.
- **`HasPermission` unchanged** → all existing endpoint authz automatically respects
  entitlement once tokens are filtered.

## 7. Rollout phases

1. **DONE** — Schema + backfill + `is_core` seed (`db/patches/brand_module_entitlement.sql`).
2. **DONE** — `GetNavigator` entitlement filter behind the flag. EF entity `BrandModule`
   + `AppModule.IsCore` + `ICurrentUser.TryGetBrandId()` added; all hosts build green.
3. **DONE** — `ScopeResolver` token filter behind the same flag. The 3 staff auth
   handlers (PasswordLogin, OtpVerify, RefreshToken) pass
   `enforceEntitlement: config["Entitlement:Enforced"]`; customer logins use a separate
   path and are untouched. Verified at the data layer (see caveat below); identity host
   builds green. Runs on the `bypass_rls` auth path, so the `brand_module` read returns
   rows and the explicit brand filter keeps it correct.
4. **DONE** — Admin surface. Bundle entities (`ModuleBundle`/`ModuleBundleItem`),
   commands `SetBrandModule` + `ApplyBundleToBrand`, queries `GetBrandEntitlements` +
   `GetModuleBundles`, endpoint group `AdminEntitlements`
   (`/api/v1/admin/entitlements`, gated `saas.read`/`saas.manage`), and a **Modules**
   tab in the admin-web Access Control page (`EntitlementsTab`, toggles + apply-plan).
   Verified live against a standalone build on :5056: bundles `starter(7)/pro(15)/
   enterprise(18)`, toggle royalty off→21/22→on→22/22, apply-bundle preserves manual
   rows. All hosts build green; admin-web typechecks.
5. Flip the flag per brand; then default-on.

### ⚠️ Caveat — permission tags are shared across modules

A permission's `module` tag can belong to **several** navigator modules'
`permission_modules`. Observed overlaps:

- `orders` tag ∈ both **orders** and **pos** modules.
- `analytics` tag ∈ both **analytics** and **dashboard** (and dashboard is core).

Consequence: unlicensing a single module does **not** revoke a permission still covered
by a sibling module. Verified live — dropping `orders` alone kept all `orders.*`
(covered by `pos`); dropping `orders`+`pos` together dropped all 8 `orders.*`; dropping
`riders` (unique tags) cleanly dropped 13 `rider/delivery/pickup.*` permissions.

This is defensible (POS genuinely needs order permissions), but **bundle authors must
unlicense tag-sharing modules together** to fully revoke a capability. A future cleanup
(gap doc #4 — single canonical permission registry) would remove the overlap ambiguity.

### The flag

Phase 2 gates on config key **`Entitlement:Enforced`** (read via `IConfiguration`),
default **false** → no behaviour change until enabled. Turn on with an env var:

```
Entitlement__Enforced=true
```

(Interim mechanism; can be promoted to a per-brand `kernel.feature_flags` lookup later.)
With it off, `GetNavigator` behaves exactly as before. With it on, a module shows only if
it is `is_core` or licensed for the active brand (resolved via `TryGetBrandId()` =
X-Brand-Id override ?? JWT `brand_id`); no brand context ⇒ entitlement not applied.
