---
name: admin-api-auth-model
description: How admin-web API auth/tenancy behaves under test — X-Brand-Id requirement, pagination shape, 401-vs-403 semantics, step-up gating
metadata:
  type: reference
---

Facts confirmed by a full GET smoke sweep (61 admin GET endpoints x 3 roles) plus write flows.
Backend is 3 standalone hosts serving `/api/v1/admin/...`; frontend clients map to them via
`admin-web/src/api/client.ts` env vars (real deploy proxies through an `:8080` gateway with
`/core`, `/ops`, `/commerce` path prefixes — see `.env.local`).

- **X-Brand-Id is mandatory for platform_admin** on every brand-scoped resource (catalog, orders,
  pickups, warehouse, riders, commerce, finance, analytics). platform_admin's JWT has NO `brand_id`
  claim, so without the header these endpoints throw an app-level 401 `UnauthorizedAccessException`
  (JSON body `{"errorMessage":{"UnauthorizedAccessException":["Unauthorized."]}}`). Get a brandId from
  `GET /api/v1/admin/brands` then send `-H 'X-Brand-Id: <id>'`. Platform-level endpoints
  (brands, platforms, platform-plans, franchise-subscriptions, rider-payout-requests, entitlements,
  settings) work WITHOUT the header.
- **Two distinct 401 sources — don't conflate them.** Empty-body 401 = ASP.NET auth challenge
  (no/invalid token). JSON-body 401 `UnauthorizedAccessException` = app code failing to resolve the
  brand/tenant. The latter looks like an auth bug but is really missing brand context.
- **Paginated list response shape is `data.list`** (with `pageNumber`/`pageCount`/`totalCount`),
  NOT `data.items`/`data.results`. Parse `data.list` when extracting ids from list calls.
- **Localized name fields (`nameLocalized`) are `string` columns holding JSON** — send a
  JSON-*stringified* value like `"{\"en\":\"…\"}"`, not a bare string (422 `22P02`) and not a raw
  JSON object (400 "Malformed request body").
- **`stock-reconciliations` returns 403 `step_up_required` even for platform_admin** — step-up (OTP
  re-auth) gating is orthogonal to the platform_admin permission bypass; by design, not an RBAC bug.
- **store_admin seed carries `store_id` but `brand_id: null`** → all brand-scoped reads 401 even with
  an X-Brand-Id header (backend resolves tenant from the JWT `brand_id` claim only). Likely a
  seed/provisioning gap; means store_admin currently can't read catalog/orders/commerce.
- **Missing/mismatched routes found:** `/api/v1/admin/incentive-rules` 404 on all hosts (frontend
  Incentives feature has no backend route); `fulfillment.ts` calls `/fulfillment-config/` but the live
  route is `/api/v1/fulfillment-config/` (missing `/api/v1` prefix — works only via gateway rewrite).
- **`business-settings` GET requires a `category` query param** (general/pricing/order/fulfillment/
  operating_hours); without it → 400 "Malformed request body" (misleading message for a missing param).
- FE interceptor (`client.ts`) treats 401 as "refresh then logout on repeat" — so app-thrown 401s on
  scoped users can cause spurious logout instead of a clean forbidden. See [[api-smoke-sweep-tooling]].
