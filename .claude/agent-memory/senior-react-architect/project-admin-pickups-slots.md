---
name: project-admin-pickups-slots
description: Admin pickup-request queue (Orders tab) + delivery-slot management (Tenancy tab) shipped in admin-web
metadata:
  type: project
---

Admin pickup-request queue + delivery-slot management UI landed in admin-web
(Task #21), driven by customer bookings now persisting as pickup requests with
`requested_items` jsonb.

**Where it lives:**
- Pickup queue → new **"Pickup requests" tab on the Orders page** (`?tab=pickups`
  deep-link, mirrors Riders `?view=`). The original orders list became an inner
  `OrdersTab`.
- Slots → new **"Delivery slots" tab on the Tenancy page**, alongside
  Stores/Franchises/Warehouses.

**Backend contracts consumed (Orders :5002 `/api/v1/admin`):**
- `GET /pickup-requests?page&pageSize&status` (perm pickup.read) — DTO **does**
  expose `cartItems[]` + `paymentPreference` on the admin path (verified live).
- `POST /pickup-requests/{id}/assign {riderId}` (perm pickup.assign).
- `GET/POST/PUT /delivery-slots` (perms delivery.slot.read / .manage).
- Assign picker searches riders via Logistics `GET /api/v1/admin/riders` (:5004).

**Why:** Customer booking went live with no admin surface for incoming pickups.

**How to apply:**
- There is **NO pickup reject/cancel endpoint** — assign is the only mutation.
  Don't promise a reject action until the backend adds one.
- Pickup + slot DTOs carry only `customerId` / `storeId` (no names) — enrich via
  the customers list (Catalog) / stores list (Identity), same Map pattern as
  TenancyPage's franchiseName lookup.
- New code: `src/api/pickups.ts`, `src/hooks/usePickups.ts`, and the
  `pages/orders/Pickup*` + `pages/tenancy/DeliverySlot*` files. Gates pass
  (tsc --noEmit + build). Related: [[project-backend-inferred-body-500]].
