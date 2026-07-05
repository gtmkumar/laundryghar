---
name: project-value-slab-pricing
description: GH #22 value-slab pricing — declared-value slabs for branded garments; lane semantics, tax gotcha, nullable PriceListItemId, dynamic permission grant
metadata:
  type: project
---

GH #22 value-slab pricing for branded/luxury garments: an item's `pricing_mode` = 'value_slab'
prices its line from the customer's DECLARED garment value against per-brand
`customer_catalog.value_price_slabs`, bypassing price lists. Slabs are brand data (NOT seeded).

**Why:** one franchise's handwritten rate sheet priced luxury garments by declared value bands
(₹2k–5k→₹200, …, 90k+→₹2200), not flat item×service rates.

**How to apply / non-obvious decisions (verify against code before relying):**
- **Two lanes**: service-specific slabs and brand-wide (`service_id IS NULL`) slabs are SEPARATE
  overlap lanes — a service-specific slab may intentionally overlap a brand-wide one. At
  resolution the service-specific lane is tried first, then falls back to the brand-wide lane.
  Overlap prevention is APP-SIDE (`ValueSlabResolver.EnsureNoOverlapAsync`), not a DB EXCLUDE
  constraint — a range-overlap EXCLUDE with NULL-max open-ended top slabs needs numrange+btree_gist.
- **Boundaries**: `[min_value, max_value)` — min inclusive, max exclusive; NULL max = open-ended top.
- **Tax gotcha**: `CreateOrderHandler` IGNORES `PriceResolver.ResolvedPrice.TaxRatePercent/IsTaxable`
  — per-line + order tax is computed from resolved `OrdersSettings.TaxRatePercent`. So a slab line's
  tax/express is handled at order level; the slab resolver only supplies the base price (ExpressPrice
  left null so the settings express-surcharge % applies unchanged downstream).
- `ResolvedPrice.PriceListItemId` is nullable — slab lines have no price_list_items row;
  `order_items.declared_value` + `applied_slab_price` snapshot the slab decision immutably.
- Structured 422 codes (`StructuredBusinessRuleException`): `declared_value_required`,
  `no_value_slab_match`, `value_slab_overlap`.
- Permission `pricing.slab.manage` (reads gated by `pricing.read`). The DB patch grants it
  DYNAMICALLY to every role already holding `pricing.pricelist.update` (join, no hardcoded role
  codes); IdentitySeeder mirrors it (platform_admin via wildcard, brand_admin explicit).
- DDL patch: `db/patches/value_slab_pricing.sql` (repo root, NOT backend/); also extends
  `pricing_change_log.target_kind` CHECK with 'value_price_slab' and `RevertPricingChange` handles it.

Built on branch feat/settings-foundation alongside [[project-business-settings]] (#26 min-order-value
seam — `MinOrderValueRule` in the same CreateOrderHandler must not regress).
