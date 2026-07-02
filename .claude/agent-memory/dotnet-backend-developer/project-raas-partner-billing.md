---
name: project-raas-partner-billing
description: RaaS partner billing (FULL-10) — why the Razorpay paylink client/webhook is duplicated per-BC, platform-gateway credential resolution, and the reconcile design
metadata:
  type: project
---

## RaaS partner invoices + wallet paylink top-up (FULL-10, issue #14, commerce host)

**The Razorpay Payment-Links plumbing exists TWICE, on purpose — cross-BC infra reuse is disallowed.**
The original paylink client (`IRazorpayLinkClient`/`RazorpayLinkClient`) and webhook
(`/api/v1/webhooks/razorpay-paylink` → `ProcessPaylinkWebhookCommand`) live in the **CORE** host over
`ICoreDbContext`, and reconcile **brand-platform (SaaS) invoices** (`identity_access.brand_platform_invoice`).
Partner invoices + wallet live in **COMMERCE** over `ICommerceDbContext`. Commerce cannot reference
`core.Infrastructure`, and core's webhook handler cannot touch commerce partner tables or the commerce
wallet ledger primitive. So FULL-10 re-homed the SAME thin client + webhook SHAPE in-BC:
`IPartnerPaymentLinkClient`/`PartnerRazorpayLinkClient` (commerce.Infrastructure/Gateway) + a dedicated
`/api/v1/webhooks/razorpay-partner-paylink` receiver → `ProcessPartnerPaylinkWebhookCommand`.
**Why:** "reuse, don't reinvent" means reuse the Razorpay API + resolution pattern, not a cross-BC
project reference. **How to apply:** if a 4th lane needs paylinks, clone the same shape in its BC; do
not try to share the class across hosts.

**Credential + webhook-secret resolution = the PLATFORM gateway account (settings-first → env).**
Both core and the new commerce partner client resolve creds from the platform-scoped
`kernel.system_settings` row (category `payment`, key `platform_gateway`, `brand_id IS NULL`) — the
operator's dedicated collection account — decrypt with `IFieldCipher`, else fall back to env
`Razorpay:KeyId`/`KeySecret` (webhook: `Razorpay:WebhookSecret`). This is DISTINCT from each brand's
own `payment/gateway` row used for customer payments.
**Key fact that makes this work from a partner session:** `kernel.system_settings` has **no RLS
enabled** (the policy exists only inert in `rls_proposal.sql`), so the platform row (brand_id NULL) is
readable from an authenticated partner token (no brand claim) WITHOUT a bypass — same physical row
core's webhook reads under bypass.

**Reconcile is dual-path, both idempotent on the same key:**
- Push: the commerce partner paylink webhook (bypass_rls via `Items["bypass_rls"]` + the pre-auth
  route allow-list in commerce Program.cs) → invoice marked paid by link id, OR wallet credited from
  the link's `notes` (`kind=partner_wallet_topup`, `partner_id`, `idempotency_key`) with the credited
  amount taken from Razorpay's `amount_paid`.
- Pull: `SyncPartnerInvoicePaymentCommand` / `SyncPartnerWalletTopUpCommand` (API key, no webhook
  secret, in the caller's RLS scope).
Wallet credit always goes through the existing `PartnerWalletLedger.AppendAsync`, so at most one credit
lands no matter how many confirmations arrive. The existing direct-credit `TopUpPartnerWalletCommand` is
untouched (kept for dev/manual).

**Money-path invariants hardened after PR #16 adversarial review (partner wallet) — must hold going forward:**
- **Pull sync MUST mirror the webhook and trust only SERVER-set link `notes`, never client input.** The
  top-up sync endpoint takes ONLY the route `linkId` (no request body). It fetches the link, verifies
  `notes.kind==partner_wallet_topup` AND `notes.partner_id==caller` (rejecting invoice/brand/other-partner
  paid links), and credits keyed on `notes.idempotency_key` (fixed per link → repeat sync dedups). Reason:
  the pre-fix version credited `amount_paid` keyed on a CLIENT idempotency key with no binding → a caller
  could re-credit a paid link with fresh keys, or claim ANY paid link on the shared platform account.
  `PartnerPaymentLinkDetails` now carries `Notes` (parsed on fetch) so pull has the same binding as push.
- **Wallet ledger idempotency is PER-PARTNER: `UNIQUE(partner_id, idempotency_key)`**, not global. Keys are
  caller-supplied free-form; a global unique 500s the second partner (dedup lookup is partner-scoped and
  finds nothing on the recovery re-read). Migrated by `raas_partner_wallet_idem_scope.sql`.
- **`AppendAsync` serializes concurrent same-wallet credit/debit with a pessimistic `SELECT … FOR UPDATE`
  row lock + reload** (`ICommerceDbContext.LockPartnerWalletRowAsync`). `version` is a plain counter, NOT
  an EF concurrency token, so a bare read-modify-write lost updates (top-up HTTP vs debit worker, or two
  worker replicas). **LATENT: the CUSTOMER wallet (`AdminWalletHandlers`/`CustomerWalletHandlers` over
  `WalletAccount`) still has this SAME unfixed lost-update race** — same read-modify-write, `version` not a
  token. Fix it the same way if you touch that money path.

**Deferred:** the invoice PDF endpoint only surfaces a pre-stored `InvoicePdfUrl` (404 stub if none) —
no PDF renderer this wave. No writer yet creates `partner_invoices` rows (issuance/generation is a
later wave); FULL-10 is read + pay + reconcile only.

Related: [[project-integration-settings]] (IFieldCipher singleton, platform_gateway shape),
[[project-subscriptions-adr010]] (generated amount_due column mapping), [[project-shared-data-model]].
