---
name: project-dashboard-ops-orders-live
description: Tasks #41/#42 — dashboard ops widgets + live Orders page (cards/history/chime); backend ops-queues unactioned bucket + statusGroup param
metadata:
  type: project
---

Tasks #41 (dashboard ops widgets) + #42 (Orders page cards/live/tone) — admin-web.

**Backend additions (smallest correct change):**
- `OpsQueuesQuery`/`OpsOrderDto` gained a 4th bucket `unactioned` (orders status='placed') plus `StoreId` + `AgeMinutes` on the DTO. Drives the dashboard "Needs action" panel.
- `GetOrdersQuery` gained a `statusGroup=active|history` param (active=non-terminal, history=terminal). A specific `status` filter still takes precedence. Powers the Orders page active-cards vs history-table split, pagination-safe (vs client-side splitting which breaks paging).
- Terminal set (both places): delivered, cancelled, closed, returned.

**Why:** screenshots wanted a placed-order "needs action" queue + an active/history card split; the list endpoint only did single-status equality.

**Frontend shared infra (reuse these):**
- `pages/orders/orderFormat.tsx` — `useStatusLabel()`/`formatStatusLabel()` (i18n `orders.status.*` with Title-Case fallback), `formatDurationMinutes`, `minutesSince/Until`, `ageUrgency` (amber <2h / red ≥2h), `paymentTone`+`PAYMENT_TONE_CLASS`. ALL status-label surfaces route through here.
- `hooks/useOrderChime.ts` — Web-Audio two-tone chime, no asset; AudioContext armed on first pointer/keydown (autoplay unlock), persisted toggle localStorage `lg_order_sound` (default ON, silent until gesture).
- `hooks/useNewItemDetector.ts` — generic seen-ids tracker; establishes a baseline on first non-empty list so it NEVER fires on mount; returns highlighted ids (auto-expire ~6s) + calls `onNew(id)`. Shared by orders + (available to) dashboard.
- `.lg-new-pulse` keyframe in index.css (green ring; respects prefers-reduced-motion).
- Toasts via `showToast(variant,msg)` from `stores/toastStore` (de-dupes bursts).

**How to apply:** new live-list surfaces should reuse useNewItemDetector + useOrderChime rather than re-rolling. Status labels: never `.replace(/_/g,' ')` inline — use useStatusLabel.

**Deep-links:** Orders page supports `?order=<id>` (opens OrderDetailDrawer, survives refresh) and `?tab=pickups|ops`. Dashboard NeedsActionPanel navigates orders→`/orders?order=`, pickups→`/orders?tab=pickups`; riders panel → `/riders?view=map`.

**Live-verify recipe that worked:** Playwright script must live INSIDE admin-web/ (module resolution); login form ids `#identifier`+`#password`. To exercise the new-booking chime without fighting the order-create contract, intercept `**/admin/orders?*statusGroup=active*` and inject a synthetic order on the 2nd+ poll — exercises the real detector→chime→toast path, leaves zero DB rows. Console-logs `[order-chime] new order <number>`.

i18n namespaces added: `orders.*` (incl. status.* + payment.*) and `dashboard.*` in en.json + hi.json.
