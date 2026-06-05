---
name: project-admin-web-scaffold
description: Admin-web React 19 + Vite scaffold — current screen inventory, API client patterns, and CMS section status
metadata:
  type: project
---

Admin-web lives at `admin-web/` (repo root). Stack: React 19 + Vite + TypeScript strict + TanStack Query + Zustand + Tailwind + shadcn-style UI (custom components in `src/components/ui/`).

**Why:** Platform-admin console for Laundry Ghar microservices. Build and tsc both pass clean. Bundle ~650 kB (pre-existing chunk-size warning, not a regression).

**Current screens (all routed under AppShell / ProtectedRoute):**
- `/` — DashboardPage
- `/tenancy` — TenancyPage (stores + franchises tabs)
- `/catalog` — CatalogPage (service categories + services + price lists tabs)
- `/orders` — OrdersPage (paginated list + status filter)
- `/cms` — CmsPage (Engagement service, port 5007) — **added 2026-06-06**

**CMS section details (added 2026-06-06):**
- 6 tabs: Notification Templates (CRUD), Onboarding Slides (CRUD), App Banners (CRUD), Mobile App Config (CRUD), Notification Outbox (read + retry), Notification Logs (delivery + WhatsApp sub-tabs, read-only)
- Files created: `src/api/engagement.ts`, `src/hooks/useCms.ts`, `src/pages/cms/CmsPage.tsx`, `NotificationTemplatesTab.tsx`, `OnboardingSlidesTab.tsx`, `AppBannersTab.tsx`, `MobileAppConfigTab.tsx`, `NotificationOutboxTab.tsx`, `NotificationLogsTab.tsx`
- `engagementClient` added to `src/api/client.ts` via `VITE_ENGAGEMENT_URL`
- CMS types appended to `src/types/api.ts`

**API pattern (all services):**
- Axios instance per service created by `createInstance()` in `src/api/client.ts`
- 401 → silent refresh → retry (once), then clear auth + redirect to /login
- Response envelope: `{ status: boolean, data: T | null, message? }`; helpers `unwrap<T>` / `unwrapPaginated<T>`
- TanStack Query hooks in `src/hooks/use*.ts`; query key factories as exported objects; mutations call `qc.invalidateQueries` on success

**How to apply:** When adding new service integrations or screens, mirror the above patterns exactly. Do not invent new axios instances or query key shapes.
