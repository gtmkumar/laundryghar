---
name: project-admin-web-patterns
description: admin-web (React 19 + Vite + TS + TanStack Query + Zustand + Tailwind) house patterns — shared components, route gating, infinite scroll, validation, CSV
metadata:
  type: project
---

admin-web lives at repo root `admin-web/` (NOT under backend/). React 19, Vite, TS, TanStack Query, Zustand, Tailwind.

**Shared components (compose, never hand-roll)** — `src/components/shared/`:
- `FilterableTable` backs every list. Props worth knowing: `totalCount` (server grand total for the count line), `footer` (slot for an infinite-scroll sentinel + spinner), and `csvExport={{ filename, columns: CsvColumn<T>[] }}` — CSV export is BUILT IN (client-side, exports filtered/visible rows; `src/lib/csv.ts` has an RFC-4180 + formula-injection guard). Wire `csvExport` per page to get the button.
- `FormDrawer` = the one drawer chrome. As of R3-AW-1 it has Escape-to-close + focus-trap + scroll-lock via a module-level `drawerStack` so only the TOPMOST layer handles Escape (respects `elevated` z-[60] nesting). Hooks (useId/useRef/useEffect) sit ABOVE the `if (!open) return null` guard.
- `ConfirmDialog` + `useConfirm()` hook = the destructive-action gate (z-[70], already had focus-trap/scroll-lock). `gate.confirm({...})` + `<ConfirmDialog {...gate.dialogProps} />`.
- `ForbiddenState`/`isForbiddenError` for page-level 403s; `ForbiddenPage` (src/pages/) for route-level 403s.

**Infinite-scroll pattern**: `useInfiniteQuery` (queryFn reads `page`/`pageSize`, `getNextPageParam` reads `lastPage.hasNextPage`) + the `useInfiniteScroll({hasNextPage,isFetchingNextPage,fetchNextPage})` hook returns a callback ref for a sentinel `<div>`. Reference: `useAccessControl.ts` / catalog tabs. `PaginatedList<T>` carries `totalCount` + `hasNextPage`; backend `PaginatedList.CreateAsync` does an independent `CountAsync`, so a `pageSize:1` call is a cheap accurate count (used for dashboard KPIs).

**Route → permission gating** (R3-WEB-1): `src/lib/routePermissions.ts` is the canonical static map mirroring the server `identity_access.modules` table (route + required_permission). The navigator DTO (`NavItemDto`) drops `requiredPermission`, so the client CANNOT derive it from the navigator — keep the static map in sync with the seed SQL. `RequirePermission` (layout route) renders `ForbiddenPage` on miss. `/settings` is user_type-gated (useCanManageSettings: platform_admin|brand_admin), NOT a permission code.

**Permissions**: `usePermissions().hasPermission(code)` (platform_admin bypasses); `useCanManageSettings()` for settings panels. Settings panel submit handlers must early-return `if (!canManage)` — a disabled button is a hint, not a guard.

**Validation**: `src/lib/validation.ts` holds shared zod schemas mirroring backend FluentValidation — `futureDate`, `percentage`/`percentageInt` (0-100), `optionalPan`, `optionalIfsc`, `optionalAadhaarMasked` (XXXX XXXX 1234 or 12 digits), etc. Some drawers (packages/coupons/royalty) validate imperatively in `submit()` with `Number()` + `setError`, not zod — package credit≥price, coupon %≤100 & flat<minOrder, royalty/marketing/GST 0-100 all live there.

**Brand-scoped query enabled-gate (recurring 401 bug class)**: getStores/getOrders/getOpsQueues/etc. ride an axios interceptor that injects `X-Brand-Id` from brandStore. On dashboard mount the brand auto-select (in AppShell) resolves a tick AFTER the first render, so any brand-scoped query that fires immediately 401s. The house fix: every such hook takes a trailing `enabled = true` param wired to TanStack's `enabled`, and callers pass `Boolean(activeBrandId)`. When auditing a dashboard/panel, check that EVERY brand-scoped query is gated — `useStores`/`useOpsQueues` were each missed once (DEF-R3-1). `usePickupRequests` self-gates via `useEffectiveBrandId()` so it needs no explicit flag.

**DataTable row-click guard**: `src/components/shared/DataTable.tsx` `<tr onClick>` (also used through FilterableTable) guards with `if (!e.currentTarget.contains(e.target as Node)) return` so a React synthetic click bubbling from an element unmounted mid-flight (e.g. switching tabs while a click is in flight) can't spuriously fire onRowClick and open the first row's drawer. Action-cell controls still `e.stopPropagation()` in their own wrapper div.

Gates: `npm run build` runs only `tsc -b && vite build` (NO lint). `react-hooks/set-state-in-effect` errors are a pre-existing baseline across the codebase (the house "reset form state when entity prop changes" effect) — not a build blocker.
