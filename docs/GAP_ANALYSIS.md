# LaundryGhar — Gap Analysis & Remediation Backlog

_Date: 2026-06-10 · Produced by a six-track review: backend implementation audit, admin/POS web audit, mobile apps audit, security/RBAC audit, docs conformance audit, and industry best-practice research (CleanCloud, QDC, Turns, SMRT, Tumbledry, UClean, DhobiLite + Swiggy/Urban Company ops patterns)._

**How to use this file:** every gap below has a corresponding task in the session task list (`Task #N`), tagged with owner agent and priority. Work the High items first; the Top-5 security items gate production.

---

## Status (2026-06-10 EOD)

The remediation initiative ran the same day this register was produced: **~25 of 32 tasks
completed** via orchestrated specialist-agent rounds (payments hardening, refunds, PII
encryption/masking, IDOR sweep, OTP throttling, notification senders + push, booking
persistence, auto-dispatch, TAT alerting, royalty automation, GST invoices, DPDP erasure,
warehouse/cash-book/expense admin features, and this docs cleanup #29). Still in flight or
pending at EOD: **#17 subscriptions** (schema deployed 2026-06-10 via
`db/patches/subscriptions_module.sql` — service entities/endpoints/dunning follow-up),
**#23 i18n**, **#26 QA regression baseline**, **#28 accessibility**. The **session task
list is the live source of truth** for per-task status; the register below is the
point-in-time morning snapshot and is *not* updated per-row.

---

## Executive summary

The platform is architecturally strong (RS256/JWKS, enforced RLS under `app_user`, granular RBAC, transactional outbox, clean payment-gateway seam) and the operational core (order state machine, garment QC, rider ops, COD settlement, cash book) is real. What separates it from production is concentrated in five clusters:

1. **Money is not real yet** — the payment gateway is a dev stub registered in ALL environments (signature verification always passes), there is no webhook handler, no refund flow, and a cumulative-refund cap is missing.
2. **Nothing is ever sent** — the notification pipeline ends at a logging stub; no WhatsApp/SMS/push, and neither mobile app can even receive push (expo-notifications not installed).
3. **The customer booking flow is a demo** — local state only; the backend pickup-request endpoint exists but doesn't carry cart items and the app never calls it.
4. **Operational scale features are dormant** — no auto-dispatch (CurrentLoad/capacity unused), no promised-date/TAT alerting, manual-only royalty, no photo/PoD infrastructure, warehouse board is read-only.
5. **Compliance debt** — no GST invoice document, no DPDP erasure pipeline, PAN/bank stored plaintext and returned in `UserDto`, zero i18n despite the bilingual spec requirement.

The subscriptions module (ADR-010, 10 tables) is spec-only: never deployed, zero code.

---

## Top 5 must-fix before production (security review)

| # | Finding | Severity | Task |
|---|---------|----------|------|
| 1 | `DevPaymentGateway` registered unconditionally (`Commerce/Program.cs:49`) — signature verification always true in prod | **CRITICAL** | #1 |
| 2 | Refund handler has per-call cap only — N partial refunds can exceed the captured amount (`AdminPaymentHandlers.cs:84`) | HIGH | #2 |
| 3 | PAN/bank/IFSC/UPI stored plaintext and returned unmasked in `UserDto` behind broad `users.read` | HIGH | #3 |
| 4 | `CreateOrderCommand` doesn't brand-check `CustomerId` (cross-brand IDOR); sibling handlers unswept | MEDIUM | #4 |
| 5 | OTP throttling is per-code (resend resets the budget); rate limiter keys on socket IP, not X-Forwarded-For | MEDIUM | #5 |

Also latent: `VerifyPaymentHandler` never binds the client-supplied `gatewayOrderId`/amount to the stored payment — exploitable the day a real gateway lands (folded into Task #1).

**Verified-good patterns (don't re-litigate):** refresh-token rotation with family-revoke-on-reuse, Argon2, account lockout, algorithm pinning, customer/rider self-filtering from JWT `sub`, consistent platform-admin-only `X-Brand-Id` gating, env-gated CORS/OTP/seeder, expo-secure-store on mobile, no `dangerouslySetInnerHTML`.

---

## Gap register

Columns: **Issue · Impact · Solution · Owner · Priority · Task#**. Full detail (file:line evidence, step-by-step solutions) lives in each task's description.

### A. Payments & revenue

| Issue | Impact | Solution | Owner | Pri | Task |
|---|---|---|---|---|---|
| Razorpay is a dev stub in all envs; no webhook endpoint; verify doesn't bind order/amount; no idempotency keys | Payment verification bypassable; async confirmations lost | Env-gate stub, real HMAC gateway (fail-closed), webhook route, binding checks, idempotency keys | dotnet-backend-developer | High | #1 |
| No refund endpoint; cancellation never refunds; no cumulative refund cap | Money leak + customers lose money on paid cancellations | Cumulative cap, auto-refund on paid-order cancel, customer visibility | dotnet-backend-developer | High | #2 |
| Subscriptions (ADR-010, 10 tables, 2 MVs) spec-only — never deployed, zero entities/endpoints/dunning | No recurring revenue product; franchise SaaS billing still flat-fee | Deploy schema → entities → Commerce/Finance endpoints → UPI-AutoPay mandates → Worker dunning ladder | database-architect + dotnet-backend-developer | Medium | #17 |
| Royalty calc manual-only; no invoice lifecycle; no admin royalty UI | Franchises not billed on time; status untrackable | Monthly Worker job, invoice state machine + mark-paid, Finance→Royalty page | dotnet-backend-developer + senior-react-architect | Medium | #18 |

### B. Notifications & engagement

| Issue | Impact | Solution | Owner | Pri | Task |
|---|---|---|---|---|---|
| Only `LoggingChannelSender` registered — nothing ever sent; no event→template mapping | Customers receive zero lifecycle communication (WhatsApp-first is table-stakes in India) | WhatsApp BSP (utility templates) + SMS + push senders; lifecycle template mapping; honor opt-ins | dotnet-backend-developer | High | #6 |
| No push in either app (expo-notifications absent); rider polls 30s; "Notifications" button is a stub | Riders get tasks late; customers blind; no device tokens server-side | expo-notifications + token-registration endpoints + deep links | expo-mobile-developer (+backend) | High | #7 |

### C. Booking & order operations

| Issue | Impact | Solution | Owner | Pri | Task |
|---|---|---|---|---|---|
| Customer booking is local-state only (`FEATURES.bookingApi=false`); pickup-request endpoint exists but takes no items | Flagship app flow is a demo | Extend pickup request to carry cart items; wire app; flip flag | dotnet-backend-developer + expo-mobile-developer | High | #8 |
| admin-web Orders page has no detail view or actions at all | Admins cannot manage the order lifecycle | FormDrawer-based OrderDetailDrawer: status actions, history, rider assign, notes | senior-react-architect | High | #11 |
| POS lacks customer lookup/create, payment capture, receipt printing, garment tagging/labels, weighing, coupons | A real store counter cannot run on it | Counter completeness pass | senior-react-architect (+backend) | High | #12 |
| No promised-date/TAT engine, no overdue/aging queues | The industry's most-used ops screen is missing; stuck orders invisible | promised_date from service TAT; due-today/overdue/stuck queues + breach alerts | dotnet-backend-developer + senior-react-architect | Medium | #19 |
| No admin pickup-request queue; no slot/capacity management UI | Incoming app bookings invisible; slots unconfigurable | Pickup-requests tab + per-store slot CRUD | senior-react-architect | Medium (High once #8 ships) | #21 |
| No customer dispute endpoint; rewash admin-triggerable only via state machine | Complaint/redo guarantee (industry norm: ~7-day claim window) unsupported | Fold into #15 (complaint ticket) + #20 (rewash loop) | — | Medium | #15/#20 |

### D. Logistics & warehouse

| Issue | Impact | Solution | Owner | Pri | Task |
|---|---|---|---|---|---|
| No auto-dispatch; `CurrentLoad`/`DailyDeliveryCapacity` never used; duty toggle client-side only | Manual dispatch doesn't scale; availability is fiction | Duty endpoint, load tracking, Worker auto-dispatch with manual override | dotnet-backend-developer | High | #9 |
| No file storage / photo upload anywhere despite S3 columns (QC photos, PoD, signature) | No proof-of-delivery or intake-condition photos — the #1 dispute-killer | `IFileStorageProvider` + upload endpoints + rider camera capture | dotnet-backend-developer + expo-mobile-developer | High | #10 |
| Warehouse board buttons dead (Scan in / Recon report / +Add); recon manual-only; missing≠lost flow | Read-only board; no daily garment accounting; no compensation SOP | Wire board actions; daily recon Worker job; lost→notify+compensate | dotnet-backend-developer + senior-react-architect | Medium | #20 |
| Rider app missing earnings/payout history, cash summary, maps directions, failure reasons, offline queue | Backend payout/COD data invisible to riders; updates lost offline | Earnings + Cash screens, Maps deep-link, failure modal, offline queue; COD floating-cash limit follow-up | expo-mobile-developer (+backend) | Medium | #16 |

### E. Customer experience (mobile)

| Issue | Impact | Solution | Owner | Pri | Task |
|---|---|---|---|---|---|
| No address CRUD/serviceability check; booking uses a hardcoded demo address; no ratings; wallet top-up stub; profile view-only; support is an email alert | Retention/trust loops missing; table-stakes vs UClean/Tumbledry | Address book + pincode check, post-delivery rating, wallet top-up (needs #1), profile edit, Help/FAQ + complaint ticket | expo-mobile-developer + dotnet-backend-developer | High | #15 |
| No Sentry, no error boundaries, no expo-updates, force-update config fetched but ignored | Invisible crashes; store-release for every fix | Sentry + OTA + boot version check | expo-mobile-developer | Medium | #24 |

### F. Cross-cutting quality

| Issue | Impact | Solution | Owner | Pri | Task |
|---|---|---|---|---|---|
| GST-compliant invoice document doesn't exist (SAC 999712, CGST/SGST split, GSTIN) | Compliance gap; no customer receipt | Invoice record + PDF + customer download + POS print | dotnet-backend-developer | High | #13 |
| DPDP erasure pipeline absent (`AccountDeletionRequest` has zero handlers); no retention jobs | Legal exposure; DPDP Rules 2025 phase in ~May 2027 | Delete-request endpoint + anonymization Worker job + retention sweeps + grievance contact | dotnet-backend-developer | High | #14 |
| ~32 of 63 backend commands lack FluentValidation; admin forms have no Zod/RHF despite deps installed | 500s instead of 400s; bad data | Validator sweep + RHF/Zod standard for FormDrawer | dotnet-backend-developer + senior-react-architect | Medium | #22 |
| Zero i18n in any client; backend `*Localized` fields ignored | Spec violation (en-IN/hi-IN required) | i18next infra, mobile-first extraction | senior-react-architect + expo-mobile-developer | Medium | #23 |
| Refresh token in localStorage; no idle timeout; no 403 page; inconsistent permission gating | XSS token theft; confusing denials | HttpOnly cookie from Identity, exp checks, 403 state, hasPermission audit | senior-react-architect + dotnet-backend-developer | Medium | #25 |
| No automated tests for mobile (zero files) or money flows | Regressions invisible | Jest/RNTL baseline + xUnit money/security probes + smoke suite | qa-test-engineer | Medium | #26 |
| No HSTS/security headers; login accepts 6-char passwords vs 8+complexity on reset; banners allow http:// | Defense-in-depth gaps | ServiceDefaults header middleware; policy alignment; https-only | dotnet-backend-developer | Low | #27 |
| Sparse ARIA/labels web+mobile; palette contrast unverified | WCAG A failures | Accessibility pass + shared-component conventions | uiux-design-architect | Low | #28 |
| Packages & Coupons routes are ComingSoon stubs in admin-web | Coupon campaigns unmanageable from console | Build pages on FilterableTable/FormDrawer (backend CRUD already exists in Commerce) | senior-react-architect | Medium | (fold into #11 sprint or schedule next) |

### G. Documentation

| Issue | Impact | Solution | Owner | Pri | Task |
|---|---|---|---|---|---|
| Both INDEX files claim "Build not yet started" with conflicting counts (92 vs 102 tables); references point at non-existent paths (`database/`, `ADRs/001-009`, root `BUILD_PLAN.md`…) | Every new agent/human is misrouted on first read | Status lines fixed 2026-06-10 (this review); full path/ADR restoration tracked | laundryghar-orchestrator | Low | #29 |
| `docs/SCHEMA_FULL.sql` (102 tables) diverged from deployed `database_scripts/` (92) — subscription tables never deployed | "Canonical schema" is ambiguous | Documented here: **`database_scripts/` + `db/patches/` is the deployed canonical**; `docs/SCHEMA_FULL.sql` + `docs/0{8,9}_subscriptions_*.sql` are spec-only until #17 deploys them | — | — | #17/#29 |

---

## Industry best-practice shortlist (research-derived, judged against the codebase)

What comparable platforms get judged on, and our status:

1. **WhatsApp utility-template pipeline for the full order lifecycle** — missing (Tasks #6/#7). QDC's moat in India.
2. **Garment barcode tagging + intake photos + scan-assembly with count-mismatch alarms** — tagging modeled, photos/scan-in/assembly missing (Tasks #10/#12/#20).
3. **Promised-date engine → overdue/aging queues** — missing (Task #19).
4. **COD floating-cash limits + daily rider cash reconciliation with variance alerts** — settlement exists; limits/variance alerts missing (Task #16).
5. **Redo-guarantee workflow** (claim window, photo evidence, rewash loop, liability T&Cs) — missing (Tasks #15/#20).
6. **Automated franchise settlement + royalty computation** (Razorpay Route pattern) — manual royalty only (Task #18).
7. **DPDP consent/retention/erasure plumbing** before the ~2027 enforcement wall — consent done, erasure missing (Task #14).

Deliberately NOT adopted (defensible deviations): rider roster stays server-side-filtered (correct at scale), order creation stays admin/POS-side after weighing (per-kg pricing reality — bookings are pickup *requests*, which is also the UClean/Tumbledry model).

---

## Suggested wave sequencing

- **Wave A — production gates (all High/security):** #1, #2, #3, #4 → then #13, #14, #6.
- **Wave B — make the apps real:** #8 + #21, #7, #15, #10, #9, #11, #12.
- **Wave C — operate at scale:** #19, #20, #16, #18, #5, #22, #25, #26.
- **Wave D — growth & polish:** #17, #23, #24, #27, #28, #29.
