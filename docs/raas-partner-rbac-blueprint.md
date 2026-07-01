# RaaS Partner Mini-RBAC — Implementation Blueprint (issue #14 · docs/rbac.md §9)

> Design output of the RBAC Phase 5 planning workflow. **Not yet implemented.** Mirrors two proven repo patterns: brand-tenancy RLS (for `partner_id` isolation) and the customer separate-actor auth (for the partner principal). Partners are **not** staff — they never enter `users`/`user_scope_memberships`/`ScopeResolver`.

## Summary

RaaS partner mini-RBAC = a NEW separate-actor principal (partners) isolated by partner_id, built by mirroring two proven repo patterns: (1) brand-tenancy RLS (kernel.current_brand_id() + rls_brand policy + session-var plumbing) → clone as partner_id RLS; (2) the customer separate-actor auth (CustomerTokenClaims/token_use=customer + CustomerOnlyRequirement + CreateCustomerAccessToken) → clone as token_use=partner. Partners are NOT staff: they never enter users/user_scope_memberships/ScopeResolver, and the staff PermissionHandler (laundryghar.Utilities/Auth/PermissionHandler.cs Gate 1 requires token_use=user) can never authorize a partner token — so partner_admin vs partner_operator is enforced by a partner_role CLAIM via PartnerOnly/PartnerAdmin requirement handlers, not by role_permissions. Ownership: bookings + partner principal live in the OPERATIONS host (logistics schema, IOperationsDbContext); wallet + invoices live in the COMMERCE host (commerce schema, ICommerceDbContext), carrying partner_id as a scalar cross-BC key exactly as WalletTransaction already carries order_id. The single most important correctness lesson from the codebase: app.current_customer_id is declared in SQL (kernel.current_customer_id, rls_proposal.sql:81-83) but NEVER set by the interceptor (RlsConnectionInterceptor.cs:59-66 sets only brand/franchise/store/user/bypass), so the B2 customer-self RLS is INERT. The partner build MUST thread partner_id end-to-end (ICurrentTenant → interceptor set_config → kernel.current_partner_id() → rls_partner policy) or every partner sees every partner's bookings. MVP proves exactly this loop end-to-end with the smallest footprint (login + create/list bookings + a cross-partner leakage test); the full build layers on wallet, invoices, Razorpay, cancel/track via delivery legs, and the seeded catalog roles.

## Data model

SIX new entities, each cloned from a verified template.

OPERATIONS host (logistics schema, add DbSets to backend/laundryghar/operations.Application/Common/Interfaces/IOperationsDbContext.cs alongside Riders:57 / RiderAssignments:59 / Orders:46):
1. Partner (logistics.partners) — the isolation key + org record. Clone the shape of laundryghar.SharedDataModel/Entities/Logistics/Rider.cs (Id, Code, Status, ISoftDeletable, CreatedAt/UpdatedAt/CreatedBy). Columns: id (pk), code (unique), legal_name, status, contact_email/phone, optional brand_id (see open decision on brand scoping), deleted_at, audit cols. partner_id (=id) is THE RLS key.
2. PartnerUser (logistics.partner_users) — login principals; a partner org has many users (unlike customers which collapse org+login into one row). Columns: id (pk, = JWT sub), partner_id (FK→partners), phone_e164/email, partner_role text ('partner_admin'|'partner_operator'), status, audit. This is where partner_role lives authoritatively.
3. PartnerBooking (logistics.partner_bookings) — clone laundryghar.SharedDataModel/Entities/Logistics/RiderAssignment.cs. Columns: id (pk), partner_id (FK→partners, THE rls_partner key), brand_id (the brand whose fleet serves the leg), created_by (partner_user_id), pickup/drop address snapshot (jsonb or flat cols), quoted_fare, status (requested|assigned|in_progress|completed|cancelled), wallet_txn_id (scalar ref into commerce), created_at/updated_at.

RELATION TO RIDER/ORDER: a booking dispatches to a rider by spawning a delivery leg. Reuse the existing per-leg dispatch machinery in laundryghar.SharedDataModel/Entities/OrderLifecycle/DeliveryAssignment.cs (already carries rider_id + OTP/proof/track fields used by GetRiderTrack) by adding a NULLABLE partner_booking_id FK and making order_id nullable (or use a discriminator) so a RaaS leg reuses rider assignment/track/OTP/proof WITHOUT an order_id. This avoids reinventing rider dispatch. (See open decision: reuse delivery_assignments vs standalone dispatch table.)

COMMERCE host (commerce schema, add DbSets to backend/laundryghar/commerce.Application/Common/Interfaces/ICommerceDbContext.cs alongside WalletAccounts:68 / WalletTransactions:69):
4. PartnerWalletAccount (commerce.partner_wallet_accounts) — clone laundryghar.SharedDataModel/Entities/Commerce/WalletAccount.cs: swap customer_id→partner_id (drop Customer nav), keep available_balance GENERATED ALWAYS AS (balance - locked_balance) STORED, unique(partner_id).
5. PartnerWalletTransaction (commerce.partner_wallet_transactions) — clone laundryghar.SharedDataModel/Entities/Commerce/WalletTransaction.cs: append-only ledger, direction ±1 (smallint CHECK), unique idempotency_key, reference_type='partner_booking' + reference_id=booking_id for per-booking debits.
6. PartnerInvoice (commerce.partner_invoices) — clone laundryghar.SharedDataModel/Entities/FinanceRoyalty/Subscriptions/FranchiseSubscriptionInvoice.cs (the platform→tenant billing shape): partner_id, billing_period_start/end, jsonb line_items (bookings), TaxBreakdown Tax + TaxTotal, GrandTotal, AmountPaid, AmountDue GENERATED ALWAYS AS (grand_total - amount_paid) STORED, InvoicePdfUrl; add razorpay_payment_link_id/payment_link_url columns modeled on db/patches/phase4_brand_platform_invoice_paylink.sql.

partner_id FKs: partner_bookings.partner_id → logistics.partners(id); partner_wallet_accounts/partner_wallet_transactions/partner_invoices.partner_id are SCALAR cross-BC keys → logistics.partners (no EF nav across contexts, mirroring how WalletTransaction.OrderId is a scalar composite FK). RefreshToken (laundryghar.SharedDataModel/Entities/IdentityAccess/RefreshToken.cs) currently has UserId? + CustomerId? only — add a nullable PartnerUserId (or partner_id) column so partner refresh tokens can persist.

## RLS isolation (the load-bearing part)

Clone the brand-RLS pipeline verbatim, swapping brand_id→partner_id. Four moving parts:

(A) SQL helper + policy (new patch db/patches/rls_partner.sql, cloned from db/patches/rls_proposal.sql):
- kernel.current_partner_id() — exact clone of kernel.current_brand_id() (rls_proposal.sql:65-67): `CREATE OR REPLACE FUNCTION kernel.current_partner_id() RETURNS uuid LANGUAGE sql STABLE AS $$ SELECT NULLIF(current_setting('app.current_partner_id', true), '')::uuid $$;`
- GRANT EXECUTE ON FUNCTION kernel.current_partner_id() TO app_user, app_admin (clone rls_proposal.sql:112-117).
- rls_partner policy per partner-owned table — exact clone of the rls_brand body (rls_proposal.sql:216-221): `CREATE POLICY rls_partner ON <schema>.<tbl> FOR ALL TO app_user USING (kernel.rls_bypass() OR partner_id = kernel.current_partner_id()) WITH CHECK (kernel.rls_bypass() OR partner_id = kernel.current_partner_id());` on logistics.partner_bookings + commerce.partner_wallet_accounts/partner_wallet_transactions/partner_invoices. (logistics.partners itself is optionally rls_partner-on-id, or platform-admin-only.) Note the simpler B1 brand shape is the right template, NOT the B2 brand+customer combo (rls_proposal.sql:258-282).
- Per-table activation: a new db/patches/rls_enable_partner.sql cloned from db/patches/rls_enable_logistics.sql doing ALTER TABLE <tbl> ENABLE ROW LEVEL SECURITY for the 4 tables.
- app_user auto-inherits CRUD on new logistics/commerce tables via ALTER DEFAULT PRIVILEGES in harden_app_user_and_rls_bypass.sql:49-69 — no per-table GRANT needed.

(B) Session-var plumbing (the part the customer path FORGOT — must not repeat):
- Add `Guid? PartnerId { get; }` to backend/laundryghar/laundryghar.SharedDataModel/Contracts/ICurrentTenant.cs (after line 12).
- backend/laundryghar/laundryghar.Utilities/Services/HttpContextCurrentTenant.cs: add `public Guid? PartnerId => GetGuid("partner_id");` (mirror line 24).
- backend/laundryghar/laundryghar.SharedDataModel/Persistence/Interceptors/RlsConnectionInterceptor.cs BuildSetConfigCommand (lines 50-73): add `var partnerId = _currentTenant.PartnerId?.ToString() ?? string.Empty;`, a 6th line `set_config('app.current_partner_id', @partner_id, false)` in the SQL, and `AddParameter(cmd, "@partner_id", partnerId)`.
- Adding PartnerId to the interface REQUIRES implementing it in the two Worker adapters (backend/laundryghar/commerce.Infrastructure/Worker/WorkerCurrentTenant.cs → `PartnerId => null` with BypassRls=true already; backend/laundryghar/commerce.Infrastructure/Worker/CommerceHostCurrentTenant.cs → `PartnerId => null`) and the test fake (backend/laundryghar/tests/operations.IntegrationTests/Rbac/Fakes.cs FakeCurrentTenant line 9 → add `PartnerId { get; init; }`), or the solution won't compile.

(C) Bypass correctness (verified, no change needed): the interceptor writes bypass as "true"/"false" (RlsConnectionInterceptor.cs:56); the AUTHORITATIVE kernel.rls_bypass() in harden_app_user_and_rls_bypass.sql:31-38 accepts 'on','true','1','yes','t', so platform_admin bypass over partner rows works. (The older rls_proposal.sql:85-87 version only matched 'on' — the partner patch should be applied AFTER the harden patch, same as the rest of the stack.)

(D) The partner token MUST leave brand_id/bypass unset so brand RLS returns nothing and ONLY rls_partner governs visibility. Set app.current_partner_id from the partner_id claim; do not set app.bypass_rls for partners (TenantResolutionMiddleware grants bypass only to platform_admin — partner tokens correctly get neither bypass nor the token_use=user perm-version revocation path).

VERIFY: clone backend/laundryghar/tests/operations.IntegrationTests/Rbac/RbacRlsFixture.cs (defines kernel.current_* fns + hardened rls_bypass) into a partner_id cross-partner leakage test: seed 2 partners + bookings, set app.current_partner_id=P1, assert only P1 rows are visible.

## Auth & roles

SEPARATE-ACTOR auth cloned from the customer stack (NOT staff RBAC).

CLAIMS — backend/laundryghar/laundryghar.Utilities/Auth/TokenClaims.cs: add `PartnerTokenClaims(Guid PartnerUserId, Guid PartnerId, string PartnerRole, Guid? BrandId, string? Phone)` cloned from CustomerTokenClaims (lines 57-72) with `public const string TokenUseValue = "partner";`. NO permissions claim (partner capability is one coarse role).

MINT — backend/laundryghar/core.Infrastructure/Auth/JwtTokenService.cs: add `CreatePartnerAccessToken(PartnerTokenClaims)` cloned from CreateCustomerAccessToken (lines 74-90), emitting sub=partner_user_id, jti, token_use=partner, partner_id, partner_role, and (only if brand-scoped) brand_id — reusing the RS256 WriteToken (114-124). Declare it on backend/laundryghar/core.Application/Common/Interfaces/IJwtTokenService.cs (beside CreateCustomerAccessToken line 12). CRITICAL: do NOT emit brand_id unless the partner is brand-scoped, and never a permissions claim, so brand/staff RLS/authz stays inert for partner tokens.

TOKEN_USE GATE — backend/laundryghar/laundryghar.Utilities/Auth/CustomerOnlyRequirement.cs is the exact template: add PartnerOnlyRequirement/PartnerOnlyHandler that Succeed() iff claim token_use=="partner". For the admin/operator split, clone the TWO-claim pattern in backend/laundryghar/laundryghar.Utilities/Auth/RiderOnlyRequirement.cs (token_use=user AND user_type=rider, lines 24-26) → PartnerAdminOnlyHandler: token_use=="partner" AND partner_role=="partner_admin".

POLICY NAMES — backend/laundryghar/laundryghar.Utilities/Auth/PermissionPolicyProvider.cs: add consts `PartnerOnlyPolicy="PartnerOnly"` / `PartnerAdminPolicy="PartnerAdmin"` (beside lines 17-19) and two GetPolicyAsync branches cloned from the CustomerOnly (62-70) / RiderOnly (72-80) blocks, each adding the new requirement. Route+group RequireAuthorization compose with AND semantics (verified in RiderSelfEndpoints.cs: group RequireAuthorization("RiderOnly") + per-route permission), so group-gate "PartnerOnly" and add "PartnerAdmin" on admin-only routes.

HANDLER DI — register the new handlers as IAuthorizationHandler singletons in BOTH backend/laundryghar/core.WebApi/Program.cs (beside lines 244-249) and backend/laundryghar/operations.WebApi/Program.cs (beside lines 99-102); wallet/invoice routes also need them in commerce.WebApi/Program.cs.

LOGIN — clone backend/laundryghar/core.WebApi/Endpoints/Identity/CustomerAuth.cs (IEndpointGroup at /api/v1/customer/auth) into a PartnerAuth group at /api/v1/partner/auth (otp/send + otp/verify + refresh anon; logout + me RequireAuthorization("PartnerOnly")). Clone the login handler from backend/laundryghar/core.Application/Identity/Auth/Commands/CustomerOtpVerify/CustomerOtpVerifyHandler.cs (mint at 187-191, refresh persist at 198-209): resolve the partner_user row (lookup, NOT find-or-create), mint via CreatePartnerAccessToken, persist a RefreshToken keyed by the new PartnerUserId column.

ROLES / PERMS / SCOPE_TYPE — partner_admin vs partner_operator is enforced by the partner_role CLAIM (from partner_users.partner_role) + the PartnerAdmin handler, because the staff PermissionHandler.cs Gate 1 rejects any non-user token. Therefore for the MVP NO scope_type CHECK change and NO ScopeType enum change are required (partner_role is a plain string column/claim). In the FULL build, optionally seed the catalog so docs §4/§10 and the role-management UI stay truthful (Option B): add ScopeType.LogisticsPartner="logistics_partner" to backend/laundryghar/laundryghar.SharedDataModel/Enums/ScopeType.cs; relax the roles.scope_type CHECK via a patch cloned byte-for-byte from db/patches/rbac_roles_scope_territory.sql adding 'logistics_partner' (live set today is platform/brand/territory/franchise/store/warehouse); and in backend/laundryghar/core.Infrastructure/Seeders/IdentitySeeder.cs append 8 partner_booking.* permission tuples to PermissionDefs (pattern at :77), two roles to RoleDefs (pattern at :370-394, ScopeType.LogisticsPartner, priorities 120/130 after support=110, VerticalKey.Logistics), and two Grant() calls in SeedRolePermissionsAsync (:452): partner_admin → all 8 (read/create/track/cancel + wallet.read/wallet.topup + invoice.read/invoice.export); partner_operator → read/create/track/cancel + wallet.read only (NO wallet.topup, NO invoice.*, matching §13 "operator sees but can't top up"). Do NOT add any user_scope_memberships row and do NOT touch ScopeResolver — the user_scope_memberships.user_id FK→users ON DELETE CASCADE (database_scripts/02_bc2_identity_access.sql:111) would force partners to be fake users. Also update the stale IdentitySeeder.cs:391-392 and :700-702 comments.

## Endpoints

Group-gate the whole partner surface with RequireAuthorization("PartnerOnly") (mirrors RiderSelfEndpoints.cs:52), then add RequireAuthorization("PartnerAdmin") on the wallet-mutation + invoice routes (AND-composed). New IEndpointGroup classes:

PARTNER AUTH — backend/laundryghar/core.WebApi/Endpoints/Identity/PartnerAuth.cs at /api/v1/partner/auth (clone CustomerAuth.cs):
  POST /otp/send (anon), POST /otp/verify (anon → returns partner JWT), POST /refresh (anon), POST /logout (PartnerOnly), GET /me (PartnerOnly).

PARTNER BOOKINGS — operations.WebApi/Endpoints/Logistics/PartnerBookingEndpoints.cs at /api/v1/partner (clone RiderAssignmentsAdmin.cs route shape + RiderSelfEndpoints group gating), group RequireAuthorization("PartnerOnly"):
  partner_operator + partner_admin (both roles):
    POST /partner/bookings          — create booking (partner_booking.create)
    GET  /partner/bookings          — list OWN bookings (RLS-filtered)
    GET  /partner/bookings/{id}     — read own booking
    GET  /partner/bookings/{id}/track — live track (reuses rider track via the delivery leg)
    POST /partner/bookings/{id}/cancel — cancel own booking
    GET  /partner/wallet            — READ balance only (per §13: operator sees but can't top up)

PARTNER WALLET + INVOICES — commerce.WebApi/Endpoints/Commerce/PartnerBillingEndpoints.cs at /api/v1/partner, group RequireAuthorization("PartnerOnly"), each route additionally RequireAuthorization("PartnerAdmin"):
  partner_admin ONLY:
    POST /partner/wallet/top-up            — Razorpay payment-link top-up
    GET  /partner/wallet/transactions      — ledger
    GET  /partner/invoices                 — list
    GET  /partner/invoices/{id}            — read
    GET  /partner/invoices/{id}/pdf        — download
    POST /partner/invoices/{id}/pay        — payment link

The GET /partner/wallet (read balance) is PartnerOnly (both roles) so operators see the balance; every mutation and all invoice routes are the PartnerAdmin lane — this is the concrete partner_admin vs partner_operator boundary.

## Steps (MVP-1..7, then FULL-8..12)

- **MVP-1 (foundation): thread partner_id session var end-to-end — add PartnerId to ICurrentTenant, HttpContextCurrentTenant, RlsConnectionInterceptor set_config; update both Worker adapters + test fake so the solution compiles**  
  `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.SharedDataModel/Contracts/ICurrentTenant.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.Utilities/Services/HttpContextCurrentTenant.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.SharedDataModel/Persistence/Interceptors/RlsConnectionInterceptor.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/commerce.Infrastructure/Worker/WorkerCurrentTenant.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/commerce.Infrastructure/Worker/CommerceHostCurrentTenant.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/tests/operations.IntegrationTests/Rbac/Fakes.cs`
- **MVP-2 (DB isolation): new SQL patches — kernel.current_partner_id() + GRANT (clone rls_proposal.sql:65-67,112-117); rls_partner policy on logistics.partner_bookings (clone rls_brand:216-221); ENABLE RLS patch (clone rls_enable_logistics.sql)**  
  `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/db/patches/rls_partner.sql`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/db/patches/rls_enable_partner.sql`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/db/patches/rls_proposal.sql`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/db/patches/harden_app_user_and_rls_bypass.sql`
- **MVP-3 (data model, operations): Partner + PartnerUser + PartnerBooking entities (clone Rider.cs / RiderAssignment.cs) + EF configs + DbSets on IOperationsDbContext + EF migration**  
  `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.SharedDataModel/Entities/Logistics/Rider.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.SharedDataModel/Entities/Logistics/RiderAssignment.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/operations.Application/Common/Interfaces/IOperationsDbContext.cs`
- **MVP-4 (auth claims + mint): PartnerTokenClaims (clone CustomerTokenClaims) + CreatePartnerAccessToken (clone CreateCustomerAccessToken) + IJwtTokenService decl; add nullable PartnerUserId to RefreshToken**  
  `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.Utilities/Auth/TokenClaims.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/core.Infrastructure/Auth/JwtTokenService.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/core.Application/Common/Interfaces/IJwtTokenService.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.SharedDataModel/Entities/IdentityAccess/RefreshToken.cs`
- **MVP-5 (token_use gate + policy + DI): PartnerOnlyRequirement/Handler (clone CustomerOnlyRequirement) + PermissionPolicyProvider PartnerOnly branch + register handler in core/operations Program.cs**  
  `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.Utilities/Auth/CustomerOnlyRequirement.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.Utilities/Auth/PermissionPolicyProvider.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/core.WebApi/Program.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/operations.WebApi/Program.cs`
- **MVP-6 (login + booking endpoints): PartnerAuth group (clone CustomerAuth) + partner login handler (clone CustomerOtpVerifyHandler) + PartnerBookingEndpoints POST/GET (clone RiderSelfEndpoints group gate + RiderAssignmentsAdmin route shape)**  
  `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/core.WebApi/Endpoints/Identity/CustomerAuth.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/core.Application/Identity/Auth/Commands/CustomerOtpVerify/CustomerOtpVerifyHandler.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/operations.WebApi/Endpoints/Logistics/RiderSelfEndpoints.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/operations.WebApi/Endpoints/Logistics/RiderAssignmentsAdmin.cs`
- **MVP-7 (prove isolation): cross-partner leakage integration test (clone RbacRlsFixture) — seed 2 partners+bookings, set app.current_partner_id=P1, assert only P1 rows visible; this is the end-to-end acceptance gate**  
  `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/tests/operations.IntegrationTests/Rbac/RbacRlsFixture.cs`
- **FULL-8 (role split): PartnerAdminOnlyHandler (clone RiderOnlyRequirement two-claim) + PartnerAdmin policy branch; apply PartnerAdmin to admin routes; add partner_role to token + partner_users**  
  `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.Utilities/Auth/RiderOnlyRequirement.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.Utilities/Auth/PermissionPolicyProvider.cs`
- **FULL-9 (wallet, commerce): PartnerWalletAccount + PartnerWalletTransaction (clone WalletAccount/WalletTransaction, partner_id key) + DbSets on ICommerceDbContext + rls_partner policies + ENABLE; per-booking debit ledger**  
  `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.SharedDataModel/Entities/Commerce/WalletAccount.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.SharedDataModel/Entities/Commerce/WalletTransaction.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/commerce.Application/Common/Interfaces/ICommerceDbContext.cs`
- **FULL-10 (invoices + Razorpay, commerce): PartnerInvoice (clone FranchiseSubscriptionInvoice) + payment-link cols (clone phase4 paylink) + wallet top-up + invoice pay endpoints (PartnerAdmin)**  
  `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.SharedDataModel/Entities/FinanceRoyalty/Subscriptions/FranchiseSubscriptionInvoice.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/db/patches/phase4_brand_platform_invoice_paylink.sql`
- **FULL-11 (rider dispatch): add nullable partner_booking_id FK to DeliveryAssignment (+ make order_id nullable) so RaaS bookings reuse rider assignment/track/OTP/proof; wire booking→leg spawn + track endpoint**  
  `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.SharedDataModel/Entities/OrderLifecycle/DeliveryAssignment.cs`
- **FULL-12 (truthful catalog, optional): ScopeType.LogisticsPartner + roles.scope_type CHECK relax (clone territory patch) + seed 8 partner_booking.* perms + 2 roles + Grants in IdentitySeeder; update stale comments**  
  `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/laundryghar.SharedDataModel/Enums/ScopeType.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/db/patches/rbac_roles_scope_territory.sql`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/backend/laundryghar/core.Infrastructure/Seeders/IdentitySeeder.cs`, `/Users/gtmkumar/Documents/source/laundry_ghar/laundryghar/docs/rbac.md`

## Effort / MVP split

Rough sizing (1 dev; S≈≤0.5d, M≈1-2d, L≈3-5d).

MVP SLICE (smallest thing that proves partner isolation end-to-end = a partner logs in, creates+lists bookings, and CANNOT see another partner's bookings via rls_partner) = Steps MVP-1..MVP-7:
- MVP-1 session-var plumbing: S (3 edits + 3 impl updates; mechanical but touches a shared interface so all hosts must rebuild).
- MVP-2 DB patches: S (verbatim clones; test idempotency + apply order after harden patch).
- MVP-3 entities+migration: M (3 entities + EF configs + migration; PartnerBooking is the RiderAssignment clone).
- MVP-4 claims+mint: S.
- MVP-5 gate+policy+DI: S.
- MVP-6 login+booking endpoints: M (login handler clone + booking create/list; keep booking minimal — no dispatch yet, status stays 'requested').
- MVP-7 leakage test: S-M (the acceptance gate).
MVP total ≈ 4-6 dev-days. This delivers the issue #14 core claim ("partners see ONLY their own bookings via RLS on partner_id") and is independently demoable/greenlightable.

FULL BUILD adds Steps 8-12:
- FULL-8 role split (partner_admin/operator via partner_role claim + PartnerAdmin handler): S-M.
- FULL-9 wallet (account+ledger+debit-on-booking, commerce, +RLS): M-L (cross-BC debit is the tricky part — see open decisions).
- FULL-10 invoices + Razorpay payment links + PDF: L (reuses the existing paylink/webhook plumbing but is the biggest surface).
- FULL-11 rider dispatch via delivery legs (track/OTP/proof reuse): M-L (schema change to a hot partitioned table order_lifecycle.delivery_assignments — careful migration).
- FULL-12 truthful catalog seeding + scope_type/enum (optional): S.
FULL total (on top of MVP) ≈ 8-13 dev-days → whole feature ≈ 12-19 dev-days.

Suggested slicing for review: greenlight MVP first (proves the security model), then wallet (FULL-8+9), then invoices/dispatch (FULL-10+11) as separate PRs. FULL-12 can ship anytime or be dropped if the role split stays claim-only.

## Open decisions (with recommendations)

### Partner login mechanism: OTP/phone (fully-templated by the customer flow) vs API-key / client-credentials (natural for B2B server-to-server partners)
- **Options:** (a) Clone the customer OTP flow (CustomerOtpVerifyHandler) — zero new infra, but assumes a human partner_user with a phone; (b) issue per-partner API keys / OAuth2 client_credentials for machine callers — better B2B ergonomics but new issuance/rotation code
- **Recommendation:** MVP: clone OTP for partner_users (it is the only fully-built separate-actor login template, minimizing new code and proving the principal). Add API-key issuance in the full build once the portal exists. Keep CreatePartnerAccessToken mechanism-agnostic so either front-end can mint the same token.

### partner_admin vs partner_operator enforcement: coarse partner_role claim + PartnerAdmin handler vs the seeded catalog roles/role_permissions
- **Options:** (a) partner_role string claim (from partner_users.partner_role) gated by a PartnerAdminOnly requirement handler — the staff PermissionHandler rejects non-user tokens anyway; (b) also seed identity_access.roles/role_permissions + relax scope_type CHECK + add ScopeType.LogisticsPartner so the role-management UI and docs §4/§10 stay truthful
- **Recommendation:** Enforce at RUNTIME with (a) — a claim + handler — for both MVP and full build. Do (b) additively ONLY in the full build to keep the catalog/UI truthful; never route partner authz through role_permissions/ScopeResolver. This avoids fake users and a needless scope_type migration for the MVP.

### Is a partner platform-level or brand-scoped? (docs §3 draws Logistics Partners as a SEPARATE node not under a brand)
- **Options:** (a) Platform-level partner principal (no brand_id on the partner token); each booking carries brand_id = the brand whose fleet serves it; (b) partner belongs to exactly one brand (brand_id on partners + on the token)
- **Recommendation:** (a) platform-level principal, booking-level brand_id. The partner token then sets app.current_partner_id but NOT app.current_brand_id, so ONLY rls_partner governs visibility and a partner can book across brands' fleets. Revisit if the business ties each partner to a single brand.

### Booking→rider dispatch: reuse order_lifecycle.delivery_assignments (add nullable partner_booking_id, make order_id nullable) vs a standalone partner dispatch table
- **Options:** (a) Reuse delivery_assignments — inherits rider assignment + OTP + proof + GetRiderTrack for free, but requires making order_id nullable on a hot, partitioned, brand-RLS table and adding a partner_booking_id FK; (b) new logistics.partner_dispatch table mirroring the rider-leg fields — clean separation, but duplicates track/OTP/proof machinery
- **Recommendation:** (a) reuse delivery_assignments for the full build (track/OTP/proof reuse is the whole point of RaaS-on-existing-fleet). Guard order_id-nullability with a CHECK (exactly one of order_id / partner_booking_id set). MVP keeps bookings dispatch-free (status='requested'), so this decision is deferred out of the MVP.

### Per-booking wallet debit spans bounded contexts (booking in OPERATIONS must debit the wallet in COMMERCE)
- **Options:** (a) Synchronous cross-host call operations→commerce at booking time; (b) transactional outbox event (kernel.outbox_events already exists) consumed by the commerce worker to post the debit; (c) co-locate the partner wallet in operations instead of commerce
- **Recommendation:** (b) outbox event for the full build — matches the existing worker/outbox pattern and keeps the debit idempotent (PartnerWalletTransaction.idempotency_key = booking_id). Enforce prepaid-balance-sufficiency at booking time via a lightweight balance read; settle the ledger async. MVP omits wallet entirely, so this is a full-build decision.

### Where to register the partner authorization handlers (PartnerOnly / PartnerAdmin) given endpoints span 3 hosts
- **Options:** (a) Register in every host Program.cs that hosts a partner route (core for auth, operations for bookings, commerce for wallet/invoices); (b) fold registration into a shared AddPartnerAuth() extension called by all hosts
- **Recommendation:** (b) add a shared extension in laundryghar.Utilities (mirroring AddCurrentTenant) so no host silently misses a handler singleton and returns 403 on a valid partner token — a subtle failure mode given the customer/rider handlers are hand-registered per host today (core:246-249, operations:99-102).


---
🤖 Blueprint generated by a Claude Code multi-agent design workflow.
