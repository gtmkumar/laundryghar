---
name: project-backend-inferred-body-500
description: Several backend GET list endpoints intermittently 500 with "Body was inferred" when the AppHost runs a stale build
metadata:
  type: project
---

Several admin GET **list** endpoints have been observed returning HTTP 500 with
`System.InvalidOperationException: Body was inferred but the method does not allow
inferred body parameters` — seen on Logistics `GET /api/v1/admin/riders` (:5004)
and Catalog `GET /api/v1/admin/customers` (:5001). The endpoint source itself is
well-formed (uses `[FromServices] ISender` + query primitives), so this is a
**runtime/build-state issue, not a contract bug**.

**Why:** Backend .cs changes require a full AppHost restart (dcp won't auto-restart
a killed resource — see user auto-memory `laundryghar-aspire-restart`). When the
running binary is stale / mid-edit (e.g. uncommitted backend changes), minimal-API
parameter binding can mis-resolve and throw this at request time. The Orders
service (:5002) pickup-requests + delivery-slots endpoints were healthy during the
same window, so it is per-service.

**How to apply:** If a frontend list screen that worked before suddenly shows an
error state, hit the raw endpoint with curl first — a "Body was inferred" 500 means
the backend needs an AppHost restart, NOT that the frontend or the DTO contract is
wrong. Frontend code should degrade gracefully (empty name-lookup map, explicit
"roster unavailable" picker state) rather than assume the data. Related:
[[project-admin-pickups-slots]].
