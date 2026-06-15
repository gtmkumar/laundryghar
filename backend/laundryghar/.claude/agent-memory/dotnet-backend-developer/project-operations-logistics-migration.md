---
name: project-operations-logistics-migration
description: Non-derivable decisions migrating the Logistics sub-domain into the operations split host — RiderOnly auth gap, raw-SQL seam, cross-domain helper porting, deferred out-of-scope areas
metadata:
  type: project
---

Migrated the Logistics sub-domain (~6k lines) from legacy MediatR into operations.WebApi:5015, mirroring the Warehouse migration ([[project-operations-warehouse-migration]]). Six in-scope sub-areas: Riders, Assignments, CapacityConfigs, RiderOps, Payout(admin+self), RiderSelf. Non-derivable points:

**1. `RiderOnly` auth policy did NOT exist in the operations host.**
**Why:** the host's shared `PermissionPolicyProvider` only knew `permission:<code>` and `CustomerOnly`; the rider self-service lane requires a `RiderOnly` policy (token_use=user AND user_type=rider).
**How to apply:** added `RiderOnlyRequirement`/`RiderOnlyHandler` to the SHARED `laundryghar.Utilities/Auth` (next to CustomerOnly), added a `RiderOnly` branch to `PermissionPolicyProvider`, and registered `RiderOnlyHandler` as a singleton `IAuthorizationHandler` in operations.WebApi/Program.cs. Other hosts ignore it unless a route requests "RiderOnly".

**2. `IOperationsDbContext` needed a raw-SQL seam for rider current_load.**
**Why:** `RiderLoadHelper` (shared, takes concrete LaundryGharDbContext) does a guarded atomic `UPDATE logistics.riders SET current_load = GREATEST(0, current_load - 1)` — not expressible as a tracked change, and handlers only hold the interface.
**How to apply:** added `Task<int> ExecuteSqlInterpolatedAsync(FormattableString, ct)` to IOperationsDbContext (impl forwards to `_db.Database.ExecuteSqlInterpolatedAsync`). Ported the helper to `operations.Application/Logistics/Common/RiderLoad.cs` over the interface. Used by offer-accept (increment) + task completion/fail (decrement).

**3. Rider self-resolution moved OUT of endpoints INTO handlers.**
**Why:** legacy rider-self endpoints did `db.Riders.Where(UserId==.. && BrandId==..)` to get rider.Id, but split-host endpoints have no DbContext — only IDispatcher. Endpoints resolve userId/brandId from `ICurrentUser` (UserId=NameIdentifier/sub, BrandId=brand_id claim — same claims the legacy HttpContext helpers read).
**How to apply:** changed BatchLocationPing/UpdateMyAssignmentStatus/AcceptOffer/DeclineOffer commands to take `UserId` (not RiderId) and resolve rider internally; null rider → empty/NotFound result. The task/otp/proof/inspection commands already took UserId.

**4. Cross-domain helpers had to be ported (Application can't reference Orders).**
**Why:** RiderSelf task handlers depended on `LocalDateRange` + `OrderStateMachine` (legacy laundryghar.Operations/Orders) and the Orders `AssignPickupHandler.ResolvePickupCodAmount` — none exist in the split repo.
**How to apply:** ported all three as dependency-free statics into `operations.Application/Logistics/Common/` (LocalDateRange, OrderStateMachine, PickupCod). GeofenceEvaluator + PayoutConfig re-targeted at IOperationsDbContext.

**5. Deferred (out of scope, cross-domain) — left as follow-ups, NOT migrated:**
- IncentiveEvaluator call in UpdateMyTaskStatus: DROPPED with a comment. It was best-effort (try/catch, never blocked completion) and depends on the Logistics Incentives sub-area + Orders Fare config. No completion behaviour changes; incentive awards just aren't auto-created until Incentives is migrated.
- Admin incentive-rules CRUD + admin/rider support-ticket endpoint groups: depend on Orders Support / Incentives — NOT migrated.
- RiderCod admin lane (`/api/v1/admin/riders/cod/*`, `/{id}/settlements`, `/{id}/settle`): RiderCod sub-area is out of scope. `RiderSettlements` DbSet was added defensively but no handlers/routes.

Validators target the Request type (Warehouse rule). Push-token validators stayed command-targeted (endpoint binds individual fields) and get NO ValidationFilter — consistent with the Warehouse superseded-pipeline exception.
