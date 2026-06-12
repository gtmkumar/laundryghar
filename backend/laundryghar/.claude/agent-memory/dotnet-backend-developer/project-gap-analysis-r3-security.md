---
name: project-gap-analysis-r3-security
description: R3-SEC-1/2/3 and R3-BE-8 RBAC gap remediation decisions — seeder parity, POS permission family, settings permission gates, any-permission policy pattern
metadata:
  type: project
---

R3-SEC-1/2/3 and R3-BE-8 shipped 2026-06-12.

**R3-SEC-1 (seeder parity)**
Eight permission codes existed only in SQL patches but not IdentitySeeder: `payment.record`, `customer.create`, `rider.verify`, `rider.settle`, `subscription.read`, `subscription.manage`, `saas.read`, `saas.manage`. All folded into PermissionDefs. New SQL patch `db/patches/seeder_parity_r3sec1.sql` handles live DB (no-op if patches already applied). Drift gate: `PermissionDriftTests.AllEndpointPermissionCodes_ExistInPermissionDefs` via reflection on the private static field.

**R3-SEC-2 (POS permission family)**
Added `pos.order.create` and `pos.order.read`. Implemented **pipe-syntax any-permission policy** in both laundryghar.Identity and laundryghar.Orders PermissionPolicyProviders: `"permission:orders.create|pos.order.create"` routes to `AnyPermissionRequirement` (new class) handled by `AnyPermissionHandler`. Both `AnyPermissionRequirement.cs` and `AnyPermissionHandler` live in the same file per service (Identity + Orders each have their own copy — each service owns its auth stack). Pattern is reusable for other service pairs.

**Why:** Prevents POS counter staff needing the broader `orders.*` admin module grant just to run the register.

**R3-SEC-3 (settings.manage)**
SettingsEndpoints was gated by a `UserType == "brand_admin"` string check — opaque and outside the permission model. Now gated by `permission:settings.read` (GET) and `permission:settings.manage` (PUT/POST) at the endpoint level. The in-handler `Forbidden()` guard kept as defence-in-depth. The `settings` module row's `required_permission` must be set to `settings.read` via `db/patches/settings_permissions.sql`.

**R3-BE-8 (rider.read. trailing dot)**
The trailing dot was in a *code comment* only (`RiderProofPhotoQueries.cs` line 10 and `LogisticsEndpoints.cs` line 107), not in any `RequireAuthorization()` call. The actual policy string at LogisticsEndpoints.cs:196 was and remains `"permission:rider.read"` — no runtime breakage. Fixed comment wording only.

**How to apply:** See post-deploy steps in GAP_ANALYSIS_R3.md wave dispatch output.
