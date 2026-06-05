---
name: project-bc3-catalog
description: BC-3 Catalog service QA gate results — customer IDOR, pricing, token separation, RLS
metadata:
  type: project
---

BC-3 validated 2026-06-05. Catalog service on port 5001, Identity on 5000. Both require `ASPNETCORE_ENVIRONMENT=Development` env var and `ASPNETCORE_URLS=http://localhost:5001` for Catalog.

**Exit gate: PASSED** — customer-self isolation holds (no IDOR), pricing/publish correct, token separation enforced, RLS intact.

**Key facts:**
- Customer OTP auth endpoint is at `/api/v1/customer/auth/otp/send` (NOT `/api/v1/auth/otp/send`). The `purpose` field in system OTP does not accept `customer_login`; customer auth uses its own endpoint which only takes `phone` + optional `brandCode`.
- `account_deletion_requests.request_source` has a CHECK constraint: only `mobile_app`, `web`, `support`, `email`, `phone` are valid. The API accepts any string and propagates the constraint error as HTTP 400 (raw DB error exposed — DEF-001 BC-3).
- Published price list edit returns HTTP 500 (InvalidOperationException) instead of HTTP 409/422. Should be a client error status code — DEF-002 BC-3.
- `tenancy_org.stores` requires `franchise_id NOT NULL` and has no `address` jsonb column (separate address columns). `metadata` column does not exist. Schema differs from what was assumed.
- customer_catalog RLS uses `rls_brand_or_customer` policy (`{app_user}`) — but service connects as postgres (superuser) so RLS bypass is expected. IDOR protection is entirely app-layer (self-filter on CustomerId = sub claim).
- DPDP-immutable rows (`dpdp_consents`, `account_deletion_requests`, `customers`) left in DB by design.
- Price resolution rule confirmed: store > franchise > brand scope; variant-specific > null-variant within same scope.

**Why:** Validates BC-3 customer catalog and IDOR isolation before BC-4+ order flows are built.
**How to apply:** When reviewing customer endpoints always check self-filter (`CustomerId = sub claim`) is present. Watch for raw DB constraint errors surfacing as 500s.

See also: [[project-wave0-rls]] for Identity/RLS baseline.
