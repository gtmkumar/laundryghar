---
name: project-infinite-scroll
description: Infinite scroll migration — which screens were converted, how, and which hooks were intentionally left as useQuery for non-list consumers
metadata:
  type: project
---

All prev/next paginated list screens in admin-web were migrated to infinite scroll (June 2026).

**Pattern:** Additive — new `*Infinite` hook variants were added alongside the existing flat `useQuery` hooks. The existing hooks were NOT changed, preserving consumers that read `.data?.list` or `.data?.list.length` directly (Sidebar, Topbar, DashboardPage).

**Why:** `useStores` is consumed by Sidebar (badge count), Topbar (store count display), and DashboardPage (store join). `useOrders` is consumed by DashboardPage (live feed + KPI counts). Converting these to infinite would require flattening across every call site. Adding parallel `useStoresInfinite` / `useOrdersInfinite` etc. is the safe, non-breaking approach.

**How to apply:** When adding new list screens, default to the `*Infinite` hook pattern. Only add a flat `useQuery` hook if a non-list consumer needs the raw `.data?.list` shape (e.g., a selector dropdown, sidebar badge).

**Screens converted (11 total):**
- `OrdersPage` → `useOrdersInfinite` (status filter in queryKey)
- `CatalogPage` → `useServiceCategoriesInfinite`, `useServicesInfinite`, `usePriceListsInfinite`
- `TenancyPage` → `useStoresInfinite`, `useFranchisesInfinite` (brandId param)
- `NotificationTemplatesTab` → `useNotificationTemplatesInfinite`
- `OnboardingSlidesTab` → `useOnboardingSlidesInfinite`
- `AppBannersTab` → `useAppBannersInfinite`
- `MobileAppConfigTab` → `useMobileAppConfigsInfinite`
- `NotificationOutboxTab` → `useNotificationOutboxInfinite` (status filter in queryKey)
- `NotificationLogsTab/DeliveryLogsSection` → `useNotificationLogsInfinite` (channel filter)
- `NotificationLogsTab/WhatsAppLogsSection` → `useWhatsAppLogsInfinite` (direction filter)

**Sentinel placement:** `<div ref={sentinelRef} className="h-1" />` sits directly after the `<DataTable>`, outside the `<Card>` wrapper in the orders page, inside the tab content div elsewhere. The "loading more" spinner follows the sentinel.

**Filter reset:** Filter state is passed as an argument to the infinite hook, which includes it in the `queryKey`. React Query automatically refetches from page 1 when the key changes — no manual reset needed.

**pageSize:** All infinite hooks use pageSize 100. Existing flat hooks were NOT changed from their defaults (20 for most, 200 for Dashboard order counts, 8 for live feed).

**Hook consumers NOT changed (intentionally kept flat):**
- `Sidebar.tsx:145` — `useStores({ pageSize: 100 })` reads `.data?.list.length`
- `Topbar.tsx:25` — `useStores({ pageSize: 100 })` reads `.data?.list.length`
- `DashboardPage.tsx:472-480` — `useOrders` (3 status counts + feed) and `useStores` (store join map)
