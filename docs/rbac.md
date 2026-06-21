# RBAC, permissions & privileged navigation

How access control works today in LaundryGhar, the weak points we hit in practice,
and the recommended direction. Grounded in the actual code paths.

## How it works today

1. **Login** (`PasswordLoginHandler`) → `ScopeResolver.BuildTokenClaimsAsync` walks
   `User → UserScopeMembership → Role → RolePermission → Permission` and stamps the
   permission codes into the JWT as a space-separated `permissions` claim, plus a
   tenant context (`brand_id` / `franchise_id` / `store_id`) derived from the active
   membership's scope.
2. **Per request** `HttpContextCurrentUser.HasPermission(code)` reads the `permissions`
   claim from the JWT (no DB round-trip). `IsPlatformAdmin` (`user_type=platform_admin`)
   bypasses every check.
3. **Navigation** is server-driven: `GetNavigator` returns only modules whose
   `Module.RequiredPermission` is null, or that the user holds (or platform admin).
4. **Tenant-scoped services** (orders, warehouse, …) call `ICurrentUser.RequireBrandId()`,
   which throws `UnauthorizedAccessException` (→ 401) when the token has no `brand_id`.

The matrix UI uses `module:action` cell keys (colon); permissions are stored as
`module.action` codes (dot). `SetRoleCell` + `PermissionMatrix.Module/Action` translate
between them, and a single `view` cell can fan out to several read codes
(e.g. Orders view → `orders.read` + `orders.list` + `orders.notes.manage`).

## Weak points (observed)

| # | Problem | Symptom we hit |
|---|---------|----------------|
| 1 | Permissions are baked into the JWT at login; no live revocation | Grants/revokes don't apply until re-login |
| 2 | `ScopeResolver` uses only ONE membership (primary/first) | A user with multiple roles loses the others' permissions |
| 3 | Brand-scoped membership could be created with `scope_id = NULL` | Token had no `brand_id` → Orders API returned 401 |
| 4 | Two representations of a permission (`orders:view` vs `orders.list`) | Implicit, drift-prone mapping between matrix and nav |
| 5 | A system role shipped with zero permissions | `regional_manager` saw only Dashboard + CMS |
| 6 | Self-service footguns not enforced server-side | A user could change their own role |

## Fixes applied

- **#3** Frontend (`PersonDetailDrawer`, `InviteUserModal`) now sends the active brand
  id for brand-scoped roles; backend `GrantMembership` backstops a null brand scope
  with the actor's `brand_id` and rejects if neither is available.
- **#5** `IdentitySeeder` now grants `regional_manager` a sensible default
  (operational oversight + read-only finance/analytics + team visibility). The seeder
  is idempotent, so it backfills existing databases on next boot.
- **#6** `ChangePrimaryRole` rejects `ActorId == UserId`; the drawer also hides the
  control. Enforced server-side so it can't be bypassed.
- **Refresh** The `lg_refresh` cookie `Path` was corrected to include the gateway
  `/identity` prefix so the browser actually sends it on silent refresh.

## Recommended next steps

1. **Live revocation (#1).** Add a `perm_version` (epoch int) to the user row, stamp it
   into the token, bump it on any role/permission change, and have services reject
   tokens carrying a stale version (forcing a silent refresh). Near-real-time revocation
   with no per-request DB lookup.
2. **Union multiple memberships (#2).** Aggregate permissions across all active,
   non-revoked memberships rather than a single primary; resolve tenant scope
   explicitly or via a scope-switcher.
3. **Single permission registry (#4).** One canonical list of permission codes as the
   source of truth; derive both the matrix cells and each module's `RequiredPermission`
   from it. Add a startup/test assertion that every `Module.RequiredPermission` is
   grantable by some matrix cell, and that no system role is empty.
4. **Unify nav + API gating.** Feed `GetNavigator` and endpoint authorization from the
   same registry so a module never appears that the API will then 401/403.
5. **Extend self-service guards.** Apply the same server-side guard to self-suspend /
   self-deactivate that we applied to self-role-change.
