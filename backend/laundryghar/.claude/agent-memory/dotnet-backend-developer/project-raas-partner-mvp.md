---
name: project-raas-partner-mvp
description: RaaS partner MVP (issue #14) — new Partner tenant type isolated by partner_id via rls_partner, parallel to Brand/rls_brand; partners-root RLS + provisioning invariant
metadata:
  type: project
---

RaaS (Rider-as-a-Service) partner MVP, issue #14 — a NEW tenant type sitting alongside Brand. Tables: `logistics.partners` (isolation root), `logistics.partner_users` (login principals, id = JWT sub, carries `partner_id` claim), `logistics.partner_bookings` (partner_id = the rls key). Isolation is `partner_id` via `kernel.current_partner_id()` + `rls_partner` policies — a direct parallel to `current_brand_id()`/`rls_brand`. The `RlsConnectionInterceptor` already sets `app.current_partner_id` from `ICurrentTenant.PartnerId`; the data-layer patches are db/patches/raas_partner_schema.sql + db/patches/rls_partner.sql.

**Why:** MVP scoped deliberately to the booking data layer only — NO wallet / invoice / dispatch yet. Future waves extend this.

**How to apply:**
- Load-bearing invariant: the `partners` ROOT table's `rls_partner` keys on `id = current_partner_id()` (a partner session reads only its OWN org row). Provisioning a NEW partner + its first `partner_admin` has no current_partner_id yet, so it MUST run under `app.bypass_rls = true` (platform-admin path) — same treatment as the admin-only `tenancy_org.brands`/`platforms`. Don't expect a plain app_user session to insert a partner.
- `partner_users`/`partner_bookings` isolate by their `partner_id` column (standard rls_brand-shape).
- RLS enforces only when the app connects as `app_user` (non-owner); postgres/superuser bypasses natively. rls_partner.sql ENABLES RLS immediately (unlike inert rls_proposal.sql).
- partner_bookings.brand_id is a soft reference (no FK) — brands are admin-only/cross-schema.
- MVP-6 auth+booking endpoints (done): partner OTP login lives on the CORE host (the token issuer) at /api/v1/partner/auth (send+verify, anon, purpose=`partner_login`); bookings live on OPERATIONS at /api/v1/partner/bookings (PartnerOnly). Verify reuses the StepUp OTP crypto (SEC1 lockout + SEC2 HMAC), then mints via CreatePartnerAccessToken — no refresh token in MVP. Booking create sources partner_id from ICurrentTenant.PartnerId (NOT the request) so rls_partner WITH CHECK can't be spoofed; created_by = JWT sub via ICurrentUser.UserId.
- **Non-obvious gotcha:** the anonymous partner OTP send/verify paths query `partner_users`/`partners`, which have rls_partner ENABLED — but there's no partner context yet (pre-auth). They MUST be added to the core host's `IsScopeResolvingAuthPath` allow-list (core.WebApi/Program.cs) so the pre-auth RLS-bypass middleware fires; otherwise every partner login silently finds no user and 401s. This mirrors how customer/staff OTP+refresh paths are allow-listed. Any new pre-auth partner lookup path needs the same treatment.
- See [[project-consolidation-security-invariants]] and [[saas-platform-model]] for the analogous brand-RLS model.
