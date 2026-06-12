---
name: project-rbac-seeder-drift
description: IdentitySeeder has drifted from live DB — 8 permission codes exist only in SQL patches, not in the seeder. Fresh bootstraps are missing them.
metadata:
  type: project
---

## IdentitySeeder ↔ live-DB permission drift (found 2026-06-12 audit)

`IdentitySeeder.PermissionDefs` is the source of truth for fresh dev/CI/staging
bootstraps, but it is **add-only** and has drifted. Eight permission codes are
**enforced by endpoints** (`RequireAuthorization("permission:<code>")`) yet are
**not in the seeder** — they exist only in `db/patches/*.sql`:

- `payment.record`, `customer.create` → `db/patches/pos_permissions.sql`
  (granted to platform_admin, brand_admin, franchise_owner, store_admin)
- `rider.verify` → `db/patches/rider_verify_permission.sql`
- `rider.settle` → `db/patches/rider_cod_settlement.sql`
- `subscription.read`, `subscription.manage` → `db/patches/subscriptions_module.sql` / `seed_subscriptions_modules.sql`
- `saas.read`, `saas.manage` → finance SaaS endpoints (FinanceEndpoints.cs:229-286);
  patch source to confirm

**Why it matters:** on any environment built from seeder + EF migrations WITHOUT
replaying the SQL patches, these codes don't exist as permission rows, so they can
be satisfied ONLY by `platform_admin` (which bypasses every check). Non-platform
roles silently lose POS payment recording, rider KYC verify, COD settle,
subscription admin, SaaS finance. Forces over-use of the one bypass-everything role.

**How to apply:** when reviewing any RBAC/endpoint PR, diff endpoint
`permission:<code>` strings against `IdentitySeeder.PermissionDefs`. The canonical
check: `grep -rhoE 'permission:[a-z_]+\.[a-z_.]+' backend --include=*.cs | sort -u`
vs the seeded set. Recommend folding all patch-defined codes+grants back into the
seeder so fresh bootstraps match live. Related: [[project-security-patterns]].

## POS ↔ Orders permission coupling (user wants separated)

`payment.record` IS a separate family (good — POS offline payment lockable
independently). BUT POS order *creation/mutation* still uses `orders.create` /
`orders.update`, shared with the admin Orders module — so POS cannot be locked
independently of admin Orders for order ops. User has explicitly asked to fix this;
needs a distinct `pos.*` family (e.g. pos.order.create / pos.payment.record) gating
the POS endpoints. Trailing-dot artifact `rider.read.` also appeared in the scan —
verify LogisticsEndpoints proof-photo route string isn't malformed.

## Client-side route gating gap (admin-web)

`admin-web ProtectedRoute` is **auth-only** (token presence + exp refresh), NOT
permission-aware — no per-route `requiredPermission` check. Any authenticated admin
can mount any route by URL; the navigator only hides the nav item server-side
(GetNavigatorHandler). Real gate is per-endpoint RequireAuthorization (sound), so
this is UX/info-disclosure (opaque 403s), not a privilege bypass. = R2 WEB-3.
pos-web usePermissions exists but is used in only 2 POS modals, not on routes.
