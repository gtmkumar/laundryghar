---
name: project-rbac-admin-ux
description: Issue #14 RBAC admin-UX — per-user permission overrides + additive memberships panels in admin-web PersonDetailDrawer; backend read-endpoint gaps
metadata:
  type: project
---

Issue #14 (admin-web): wired two per-user access-control panels into `PersonDetailDrawer` — per-user permission overrides and additive multi-scope memberships (grant/revoke), on top of the existing single-primary change-role flow.

**Why:** operationalizes docs/rbac.md §6 (additive memberships) and §7 (suspend a capability for one user). Backend endpoints already existed and are permission-gated; this was UI + api-client + hooks only.

**Endpoint gotcha (non-obvious):** the two capabilities sit on DIFFERENT route bases —
- overrides: `POST /api/v1/admin/access-control/people/{id}/permission-override` (gated `permissions.assign`), body camelCase `{permissionCode, effect: "allow"|"deny"|null, scopeType?, scopeId?, reason?, expiresAt?}`.
- memberships: `POST /api/v1/admin/roles/memberships/{grant,revoke}` (gated `memberships.grant` / `memberships.revoke`) — NOTE `admin/roles`, not `admin/access-control`. Grant body `{userId, scopeType, scopeId?, roleId, isPrimary?}` (NO expiresAt/reason on grant despite issue text). Revoke `{membershipId, reason?}`. Grant returns MembershipDto (its id is needed to revoke).
- permission catalog picker: `GET /api/v1/admin/roles/permissions` → PermissionDto[] {id,code,module,action,name,riskLevel}.

**Backend follow-up (gap this UI works around):** there is NO GET endpoint to LIST a user's current permission-overrides or memberships (user-detail `UserDto`/`AdminUserDetail` carries neither). So the overrides panel is add/clear-only, and the memberships panel can only revoke memberships granted in the same drawer session. A future "list a person's overrides/memberships" query would let both panels show current state.

**How to apply:** if extending these panels or building a read view, expect to add backend queries first. Membership scope-id derivation mirrors the change-role flow (platform→platform/null; brand→effectiveBrandId; else→franchise + picked franchise id).

**i18n note:** the whole access-control feature uses hardcoded English strings (no `useTranslation`; en.json has no access-control keys — only ~9 files app-wide use i18n). New panels followed that local convention rather than retrofitting partial i18n. See [[project-admin-web-patterns]].

**Skipped:** optional deny/allow tri-state on the RolesTab/RoleFormModal permission matrix — the role cells API (`RoleCellChange {cellKey, enabled}`, `AccessRoles.onCells` flat set) models allow-only; deny at role level would need a backend contract + storage change (out of scope, backend untouched).
