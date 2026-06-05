---
name: project-catalog-service
description: Catalog microservice architecture decisions, DB constraints, runtime quirks, and security remediations from Phase 2 QA
metadata:
  type: project
---

## Security remediations applied (Phase 2 QA)

**C1+M2 — In-handler brand scoping:** Every list/get/update/delete handler in Catalog resolves `RequireBrandId()` and includes `.Where(x => x.BrandId == brandId)` (lists) or `.FirstOrDefaultAsync(x => x.Id == id && x.BrandId == brandId)` (by-id/mutation). This is defense-in-depth ON TOP OF RLS — the app runs as postgres superuser so RLS is bypassed at runtime; in-handler predicates are the primary isolation layer.

**M1 — JWT algorithm pinned:** `ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }` in Catalog Program.cs `TokenValidationParameters`. Prevents algorithm-confusion attacks.

**DEF-001 — Deletion request_source validator:** `CreateDeletionRequestValidator` FluentValidation rule enforces `request_source` ∈ {mobile_app, web, support, email, phone} and returns 422 before hitting the DB constraint.

**DEF-002 — BusinessRuleException → 422:** Added `BusinessRuleException` and `ForbiddenException` to `laundryghar.Utilities/Exceptions/`. ExceptionHandler maps BusinessRuleException → 422 and ForbiddenException → 403. UpdatePriceListHandler throws BusinessRuleException (not InvalidOperationException) for published-list edits.

**H2 — Consent withdraw integrity:** `WithdrawConsentHandler` now looks up an active `granted` consent for `(customerId, brandId, purpose)` before creating a withdrawal row. Returns null → 404 if none found. BrandId always comes from the authenticated token, never hardcoded to Guid.Empty.

**brands.status constraint:** `brands` table allows: active, inactive, suspended (not 'disabled'). Use `suspended` for soft-delete of test brands.

---

## DB CHECK constraints on catalog tables

All discovered at runtime — not in entity classes. Key constraints:

- `add_ons.pricing_type` IN ('flat','percent','per_item','per_kg') — NOT 'fixed'
- `add_ons.status` IN ('active','disabled')
- `price_lists.status` IN ('draft','published','archived') — NOT 'active'. Use 'published' after publish action.
- `dpdp_consents.consent_method` IN ('explicit_checkbox','implicit','imported','signed_form','phone_otp') — NOT 'explicit'
- `account_deletion_requests.request_source` IN ('mobile_app','web','support','email','phone') — NOT 'customer_app'
- `item_variants.side` IN ('left','right','pair','single')

**Why:** Live DB DDL has CHECK constraints not reflected in entity models. Always query pg_constraint when inserting to unfamiliar tables.

**How to apply:** Validate against these values in FluentValidation validators and seed data before inserting.

---

## NameLocalized and Metadata fields are jsonb

Several catalog entities have `NameLocalized` (ServiceCategory, Service, FabricType, ItemGroup, Item, AddOn) mapped as `string` in C# but stored as `jsonb` in Postgres. Callers must pass valid JSON strings e.g. `{"en":"Cotton"}` — plain text strings fail with `22P02: invalid input syntax for type json`.

Customer entities also have `Metadata` as jsonb (Customer, CustomerDevice, DpdpConsent, AccountDeletionRequest) — always seed/insert as `"{}"`.

**Why:** EF Core maps these as string (no native jsonb type for string in EF 10 without custom converter). Npgsql rejects non-JSON on the wire.

**How to apply:** API consumers must send nameLocalized as a JSON string. Seeder must use `{"en":"Name"}` format.

---

## Platform admin brand resolution

Platform admin JWTs have no `brand_id` claim (they are platform-scoped). For admin write operations needing a `brand_id` (create catalog entities, price lists, etc.), callers must pass `X-Brand-Id` header. `TenantResolutionMiddleware` captures it in `HttpContext.Items["brand_id_override"]`. `ICurrentUser.RequireBrandId()` checks override first, then JWT claim, then throws.

**Why:** Platform admins manage multiple brands. Without X-Brand-Id they cannot create brand-scoped catalog data.

**How to apply:** Admin clients must always pass `X-Brand-Id` when platform_admin token is used on catalog write endpoints.

---

## price_lists.status workflow

Status lifecycle: `draft` (on create) → `published` (after POST /publish). Only valid status values: `draft`, `published`, `archived`. Never use `"active"` for price lists. Price list items use their own status: `active`, `inactive`, `archived`.

The price resolution query and customer-facing published-list query must filter by `status = 'published'` not `'active'`.

**Why:** DB constraint and wrong status silently returns empty results for customers.

---

## GetPublishedPriceListQuery returns most-recently-published brand list

Customer-facing `GET /customer/catalog/price-list` returns all items from the single most recently published brand-scoped price list. If the team creates many smoke-test published lists, the customer sees only the latest one's items. For staging/prod: there should be only one active brand list at a time.

---

## Seeded catalog data (Development)

Under brand LG-MAIN (id resolved at runtime):
- 3 service categories: DRY-CLEAN, LAUNDRY, STEAM-IRON
- 3 services: SVC-DRY-CLEAN, SVC-LAUNDRY, SVC-STEAM-IRON  
- 3 fabric types: COTTON, SILK, WOOLEN
- 2 item groups: MEN, WOMEN
- 3 items: SHIRT, TROUSER, SAREE
- 3 item variants: SHIRT-COTTON, SHIRT-SILK, TROUSER-STD
- 1 add-on: STAIN-TREAT (pricing_type=flat)
- 1 published price list: MAIN-BRAND-PL (4 items: dry-clean/laundry × shirt/trouser)

Natural key for idempotency: (brand_id, code) on all entities.

---

## Customer token self-filtering

All customer endpoints extract `sub` via `ClaimTypes.NameIdentifier` (JwtBearer remaps sub → NameIdentifier). Customer queries/mutations must explicitly filter by `customerId = sub` at the app layer — RLS provides brand isolation but NOT per-customer row isolation within a brand.

Default-address flag: when setting a new default, `ExecuteUpdateAsync` unsets all other defaults atomically within the same customer scope before inserting/updating the new one.

---

## Catalog service port: 5001 (Identity: 5000)
