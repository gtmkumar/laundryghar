---
name: project-business-settings
description: Scope-aware business-rule settings foundation (#26) — why it lives on the ops host, precedence, clamp, jsonb scalar codec
metadata:
  type: project
---

Scope-aware business-rule settings (tax/TAT/currency/order/catalog/logistics rules) built for GitHub #26.

**Key facts:**
- Surface: `/api/v1/admin/business-settings` on the **operations** host (`BusinessSettingsAdmin`), NOT core's `/api/v1/admin/settings`. Both hosts can share the `/api/v1/admin/...` namespace because admin-web's vite proxy routes by client prefix (`/core` vs `/ops`), not by API path — the client instance chosen decides the host.
- Two settings surfaces now coexist and are intentionally separate: core's is a fixed bundle (email/maps/gateway via `SettingsStore`, brand-only scope, see [[project-integration-settings]]); the ops one is generic scoped key-value CRUD with store→franchise→brand→platform precedence, whose consumers are operations-domain.
- Reuses existing `settings.read` / `settings.manage` permissions (seeded by IdentitySeeder) — no new permission seeds.

**Why / how to apply:**
- `system_settings.setting_value` is **jsonb**, so every scalar is stored as a JSON literal (number `18`, string `"INR"`, bool `true`). `SettingValueCodec` (operations.Application/Common/Settings) is the single encode/decode seam — always go through it, never write a bare scalar.
- The unique index `(scope_type, brand_id, franchise_id, store_id, category, setting_key)` treats NULL brand_id as DISTINCT, so **ON CONFLICT is unreliable for platform rows** — seed/guard with NOT EXISTS (the DB patch does this).
- Store-scope rows are keyed by store_id alone (franchise_id kept NULL) so upsert-find stays deterministic; the resolver keys franchise/store rows on their own id (globally unique) and does not require brand_id match.
- Only the five previously-hardcoded order values (tax 18, express 50, tat 48/24, INR) get a **platform** seed; `min_order_value` and all other new keys deliberately have NO default anywhere (resolve to null until an operator sets them). Correctness on a fresh DB without the patch is preserved because `OrderSettingsResolver` falls back to the injected `OrdersSettings`.
- GST rule: an order's franchise with `Gstin == null` (unregistered) forces effective tax rate to 0 regardless of any configured value — enforced in `OrderSettingsResolver.ResolveAsync`.
