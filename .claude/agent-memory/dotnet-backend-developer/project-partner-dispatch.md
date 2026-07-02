---
name: project-partner-dispatch
description: FULL-11b partner dispatch — dual-visibility RLS (rls_partner_or_brand); assign handler server-verifies partner/brand attribution via a transaction-scoped SET LOCAL app.bypass_rls read (PR #16 fix)
metadata:
  type: project
---

RaaS partner-booking rider dispatch (FULL-11b, issue #14) lives in a NEW SEPARATE table
`logistics.partner_dispatches` — the hot partitioned `order_lifecycle.delivery_assignments` was
deliberately left untouched; track/OTP/proof/location fields are REPLICATED, not referenced.

**Dual-visibility RLS is the crux.** Policy `rls_partner_or_brand` = `rls_bypass() OR partner_id =
current_partner_id() OR (brand_id IS NOT NULL AND brand_id = current_brand_id())`. The `brand_id IS
NOT NULL` guard is load-bearing: without it a NULL-brand row would match a brand session whose
`current_brand_id()` is also NULL.

**Assign handler now server-verifies attribution via a scoped RLS bypass (PR #16 review fix,
2026-07-01).** `logistics.partner_bookings` has partner-only RLS (`rls_partner`), so a BRAND-STAFF
session (partner_id NULL) SELECTs *zero* bookings — it can't derive partner_id/brand under normal RLS.
The old handler therefore TRUSTED `request.PartnerId` and set `brand_id` from the session, which let a
staff session mis-attribute a dispatch to any partner/booking (the combined `rls_partner_or_brand`
WITH CHECK only enforces the brand arm, never the partner arm). FIX: `AssignPartnerDispatchHandler`
now runs inside `ExecuteInTransactionAsync`, does `SET LOCAL app.bypass_rls='true'` (honoured by
`kernel.rls_bypass()`, reverts on commit — the codebase's transaction-scoped bypass idiom for HTTP
handlers), reads the ONE booking (`AsNoTracking` projection), then: derives `partner_id` FROM the
booking (rejects a mismatched `request.PartnerId` with `ForbiddenException`); verifies serving
`brand_id == _tenant.BrandId`, else if the booking is unclaimed atomically claims it
(`UPDATE ... WHERE brand_id IS NULL`, 0 rows → someone else claimed → reject); `SET LOCAL
app.bypass_rls='false'` again before the dispatch INSERT so its WITH CHECK still validates the
server-set brand_id. Booking-not-found → `KeyNotFoundException` (clean 404).

**Bypass-read mechanism for HTTP handlers (reusable):** there is NO bypass DbContext for the request
path; `HttpContextCurrentTenant.BypassRls` is only true for platform admins. To do a one-off
privileged read in a normal staff session, wrap in `ExecuteInTransactionAsync` and toggle `SET LOCAL
app.bypass_rls` around the read — scoped to the transaction, never leaks. This is the pattern to
mirror for any future "read a partner-RLS row from a brand session" integrity check.

**Staff gate:** reused existing high-risk `delivery.assign` permission (no new seed). Partner track
endpoint uses only `PartnerOnly` (no permission code) — matches the MVP note that partner authz is
partner_role JWT claim + partner_id RLS, not role_permissions.

Related: [[project-logistics-bc]], [[project-raas-partner-billing]].
