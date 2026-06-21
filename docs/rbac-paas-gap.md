# RBAC / Navigation as a PaaS — gap analysis

Comparing the reference architecture (`RBAC_Navigation_PaaS_PostgreSQL.md`) against
LaundryGhar today, treating the project as a multi-tenant PaaS.

## Conceptual mapping

| Reference concept | LaundryGhar equivalent |
|---|---|
| `tenant` (one org) | `tenancy_org.brands` (a brand is the tenant) |
| single-level tenant | **deeper**: platform → brand → franchise → store/warehouse (`user_scope_memberships.scope_type`) |
| `privilege` (module-scoped) | `identity_access.permissions` (code = `module.action`) |
| `role` + `role_privilege` | `roles` + `role_permissions` |
| `user_role` | `user_scope_memberships` (richer — carries scope) |
| `navigation` (tree) | `identity_access.modules` (flat) |
| `tenant_module` (entitlement) | **— none —** |
| `user_nav_override` | **— none —** |
| Redis + `perm_hash` + LISTEN/NOTIFY | **— none —** (permissions baked into JWT) |

LaundryGhar's scope tree and role-priority checks are actually **stronger** than the
reference. The gaps are on the *platform* axis: entitlement, data-driven menu, and
cache/invalidation.

## What's already strong (keep)

- **RLS tenant isolation** — verified: `app_user` sees zero rows without an
  `app.current_tenant` GUC; every `identity_access` / `tenancy_org` table has policies.
  This matches the reference's core isolation guarantee.
- **Server-driven navigation** — `GetNavigator` returns only permitted modules; menu
  hiding is UX and endpoints still enforce. Matches the reference's stance.
- **Role priority / rank guard on grant** — `GrantMembership` blocks granting a role
  higher than your own. The reference doesn't even model this.

## Gaps (reference → current)

### P0 — platform-defining

1. **No entitlement dimension (`tenant_module`).**
   Reference effective access = `entitlement ∩ authorization`. LaundryGhar has only
   authorization — `modules` is a **global** catalog with no `brand_id`, so every brand
   sees the same feature set. `platform_plans` encodes *quotas* (`max_stores`,
   `max_users`, …) not *module licensing*, and `kernel.feature_flags` is a separate
   rollout mechanism not wired into nav/permission resolution.
   → Add `brand_module` (brand_id, module_id/key, enabled, valid_until) and intersect it
   in both `GetNavigator` and `HasPermission`. Without this you cannot sell
   "Brand A gets Analytics + Royalty, Brand B doesn't" as data.

2. **Hot path doesn't intersect entitlement.**
   Reference `has_privilege` joins `tenant_module` so an unlicensed module's privileges
   are invisible. `HttpContextCurrentUser.HasPermission` only checks the JWT claim. Even
   after adding entitlement, the check must intersect it (or bake the intersection into
   the token at login).

3. **Stale permissions / no live revocation.** ✅ **DONE.**
   Reference: resolve once → Redis (`authz:{tenant}:{user}`) keyed by `perm_hash` →
   `LISTEN/NOTIFY` triggers evict on any grant change. LaundryGhar bakes permissions into
   the JWT at login with no invalidation, so grants/revokes applied only on re-login (the
   exact problem hit this session).
   **Implemented:** `users.perm_version` (SQL patch `user_perm_version.sql` + SECURITY
   DEFINER reader `kernel.user_perm_version`), stamped into the JWT (`perm_ver` claim),
   bumped on every effective-permission change (`PermVersionBumper` in GrantMembership /
   RevokeMembership / SetRoleCell / SetBrandModule / ApplyBundleToBrand). Enforced in the
   shared `TenantResolutionMiddleware` (gated by `Auth:EnforceTokenVersion`, fail-open,
   15s in-process cache via `ITokenVersionStore`): a token with a stale `perm_ver` → 401 →
   the existing axios interceptor silently refreshes → fresh token. Verified live: change a
   permission → old token 401 within the cache window → re-login restores access with the
   updated set. A Redis cache + `pg_notify` would remove the ≤15s cache-TTL latency at
   multi-instance scale, but isn't required at current scale.

### P1 — flexibility / fewer roles

4. **No allow/deny effect → role explosion.** ✅ **DONE.**
   `role_permissions.effect` ('allow'|'deny') + a new `user_permission_override` table
   (patch `permission_overrides.sql`, RLS user-self) give "broad grant, precise exception"
   without a new role. `ScopeResolver` resolves deny-wins:
   `effective = (role-allowed − role-denied ∪ user-allow) − user-deny`, layered on the
   same-scope membership union. Admin endpoint `POST /access-control/people/{id}/
   permission-override` (gated `permissions.assign`) sets/clears an override and bumps
   `perm_version` (live revocation). Verified live: deny `orders.cancel` removed it
   (31→30) despite the role granting it; allow `royalty.read` added it though no role does;
   clearing both restored the baseline.

5. **Single-membership resolution.** ✅ **DONE.**
   `ScopeResolver` now unions permissions across **all active memberships at the same
   scope** as the active one (a user holding Operations Manager + Finance Manager at one
   brand gets both), deduped by code. Same-scope only, so no cross-brand leakage. Verified:
   ops(31) ∪ finance(12) = 42 distinct. (Cross-scope roles still require a scope switch —
   the token carries one tenant context.)

6. **Two permission representations / tag overlap.** ✅ **DONE (entitlement side).**
   Added `permissions.module_key` (patch `permission_canonical_module.sql`): each permission
   now has a SINGLE canonical owning module (deterministic backfill — exact key match wins,
   else dedicated module over aggregator). Entitlement (the `ScopeResolver` token filter)
   keys off `module_key`, so the tag-overlap ambiguity is gone and the latent orphan is
   fixed — `pos.*` was owned by no module (the `pos` module's `permission_modules` is
   `{orders}`), so under entitlement it would have been wrongly dropped; it's now owned by
   `pos`. **Verified live:** disabling only `orders` (with `pos` still licensed) now drops
   all 8 `orders.*` (was kept before via the overlap). 136/137 permissions owned; 1 orphan
   (`dispatch.mode.manage`) → always allowed (fail-open) and logged by the validator.
   The matrix UI keeps using `permission_modules` (a permission can still appear under
   several matrix rows by design — unchanged). Collapsing the colon/dot *matrix-cell*
   representation itself is cosmetic/internal and intentionally left as-is.
   **`IdentitySeeder` now auto-assigns `module_key`** to any unowned permission at boot
   (same logic as the patch), so newly added permissions are owned without re-running the
   SQL — verified by nulling a permission and confirming the seeder reassigned it.

### P2 — richer UI surface

7. **Navigation is flat, not a tree, and global, not per-tenant.**
   Reference `navigation` is a self-referencing tree (`parent_id`, recursive CTE,
   container nodes, "walk up to parents") with `tenant_id` (NULL = platform default,
   non-null = per-tenant addition/override) and `portal_type` (web/mobile/admin/partner).
   LaundryGhar `modules` is flat (`section` + `nav_order`), global, single-portal.
   → For nested menus / per-brand white-labelled nav / multiple portals from one
   codebase, adopt the tree + `tenant_id` + `portal_type` shape.

8. **`required_permission` is a single code.**
   Reference gates each leaf by `privilege_id` and supports container nodes. A module that
   should show for "any of N permissions" can't be expressed cleanly today.

## Suggested roadmap

1. **`brand_module` entitlement** + intersect in nav and (token-baked) permission set — this is the single change that turns the app into a PaaS.
2. **`perm_version`** for live revocation (small, high value; pairs with this session's grant fixes).
3. **allow/deny + per-user overrides** to stop role explosion.
4. **Union memberships** + **canonical permission registry**.
5. **Navigation tree + `tenant_id` + `portal_type`** when nested/white-labelled menus are needed.

RLS, scope hierarchy, and server-driven nav are already in place — the work is additive,
not a rewrite.
