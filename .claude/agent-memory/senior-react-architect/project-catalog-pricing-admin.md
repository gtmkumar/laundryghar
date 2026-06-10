---
name: project-catalog-pricing-admin
description: Catalog & Pricing admin UI (Task #38) — /pricing rebuilt on house components, full CRUD + price-list editor, backend contracts + gotchas
metadata:
  type: project
---

Task #38 rebuilt admin-web `/pricing` (CatalogPage) from read-only DataTables into full CRUD on FilterableTable/DataTable + FormDrawer. Tabs: Service Categories | Services | Items | Price lists.

**Backend was already complete** (BC-3). laundryghar.Catalog has full admin CRUD for service-categories, services, fabric-types, item-groups, items, item-variants, add-ons, price-lists (+ `/publish`, DELETE), and price-list-items (GET/POST/PUT — **no DELETE**; "remove" = PUT `isActive:false`). Endpoints in `Endpoints/AdminCatalogEndpoints.cs` + `AdminPricingEndpoints.cs`. All permission codes (`catalog.*`, `pricing.*`) already seeded in `identity_access.permissions`. No backend endpoints or permission seeds needed.

**Only backend edit:** added `DisplayLabel NotEmpty` + `MinimumQuantity >= 1` FluentValidators to `Application/Pricing/Commands/PriceListItemCommands.cs` (Create + new Update validator). Reason: seeded price_list_items had NULL display_label → customer app showed UUID fragments. NOT yet live (needs AppHost restart; verified via the `BasePrice>=0` sibling validator which IS live → -5 returns 422). UI enforces label client-side regardless.

**jsonb gotcha (confirmed live):** `name_localized` is jsonb on service_categories/services/items/fabric_types/item_groups/add_ons. Send a JSON-object string `{"en":"…","hi":"…"}` (helpers in `pages/catalog/localized.ts`: buildNameLocalized/parseNameLocalized/displayLocalized). `price_list_items.display_label` is plain varchar (no jsonb).

**DB check constraint gotcha:** `services.pricing_model` CHECK allows only `per_item | per_kg | per_sqft | per_pair | per_side | flat` (NOT "per_piece" — that 400s with 23514). The `PRICING_MODELS` const in `pages/catalog/CatalogDrawers.tsx` matches this. ServiceDto also exposes BaseTatHours/ExpressTatHours (promised-date engine) — surfaced as editable fields.

**Customer price endpoint:** `GET /api/v1/customer/catalog/price-list` (CustomerOnly). Handler `GetPublishedPriceListHandler` returns price_list_items of the newest **brand**-scoped published list (`is_published && scope_type='brand' && status='published'`, ORDER BY published_at DESC) where item `is_active && status='active'`. So only brand-scoped published lists surface to customers; franchise/store lists are for resolve/override priority.

**apiErrorMessage promoted into interceptor:** `src/api/client.ts` response interceptor now overwrites `Error.message` with the flattened 422 validator text (via `apiErrorMessage`) for non-401 errors, so field-level messages surface app-wide. 422 envelope: `response.data.message.errorMessage` = `{ "Request.Field": ["msg"] }`. See [[project-subscriptions-admin-ui]].

Files: `pages/catalog/{CatalogPage,CatalogDrawers,PriceListDrawer,localized}.tsx/.ts`, `api/catalog.ts`, `hooks/useCatalog.ts`, `types/api.ts` (additive), `api/client.ts` (interceptor). Login for live test: POST `http://localhost:5050/api/v1/auth/password/login` `{identifier,password}`; admin@laundryghar.local is platform_admin (no brand_id claim → must send X-Brand-Id header). Catalog svc on :5001.
