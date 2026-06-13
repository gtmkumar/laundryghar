---
name: project-consolidation-boundaries
description: Security review of the 11→3 service consolidation (Core/Commerce/Operations hosts). Trust boundaries that became weaker by sharing a process — findings + what was verified safe.
metadata:
  type: project
---

## 11→3 consolidation: three hosts, each runs multiple ex-services in one process

- **laundryghar.Core** = Identity (OAuth authz server, holds RSA private key in-proc) + Engagement (admin CMS + anonymous `/api/v1/public/*`) + Mcp (resource server). Two JWT schemes: default "Bearer" + "mcp" (RFC 9728 challenge). Both validate the SAME in-proc RS256 key.
- **laundryghar.Commerce** = Commerce + Finance + Analytics HTTP lanes (RLS, app_user) + Worker BackgroundServices (BypassRls=true) in ONE process/DI container.
- **laundryghar.Operations** = Catalog + Orders + Warehouse + Logistics.

## BOUNDARY 1 — Commerce RLS dispatch (the load-bearing one)

`HostTenant/CommerceHostCurrentTenant.cs` dispatches on `IHttpContextAccessor.HttpContext`: present → HTTP lane (RLS from JWT); **null → worker lane, `BypassRls=true`**.

- **Fail-OPEN default (the consolidation risk):** `BypassRls` getter returns `true` when HttpContext is null (line 66-69). The standalone `HttpContextCurrentTenant.BypassRls` returned `false` when null. So the merge INVERTED the null-default from RLS-enforced to RLS-bypassed. Verified NOT currently triggerable: no HTTP handler spawns a detached scope / `Task.Run` / `CreateScope` that would run a DbContext query with a lost HttpContext (only Worker BackgroundServices + startup migration use CreateScope). ExecutionContext/AsyncLocal flows through EF async path. **Residual:** any future fire-and-forget DB work off a request would silently bypass RLS cross-tenant. Recommend the dispatch require a POSITIVE worker marker (AsyncLocal flag set by the worker scope) rather than inferring worker from `HttpContext == null`.
- **Worker is now app_user, not superuser.** Old `WorkerCurrentTenant` comment says worker connected as postgres superuser (RLS-immune at DB level). Consolidated worker = app_user, relies 100% on the `app.bypass_rls` GUC. Lost the DB-role safety net. Not a leak (fails toward seeing nothing), but the defense-in-depth layer is gone.
- **Pooled-connection GUC leak: SAFE.** `RlsConnectionInterceptor` fires on every `ConnectionOpened` and re-runs `set_config(...is_local=false)`; Npgsql issues DISCARD ALL on pool return (default, connstr has no `No Reset On Close`). Interceptor is Scoped → fresh ICurrentTenant per scope. No cross-request bleed. (This was the prior "H1" fix — still intact.)
- **Platform-admin bypass: SAFE.** `TenantResolutionMiddleware` sets `Items["bypass_rls"]` only when `user_type == PlatformAdmin` (claim from validated JWT). A normal brand JWT cannot set it.

## BOUNDARY 1b — GatewaySettingsCache cross-tenant credential bleed (pre-existing, consolidation-adjacent)

`Commerce/Infrastructure/Gateway/GatewaySettingsCache.cs` is a **DI singleton** caching ONE brand's decrypted gateway creds (API key + webhook secret) for 60s, served process-wide to ALL brands. Comment claims "Commerce always operates within the RLS brand scope" — but the anonymous Razorpay webhook path sets `bypass_rls=true` and primes the cache with a global/arbitrary-brand row (`OrderBy(s => s.BrandId == null).FirstOrDefault()`). Multi-brand deployments: brand B's payment request may transact against brand A's cached gateway creds. Existed in standalone Commerce too, but the bypass-priming webhook now shares the process. Fix: key the cache by brandId, or scope it.

## BOUNDARY 2 — Core dual schemes + anonymous lanes: SAFE

- MCP `/mcp` correctly pins `AuthenticationSchemes="mcp"` + `Policy="McpCustomerOnly"` (Program.cs ~525). `McpCustomerOnly` (customer_mcp+mcp:booking scope OR customer) is used ONLY on `/mcp`. No admin endpoint got a customer policy; no customer endpoint left unauthorized.
- `CoreAuthorizationPolicyProvider`: `permission:*`→Identity req (superset, serves Engagement admin), `CustomerOnly`→Identity req (strict customer), `McpCustomerOnly`→Mcp req. Engagement's own `CustomerOnlyRequirement` type is DEAD (handler not registered, no endpoint produces it). No widening.
- RSA private key NOT exposed: JWKS emits only `n`/`e` (`ExportParameters(false)`); `SigningKey` is DI-only, no HTTP surface. Settings read endpoints are permission-gated + admin guard, not anonymous-reachable.
- **`/api/v1/public` blanket `bypass_rls=true`** (Program.cs ~466): disables RLS for the whole anonymous CMS lane; queries rely SOLELY on explicit `.Where(brandId)` (verified present in all 3 public handlers). brandId is attacker-supplied (X-Brand-Id / brandCode) but content is public-by-design. RISK: this bypass now lives in the SAME process as Identity user/credential tables — any future unfiltered query reachable under `/api/v1/public` would read cross-tenant Identity data. Path match is segment-safe.

## BOUNDARY 3 — anonymous + admin same process

- Razorpay webhook (`RazorpayWebhookHandler.cs`) fail-closed logic CONFIRMED: missing secret non-Dev → reject; bad signature → reject all envs; Dev-only accept-unsigned ONLY when no signature header. HMAC is constant-time. No path to 200 without valid signature outside Development. Endpoint sets `Items["bypass_rls"]=true` BEFORE HMAC verify, but first bypassed query (`GatewaySettingsCache`) and all writes are gateway-id-filtered / post-verify; not exploitable for arbitrary exfil (but see 1b).
- `/health/ready` ResponseWriter emits health-check `description` (Core Program.cs ~440) — could surface DB host/db/user from a failed Npgsql check exception (password redacted by Npgsql). Low, not consolidation-caused (every standalone host had it).

## BOUNDARY 4 — policy unification: SAFE (verified, no widening)

All 4 Operations `PermissionHandler`s (Catalog/Orders/Warehouse/Logistics) are behaviourally identical: token_use=user gate → platform_admin bypass → space-split permissions claim `Contains(code, OrdinalIgnoreCase)`. `TokenClaims.TokenUseValue="user"` / `CustomerTokenClaims="customer"` identical across all domains. `OperationsPolicyProvider` sources permission/customer from Orders, rider from Logistics; registered handlers (Program.cs 134-137) match the produced requirement types exactly. RiderOnly = token_use=user AND user_type=rider. Rider self-service `/api/v1/rider` group-gated by RiderOnly (LogisticsEndpoints.cs ~276); admin rider mgmt under `/api/v1/admin/riders` with permission policies. Commerce provider handles only single-code `permission:*` + CustomerOnly (no pipe any-perm) — fine, none used. No policy name widened.

**How to apply:** On any PR touching these hosts, re-check: (1) no new off-request DbContext work in Commerce (fail-open RLS), (2) GatewaySettingsCache keying, (3) new `/api/v1/public/*` routes don't issue unfiltered queries, (4) merged policy providers stay handler-aligned. Related: [[project-security-patterns]], [[project-oauth-mcp]], [[project-rbac-seeder-drift]], [[project-rider-multitenancy]].
