---
name: project-subscriptions-admin-ui
description: Subscriptions + Platform-plans admin UI (Task #36) — pages, contracts, JSON-string field UX, navigator seed
metadata:
  type: project
---

Task #36 shipped admin UI for the subscriptions backend (ADR-010, two modules).

**Contracts consumed** (all under `/api/v1/admin`, brand-scoped via X-Brand-Id):
- Commerce (port 5005): `subscription-plans` CRUD (perm `subscription.manage`), `subscriptions` read-only list (perm `subscription.read`). No customer-subscription detail-by-id or invoices list endpoint exists — the admin list DTO is all we get.
- Finance (port 5006): `platform-plans` CRUD + `franchise-subscriptions` (list / `assign` / `{id}/cancel`), perms `saas.read` / `saas.manage` (platform_admin only).

**JSON-string fields are server-validated → 422 on bad input** (verified live):
- `subscription-plans.nameLocalized` MUST be a JSON-OBJECT string. UI never shows raw JSON: two friendly inputs (en, hi) serialized to `{"en":…,"hi":…}` on submit, parsed back on edit. See [[project-admin-web-scaffold]].
- `platform-plans.features` is ALSO a validated JSON-object string (422 otherwise) — exposed as a validated `<textarea>` (zod refine rejects non-objects pre-flight).

**422 envelope:** `response.data.message.errorMessage` is `{ "Request.Field": ["msg"] }`. Axios rejects raw, so `e.message` is generic. Added `src/lib/apiError.ts` `apiErrorMessage(e, fallback)` to flatten `errorMessage` → the validator text for inline display. The rest of the codebase still uses `e instanceof Error ? e.message` (generic) — prefer apiErrorMessage for new drawers needing field-level 422 text.

**Publish/archive** reuse the PUT update endpoint (UpdateRequest has no partial semantics) — re-send all current fields with the new `status`. Plan delete is blocked server-side when `currentSubscriberCount > 0` (retire instead).

**Navigator seed:** `db/patches/seed_subscriptions_modules.sql` (mirrors seed_royalty_module.sql) adds `subscriptions` (Catalogue, /subscriptions, gate subscription.read) + `platform_plans` (Finance, /platform-plans, gate saas.read). Applied. Added `CreditCard` + `Layers` to the Sidebar ICONS map.

Files: `pages/subscriptions/{SubscriptionsPage,SubscriptionDrawers}.tsx`, `pages/finance/{PlatformPlansPage,PlatformPlanDrawers}.tsx`, `api/subscriptions.ts` (+ finance.ts additions), `hooks/useSubscriptions.ts` (+ useFinance.ts additions).
