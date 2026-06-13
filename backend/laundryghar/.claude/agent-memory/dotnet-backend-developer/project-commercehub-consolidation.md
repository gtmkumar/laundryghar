---
name: commercehub-consolidation
description: CommerceHub merges Commerce+Finance+Analytics+Worker; the worker-vs-HTTP single-DbContext tenant dispatch design and why duplicate types coexist
metadata:
  type: project
---

laundryghar.CommerceHub is the consolidated host for the former Commerce (5005), Finance (5006), Analytics (5008) and Worker services (part of the 11→3 service consolidation). HTTP lanes + in-process Worker hosted services run in ONE host on port 5005.

**Why the worker/HTTP tenant split is solvable with one DbContext (the load-bearing insight):**
At runtime the standalone Worker's `ConnectionStrings:Default` was already `app_user` (NOT superuser) — the architecture note saying "Worker connects as superuser" is wrong for the live config. The Worker gets cross-tenant visibility purely via `WorkerCurrentTenant.BypassRls=true`, which makes `RlsConnectionInterceptor` emit `SET app.bypass_rls='true'` (a DB policy honors it). `ConnectionStrings:Admin` (postgres) is used ONLY for the privileged seeding path (`SeedingSupport.CreatePrivilegedContext`), never for normal worker queries.
**Why it matters:** because both lanes use the same app_user connection, the merge needs only ONE `AddSharedDataModel` + ONE `LaundryGharDbContext` + ONE Scoped `ICurrentTenant`. No second DbContext / DbContextFactory / keyed connection was needed. Do not add one.

**How the single ICurrentTenant serves both lanes:** `HostTenant/CommerceHubCurrentTenant.cs` dispatches on `IHttpContextAccessor.HttpContext` presence. HTTP request scope → has HttpContext → read tenant from JWT claims + honor `brand_id_override`/`bypass_rls` (RLS enforced). Worker hosted-service scope (created via `IServiceScopeFactory.CreateAsyncScope()`) → no HttpContext → BypassRls=true, no brand context. The moved `Worker/Infrastructure/WorkerCurrentTenant.cs` is now dead/unregistered but left in place (keep-husk rule).

**Why duplicate types did NOT need deleting:** Commerce/Finance/Analytics each keep their own namespace (`laundryghar.Commerce.*` etc.), so identically-named types (`ICurrentUser`, `JwtSettings`, `PermissionHandler`, `PermissionPolicyProvider`, `TokenClaims`, `ValidationPipelineBehavior`) are DISTINCT types and coexist. The only compile hazard was global-using ambiguity: each lane's `_Imports.cs` globally imported its own `Infrastructure.Auth` + `Infrastructure.Services`; in one assembly that made simple names like `ICurrentUser` ambiguous. Fix applied: removed those two global usings from all three `_Imports.cs` and added explicit file-scoped `using laundryghar.<Lane>.Infrastructure.Services;` to the ~20 files that consume `ICurrentUser`. Auth types are only used inside their own Auth folder (same-namespace), so needed no using.

**DI reconciliations (single registration of the strict superset):**
- `ICurrentUser`: all THREE registered (each lane's interface→its own HttpContextCurrentUser) — handlers inject their own.
- Auth: Commerce's `PermissionPolicyProvider` is the superset (handles both `permission:<code>` AND `CustomerOnly`); Finance/Analytics versions handled only `permission:` — dropped. Commerce's `PermissionHandler`+`CustomerOnlyHandler` registered once (byte-equivalent across lanes).
- MediatR: one scan of merged assembly; ONE `ValidationPipelineBehavior` (Commerce's) registered as open-generic `IPipelineBehavior<,>` — Commerce's and Finance's were byte-equivalent, registering both double-validates.
- TenantResolutionMiddleware: Commerce's registered once (byte-equivalent across lanes).

**Route facts:** Commerce mounts children directly under `/api/v1/admin` and `/api/v1/customer` (NOT `/admin/commerce`, despite task prose) — e.g. `/api/v1/admin/payment-methods`. Finance also mounts under `/api/v1/admin` but child paths are disjoint from Commerce (cash-books/expenses/royalty-invoices/etc. vs payment-methods/packages/coupons/etc.), so no routing conflict. Analytics under `/api/v1/admin/analytics`. Anonymous HMAC `/api/v1/webhooks/razorpay` preserved.

**No Razorpay NuGet SDK exists** — Razorpay is called via a named `HttpClient "razorpay"`, not an SDK package. csproj union has no Razorpay package.

Old husk projects (laundryghar.Commerce/.Finance/.Analytics/.Worker) keep Program.cs+appsettings+csproj+obj but their source moved out, so they no longer build standalone; AppHost/slnx still reference them — rewiring is the orchestrator's job, deferred.
