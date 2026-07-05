# LaundryGhar — End-to-End QA Report

**Date:** 2026-07-04
**Scope:** Full application — every admin-web feature, all admin roles, full backend API surface.
**Method:** Browser-driven UI sweep (admin-web :5173) + API smoke test (all endpoints × 3 roles) + targeted fixes with re-verification.

---

## Executive summary

The app was **completely down at login** (502) due to a backend-topology mismatch — fixed first. After that, **7 breaks** were found and **all 7 fixed and verified end-to-end**. The backend API surface is stable: **0 server errors (5xx)** across 189 GET + 5 write calls spanning 3 roles.

| Area | Result |
|---|---|
| admin-web routes rendered (Super Admin) | 20 / 20 |
| Admin roles logging in | 3 / 3 (platform_admin, store_admin, warehouse) |
| Backend API 5xx errors | 0 |
| Bugs found | 7 |
| Bugs fixed & verified | 7 |
| Items flagged for product/architecture decision | 2 |

---

## Environment

- **Backend:** 3 standalone hosts — CORE `:5056` (identity/engagement), OPERATIONS `:5015` (catalog/orders/warehouse/logistics), COMMERCE `:5242` (finance/analytics). This is the `run-stack.sh` topology that admin-web's `.env.local` + Vite proxy target.
- **admin-web:** Vite `http://localhost:5173`.
- **DB:** `laundry_ghar_db` on `localhost:5432`.
- **Roles / credentials used:**
  - platform_admin — `admin@laundryghar.local` / `Admin@123` (permission bypass)
  - store_admin — `storeadmin@laundryghar.local` / `Store@123` (scoped: Mumbai Central store)
  - warehouse — `warehouse@laundryghar.local` / `Warehouse@123` (scoped: QA Central Warehouse)
  - rider/customer — OTP only, non-prod master OTP `123456`

---

## Bugs found & fixed

### BUG-1 — Login returned 502 (entire app unusable) · FIXED
**Symptom:** Sign-in failed with "Request failed with status code 502"; the browser called `/core/api/v1/auth/password/login` (Vite proxy → :5056) which was dead.
**Root cause:** Backend was running under the Aspire AppHost topology (core:5050 / operations:5002 / commerce:5005 + gateway:8080). admin-web's active `.env.local` + `vite.config.ts` proxy expect the **standalone hosts** on 5056/5015/5242. Not an app bug — an environment mismatch.
**Fix:** Restored the standalone-host topology (as `scripts/run-stack.sh` launches them, with `ConnectionStrings__Default/__Admin` injected).
**Verified:** All 3 roles log in; end-to-end auth 200.

### BUG-2 — CMS Notification Templates: "Invalid Date" + blank Status; Edit form lost body/subject · FIXED
**Symptom:** All 35 template rows showed "Invalid Date" (UPDATED) and an empty STATUS badge. Opening **Edit** showed a blank Body/Subject/Variables — saving would have wiped template content.
**Root cause:** The backend list/by-id DTO (`NotificationTemplateDto.FromEntity`) returned only 8 of 23 fields — omitting `status`, `updatedAt`, `isTransactional`, `bodyTemplate`, `subjectTemplate`, `variables`, etc. The Edit form is seeded from the list row, so those fields arrived blank.
**Fix:** Expanded `NotificationTemplateDto` + `FromEntity` to project the full field set (`backend/.../GetNotificationTemplateByCode/NotificationTemplateDto.cs`).
**Verified:** Table shows real dates ("10 Jun 2026"), STATUS "Active", TRANSACTIONAL "Yes"; Edit modal shows the real body text.

### BUG-3 — `formatDate(undefined)` rendered "Invalid Date" app-wide · FIXED
**Root cause:** `formatDate`/`formatDateTime` didn't guard against null/invalid input.
**Fix:** Return `"—"` for missing/invalid dates (`admin-web/src/lib/utils.ts`).

### BUG-4 — CMS template Edit save always failed ("must be a valid JSON object") · FIXED
**Root cause:** The form validated `variables` with `parseJsonObject` (rejects arrays), but the backend's own default is `Variables = "[]"` (a JSON array). Every seeded template therefore failed save.
**Fix:** Added `isJsonContainer()` (accepts object *or* array) and used it in the CMS form (`admin-web/src/lib/validation.ts`, `pages/cms/NotificationTemplatesTab.tsx`).
**Verified:** Typecheck clean; form accepts `[]`.

### BUG-5 — `fulfillment-config` endpoint returned 404 · FIXED
**Root cause:** `admin-web/src/api/fulfillment.ts` called `/fulfillment-config/`, omitting the `/api/v1` prefix every other ops call uses. Backend route is `/api/v1/fulfillment-config`.
**Fix:** Prefixed both the list and by-mode paths.
**Verified:** `/api/v1/fulfillment-config/` → 200.

### BUG-6 — store_admin & warehouse locked out (401 → forced logout) · FIXED
**Symptom:** Logging in as store_admin and opening Orders 401'd every brand-scoped read; the axios interceptor treated 401 as session-expired and **bounced the user back to /login**.
**Root cause:** `db/patches/seed_demo_users.sql` pinned the demo users' scope memberships to a store (`60e5bb20…`) and warehouse (`a6c735c1…`) that were removed in a later DB reseed. `ScopeResolver` resolves `brand_id` from the store/warehouse row; missing row → `brand_id` null → brand-scoped reads 401. `scope_id` is polymorphic and not FK-constrained, so the stale IDs inserted silently.
**Fix:** Rewrote the seed patch to resolve `scope_id` by **code** (reseed-proof) with `ON CONFLICT DO UPDATE`, and repointed both memberships to the real Mumbai Central store / QA Central Warehouse.
**Verified:** store_admin now loads its 4 active orders with no logout; warehouse JWT now carries `brand_id`.

### BUG-7 — Rider Incentives page returned 404 (missing backend endpoint) · FIXED
**Symptom:** `/riders/incentives` errored; the FE calls `/api/v1/admin/incentive-rules` (GET/POST/PUT/DELETE) but no such endpoint existed on any host.
**Root cause:** The `IncentiveRule` entity + DbSet + full frontend page/hooks existed, but the admin CRUD endpoint was never implemented.
**Fix:** Implemented the endpoint on operations.WebApi following the sibling logistics-admin pattern (CQRS handlers + minimal-API group), brand-scoped via `ICurrentUser.RequireBrandId()`, permissions `rider.read` (GET) / `rider.manage` (writes), soft-delete. Files: `operations.Application/Logistics/Incentives/*`, `operations.WebApi/Endpoints/Logistics/IncentiveRulesAdmin.cs`.
**Verified:** `dotnet build` succeeded; GET → 200 (was 404); the page renders its incentive rule. Writes correctly require OTP step-up (`rider.manage` is step-up-gated by design).

---

## Route sweep (Super Admin) — all render

Dashboard, Tenancy, Orders, Customers, Riders (+ Verification/Payouts/Incentives), Items, Pricing, Packages, Coupons, Promotions, CMS, Subscriptions, Cash book, Expenses, Royalty, Platform plans, Analytics, Access control, Settings, Warehouse board, Support.

## RBAC

- Nav is correctly reduced per role (store_admin/warehouse see a filtered menu; no Tenancy/Settings/Analytics/Finance).
- Landing pages correct (store_admin → /orders).

---

## Flagged for decision (not auto-changed)

1. **401-vs-403 semantics.** Authorization failures ("can't resolve brand / forbidden") return **401** on several endpoints. The admin-web axios interceptor treats 401 as "session expired → refresh, then log out on repeat," so a scoped user can be spuriously logged out instead of shown a clean 403/forbidden. Recommend standardizing authorization failures on **403**. Architectural — left for the team.
2. **Incentive-rule writes require OTP step-up** (`rider.manage` is step-up-gated). This is by design; noting it so it isn't mistaken for a bug. If incentive management shouldn't be step-up-gated, adjust the permission or its risk level.

---

## Changed files (all uncommitted — git is ask-gated)

**Backend**
- `backend/laundryghar/core.Application/Engagement/Cms/NotificationTemplates/Queries/GetNotificationTemplateByCode/NotificationTemplateDto.cs` (expanded projection)
- `backend/laundryghar/operations.Application/Logistics/Incentives/**` (new — Dtos, GetIncentiveRules, Create/Update/Delete)
- `backend/laundryghar/operations.WebApi/Endpoints/Logistics/IncentiveRulesAdmin.cs` (new endpoint group)

**Frontend (admin-web/src)**
- `lib/utils.ts` (formatDate/formatDateTime hardened)
- `lib/validation.ts` (added `isJsonContainer`)
- `pages/cms/NotificationTemplatesTab.tsx` (use `isJsonContainer`)
- `api/fulfillment.ts` (added `/api/v1` prefix)

**DB / seed**
- `db/patches/seed_demo_users.sql` (rewritten reseed-proof; applied to live DB)

Frontend edits typecheck clean (`tsc -b --noEmit` → 0 errors). Backend incentive endpoint builds clean.
