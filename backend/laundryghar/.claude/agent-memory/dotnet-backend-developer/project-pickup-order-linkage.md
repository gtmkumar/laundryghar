---
name: project-pickup-order-linkage
description: Pickup-leg DeliveryAssignment links to PickupRequest (not Order); order resolution for pickup legs is fragile because converted_order_id is often null
metadata:
  type: project
---

A pickup-leg `delivery_assignments` row links via `pickup_request_id` only — its `order_id`/`order_created_at` are NULL. The order is reached transitively: `delivery_assignment.pickup_request_id → pickup_requests.converted_order_id → orders`.

**Why:** In the customer-mobile booking flow the app creates an Order AND a separate PickupRequest, but does NOT set `pickup_requests.converted_order_id`, so the pickup leg cannot resolve its order at all. Admin/dispatch assigns the pickup leg to a rider via `pickup_request_id`, leaving the order link absent.

**How to apply:**
- Any code advancing "the order" from a pickup-leg event (e.g. DEFECT 6 completion→status) MUST treat the order as optional — advance the `pickup_request` unconditionally, advance the order only when `converted_order_id` (or the assignment's own `order_id`) resolves. Never force an illegal status jump when no order is linked.
- Rider tasks/today (DEFECT 4) must enrich pickup legs from the `PickupRequest` (customer/address/scheduled/amount/count), because the order LEFT JOIN on `{order_id, order_created_at}` misses for pickup legs and yields placeholder values ("—", "Customer", "Address on file", 0).
- Pickup status CHECK has no `picked_up` value — a completed pickup leg sets `pickup_requests.status='completed'`. Order lifecycle: collected = `picked_up`, dropped-at-store = `received` (walk OrderStateMachine.ForwardPath one legal hop set at a time).
- Open follow-up for the orchestrator: the customer booking flow should set `converted_order_id` (or the pickup-assignment should carry `order_id`) so pickup-leg completion can reliably advance the order. Until then, orders created alongside a pickup stay un-advanced by rider pickup completion.
