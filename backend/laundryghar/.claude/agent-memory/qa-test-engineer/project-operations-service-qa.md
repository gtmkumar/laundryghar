---
name: project-operations-service-qa
description: Operations service (5002, Catalog+Orders+Warehouse+Logistics) live-API contract gotchas + QA setup discovered 2026-06-13
metadata:
  type: project
---

Operations service = consolidated Catalog + Orders + Warehouse + Logistics on http://localhost:5002. Routes: admin `/api/v1/admin/*`, customer `/api/v1/customer/*` (CustomerOnly), rider `/api/v1/rider/*` (RiderOnly). Gateway 8080 prefixes /catalog /orders /warehouse /logistics — all verified routing live.

**Why:** Used when QA-testing this service's CRUD surfaces; these non-obvious request-contract values cost iterations to discover.

**How to apply (request-contract values that the DTOs type loosely as `string` so wrong values yield DB/validation errors):**
- `nameLocalized` is a **jsonb** column everywhere (categories/services/items/groups/processes/conditions). Must send a JSON-object STRING e.g. `"{\"en\":\"x\"}"`. Plain string → leaked Npgsql `22P02 invalid input syntax for type json` as 400. See [[defect-operations-jsonb-leak]] candidate.
- service `pricingModel` valid = `per_item` (NOT per_piece). add-on `pricingType` valid = `flat`. price-list `scopeType` = brand|franchise|store. rider `employmentType` = employee|contractor|gig|outsourced. garment-tag `tagFormat` = qr|barcode_128|barcode_39|rfid (lowercase). order-note `visibility` = staff|customer|platform, `noteType` = internal|customer_facing|complaint|resolution|flag. inspection `inspectedByType` = rider|store_staff|warehouse_staff|qc_staff. recon-item `status` = matched|missing|unexpected|damaged|resolved|escalated.
- Admin customer create + rider invite phone must be E.164 (`+9199...`).
- Order state machine (OrderStateMachine.cs) enforces placed→pickup_scheduled→pickup_assigned→pickup_in_progress→picked_up→received→sorting→in_process→qc→ready→delivery_scheduled→delivery_assigned→out_for_delivery→delivered→closed. Invalid transition → 422.

**QA setup that worked:** admin login on 5050 `/api/v1/auth/password/login`; customer via `/api/v1/customer/auth/otp/send`+`/otp/verify` master OTP 123456; rider via generic `/api/v1/auth/otp/send`+`/otp/verify` after inviting rider at `/api/v1/admin/access-control/riders/invite` (Core/Identity, needs franchiseId). Real storeId from existing orders list (`data.list[]`). No Warehouse records exist in this env → warehouse-batch / process-log / QC require a non-null WarehouseId and are not testable without seeding one.

**Surfaces with NO delete endpoint** (cleanup leftovers are expected): price-list-items (POST/PUT only), warehouse processes/conditions/garments/batches/recon, capacity-configs, rider-assignments, delivery-slots, delivery-assignments. Riders use POST /{id}/deactivate (after which GET returns 404). Catalog entities (cat/svc/item/group/fabric/variant/addon/price-list) hard-delete cleanly (GET→404 after).
