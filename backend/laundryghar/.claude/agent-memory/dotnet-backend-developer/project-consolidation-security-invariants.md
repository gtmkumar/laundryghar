---
name: project-consolidation-security-invariants
description: SEC-1/2/3 fail-closed invariants from the consolidation security pass — worker RLS marker, per-brand gateway cache, narrowed public-CMS bypass
metadata:
  type: project
---

Security remediation of consolidation-introduced findings (Commerce + Core hosts). Three load-bearing invariants future work MUST preserve:

**SEC-1 — worker RLS bypass is a POSITIVE marker, never inferred from "no HttpContext."**
`CommerceHostCurrentTenant.BypassRls` grants cross-tenant bypass ONLY when (a) `WorkerScope.IsWorkerScope` (an AsyncLocal flag) is set, or (b) an HTTP request set `Items["bypass_rls"]=true`. A context-less flow with NO worker marker now fails CLOSED (BypassRls=false, BrandId=null).
**Why:** the old code inferred "this is the worker" from HttpContext==null — fail-open: any fire-and-forget Task.Run off an HTTP request loses HttpContext and would have run RLS-bypassed across ALL tenants.
**How to apply:** every Worker/BackgroundService that opens a DI scope MUST use the `_scopeFactory.CreateWorkerAsyncScope()` / `.CreateWorkerScope()` extensions (in [[commercehub-consolidation]] host, namespace laundryghar.Commerce.HostTenant, exposed via project-root GlobalUsings.cs). A plain `CreateAsyncScope()` in a worker now yields NO cross-tenant visibility — its queries will see zero rows. Do NOT reintroduce bare scope creation in worker code.

**SEC-2 — GatewaySettingsCache is keyed PER BRAND (ConcurrentDictionary<Guid,…>, Guid.Empty = global row).**
`GetAsync(db, brandId, ct)` now takes an explicit brandId. `SettingsFirstPaymentGateway` passes `ICurrentTenant.BrandId`; the Razorpay webhook handler resolves the payment FIRST, then calls `GetAsync(db, payment.BrandId, ct)` and verifies HMAC against THAT brand's webhook secret.
**Why:** the old singleton cached one decrypted Razorpay key/secret for 60s and served it to every brand — brand B could transact on brand A's credentials within the TTL window.
**How to apply:** never call the cache without the correct brand. The webhook handler parses untrusted JSON → finds payment by gateway_order_id (bypass_rls path) → resolves brand secret → verifies HMAC → acts. Don't reorder.

**SEC-3 — Core's anonymous public-CMS RLS bypass is an EXACT-ROUTE allow-list, not a `/api/v1/public` prefix.**
`IsAllowlistedPublicCmsPath` (file-local static in laundryghar.Core/Program.cs) bypasses RLS only for `/api/v1/public/{banners,onboarding-slides,app-config}`. Each handler carries an explicit `.Where(brandId)` predicate.
**Why:** Core now co-hosts Identity (users/credentials/secrets); a blanket prefix bypass meant any future /api/v1/public/* route forgetting a brand predicate would read across all tenants. Couldn't GUC-enforce instead because BrandResolver must read tenancy_org.brands (policy rls_admin_only=rls_bypass()) before the brand is known, and the RLS interceptor fixes the brand GUC at connection-open.
**How to apply:** any NEW anonymous public route must be added to the allow-list AND pass an explicit brand predicate. Do NOT broaden back to a prefix match.

Health writers (Core + Operations /health/ready) now omit check `description` outside Development (it can leak Npgsql host/db/user on failure). Commerce's writer already only returned name+status.

Tests added: Commerce.Tests/HostTenant (SEC-1, 8) + Gateway/GatewaySettingsCacheBrandIsolationTests (SEC-2, 5); Engagement.Tests/Cms/PublicCmsBrandIsolationTests (SEC-3, 9). Commerce.Tests + Engagement.Tests gained a Microsoft.EntityFrameworkCore.InMemory ref (10.0.4) — the LaundryGharDbContext builds clean on InMemory because the only Postgres-only mapping (Item search_tokens tsvector) is Ignore()d.
