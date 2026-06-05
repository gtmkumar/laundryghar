---
name: project-catalog-service
description: Security posture of laundryghar.Catalog (BC-3) — customer self-service, catalog/pricing CRUD, customer-auth in Identity. Brand isolation model + key findings.
metadata:
  type: project
---

BC-3 added laundryghar.Catalog (catalog/pricing CRUD + customer self-service) and customer-auth to Identity (CustomerOtp send/verify, CustomerRefresh/Logout, CustomerOnly policy, customer JWT path with token_use=customer).

**Brand-isolation model (CRITICAL to understand):** Catalog admin + customer handlers do NOT filter queries by brand_id in C# — they delegate cross-brand isolation ENTIRELY to PostgreSQL RLS via RlsConnectionInterceptor session vars. But the service connects as `postgres` superuser (appsettings.json), which BYPASSES RLS, and RLS is not yet ENABLE'd on tables (rls_proposal.sql is inert). So cross-brand isolation is currently NOT enforced for any id-addressable resource. Pattern: every Update/Delete/Publish/GetById handler does `_db.X.FindAsync([id])` with no brand predicate (price lists, items, customers, catalog). This is the dominant cross-tenant risk surface for this service.

**Customer self-service IDOR:** Handled CORRECTLY. CustomerEndpoints reads customer id from JWT sub claim (ClaimTypes.NameIdentifier) via GetCustomerId(), never from body/route. Address/device/consent/deletion handlers all filter `.Where(x => x.CustomerId == cmd.CustomerId)` joined on the authenticated customer. brand_id for writes comes from token brand_id claim, not header.

**Customer OTP brand-binding:** CustomerOtpVerifyHandler binds OTP to brand via ReferenceId==ResolvedBrandId + ReferenceType=="brand". BUT CustomerOtpSendHandler cooldown (lines 39-47) and invalidation (61-67) queries do NOT filter by ReferenceId/brand — cross-brand OTP interactions possible (cooldown shared across brands; OTPs for other brands get invalidated). ResolvedBrandId comes from X-Brand-Id header / brandCode body / default config — client-controlled but validated to exist; OTP+phone bound to brand so can't hijack across brand.

**X-Brand-Id override:** Correctly gated to platform_admin (verified user_type claim) in TenantResolutionMiddleware + ICurrentUser.RequireBrandId. Customer tokens get brand_id from token, not header. This is correct.

**Already-fixed since Wave-0:** rate limiter now present (auth policy), OtpSettings config-driven, CustomerOnly handler registered, seeder has prod guard, CORS env-gated, Catalog has fail-fast on missing SigningKey in non-dev. JWT still HS256 + key in appsettings.Development.json (tracked prod-backlog).

**Still HS256, no ValidAlgorithms pinning** in Catalog Program.cs JWT validation (alg-confusion surface) — same as Identity, prod-backlog.
