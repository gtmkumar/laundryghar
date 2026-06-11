---
name: gap-analysis-r2-decisions
description: Non-obvious decisions and deferred items from GAP_ANALYSIS_R2 implementation (2026-06-11)
metadata:
  type: project
---

Implemented 9 gaps from GAP_ANALYSIS_R2.md (2026-06-11). Key non-obvious decisions:

**DOC-1 (Delivery completion):** UpdateMyTaskStatusHandler now wraps delivery-completion writes in NpgsqlRetryingExecutionStrategy + explicit transaction. Order status string literals used directly ("out_for_delivery", "delivered") — no project reference to laundryghar.Orders from Logistics. COD Payment row uses `Direction = 1`, `Gateway = "cod"`. RiderLoadHelper.DecrementAsync left OUTSIDE the transaction (issues its own SaveChanges). DbSet name is `_db.OrderStatusHistories` (not `OrderStatusHistory`).

**Why:** Orders silently never reached delivered status; COD revenue was invisible; no delivery.completed event fired.

**DOC-2 (Subscription fail-closed):** ISubscriptionCharger abstraction extracted. DevSubscriptionCharger registered only in Development. When no charger registered in Production, SimulateOrChargeAsync returns Status="failed" / FailureCode="no_charger_configured" and logs LogError. This triggers dunning instead of fake-success.

**Why:** Prior code always returned worker_sim_* as GatewayPaymentId, marking invoices paid with no money moving.

**DOC-5 (Loyalty):** LoyaltyPointsLedger DbSet name is singular: `_db.LoyaltyPointsLedger`. Worker LoyaltyEarnService polls delivery.completed outbox events; uses `published_at IS NULL` + `status='pending'` filter; marks event published after processing. Idempotency: checks for existing earn entry by OrderId before writing.

**DOC-6 (Promotions):** RewardConfig jsonb parsed with JsonDocument; expects `discount_type` ("percent"|"flat"), `discount_value`, optional `max_discount`. First matching promotion wins. Budget tracking: SpentBudget and RedemptionsCount updated atomically in the order transaction. No Order.PromotionDiscount column — folded into DiscountTotal. OrderDto has new PromotionDiscount derived field.

**SEC-1:** cms.notification.manage permission added. Retry POST endpoint gated on manage; GET list stays on read. brand_admin gets manage; store_admin does not (had no notification perms at all).

**SEC-2 (OAuth scope):** token_use=customer_mcp and scope=mcp:booking emitted on OAuth token grant. CustomerOnlyRequirement checks scope only when token_use==customer_mcp (backward-compatible). RefreshToken.ClientId column does NOT exist — client_id binding deferred pending a DB migration. Documented in OAuthEndpoints inline comment.

**SEC-3 (OTP brand routing):** IOtpSender.SendAsync gained `Guid? brandId = null` trailing optional param. Staff OTP (OtpSendHandler) passes null — no brand context in pre-auth. Customer OTP passes cmd.ResolvedBrandId. RoutingOtpSender uses brandId when loading WhatsApp/SMS settings (SettingsStore already supported brand-scoped lookup).

**WEB-6 (PATCH status):** Subscription plan statuses: "draft"|"active"|"paused"|"retired". PlatformPlan statuses: "draft"|"active"|"retired". PATCH /{id}/status routes added to both services. Finance endpoint uses `RequireAuthorization()` (no permission string) since platform admin is enforced in the handler.

**Package credit:** CustomerPackage.CreditValueRemaining is GENERATED ALWAYS — never written directly. Only CreditValueUsed is incremented; EF computes remaining. PackageUsageLedger DbSet is `_db.PackageUsageLedger` (singular).

**How to apply:** When adding future order pricing steps, follow the variable ordering: couponDiscount → loyaltyDiscount → packageDiscount → promotionDiscount → taxableAmount. Each discount layer is additive. DiscountTotal = sum of all.
