---
name: dashboard-redesign
description: Admin-web login + dashboard redesign — warm cream theme, dark sidebar, real data wiring, auto brand select
metadata:
  type: project
---

Redesigned LoginPage and DashboardPage in admin-web with warm LG design system.

**Why:** Orchestrator-driven redesign from product mockups; aesthetic upgrade + real data wiring.

**How to apply:** When touching admin-web auth or dashboard screens, follow established token usage patterns.

Key non-obvious decisions:
- `activeBrandId` gate: all brand-scoped queries (analytics, orders) have `enabled: Boolean(activeBrandId)` to prevent 401s before AppShell's auto-select resolves
- AppShell runs a one-shot `getBrands()` on mount to auto-select first active brand for platform_admin users (no activeBrandId in their JWT)
- Order status counts use `getOrders({status, pageSize:200})` + `list.length` — no count endpoint exists; comment left in code
- `useOrders` and `useAnalyticsDashboard`/`useDailyStoreRevenue` hooks now accept `enabled` flag
- Live feed refetches every 30s via TanStack `refetchInterval`
- Revenue chart: CSS-only bars, latest day = amber, others olive-green
- Smart Insight: derives today vs 7-day avg from chart data; falls back to generic tip when data unavailable
- Caveat font loaded via Google Fonts CSS `@import` (before `@import "tailwindcss"` to satisfy CSS order)
- Role cards on login are visual-only (no auth impact), default Super Admin selected
- Coming soon pages at /customers, /riders, /packages, /coupons, /cashbook, /expenses to prevent 404s

Design tokens in index.css as CSS vars: --lg-cream, --lg-green, --lg-amber, --lg-sidebar-bg, --lg-sidebar-pill

**Dashboard polish (2026-06-07):**
- `AdminCustomerDto` added to `types/api.ts` (mirrors C# record: id, customerCode, firstName, lastName, displayName, ...)
- `getAdminCustomers()` added to `api/catalog.ts` hitting `GET :5001/api/v1/admin/customers`
- `useAdminCustomers` + `useCustomerNameMap` added to `hooks/useCatalog.ts`; name map prefers displayName → firstName+lastName → customerCode → UUID tail fallback; staleTime 5min
- Live order feed Customer column now uses `customerNameMap.get(order.customerId)` with fallback to `…last6ofId`
- KPI trend badges: today-vs-yesterday computed from `DailyStoreRevenue` rows already in cache via `sumForDate()` helper; Orders Today now has a trend badge; Revenue Today falls back to 7-day avg if yesterday data is absent (sparse seed scenario)
- Login warm glow: replaced single `opacity-20` overlay with two-layer radial-gradient divs (outer 320px halo at 22% opacity, inner 180px core at 30% opacity) — both rgba(230,162,60,...) amber
