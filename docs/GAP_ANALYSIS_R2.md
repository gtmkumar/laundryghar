# LaundryGhar — Gap Analysis Round 2 (2026-06-11)

Consolidated, de-duplicated register from a 6-agent orchestrated review: docs-vs-implementation traceability, admin/POS web (×2), customer/rider mobile (×2), backend security/RBAC (×2), and an industry best-practice comparison. ~115 raw findings merged to the actionable set below.

Owners: **be** = dotnet-backend-developer · **aw** = admin-web (senior-react-architect) · **pw** = pos-web (senior-react-architect) · **cm** = customer-mobile (expo) · **rm** = rider-mobile (expo) · **ux** = uiux-design-architect · **db** = database-architect · **sec** = security-code-reviewer · **docs** = documentation.

Security posture note: the prior 42-task remediation closed nearly all High/Critical security findings (RS256 end-to-end, env-gated dev gateway, AES-GCM secrets, master-OTP prod block, mass-assignment fixes, PKCE single-use codes, webhook HMAC). Remaining security items are Medium/Low.

---

## P0 — Correctness / compliance bugs (fix first)

| id | title | area | owner | pri |
|---|---|---|---|---|
| DOC-1 | Rider delivery completion never advances order → `delivered`, writes no `order_status_history`, records no COD `payments` row, emits no `delivery.completed`. Orders silently never complete; COD revenue invisible. | backend (Logistics) | be | High |
| DOC-2 | Recurring subscription billing **always simulates success** (`worker_sim_*`); if enabled in prod, invoices marked paid with no money moving, dunning never fires. Make fail-closed + real `ISubscriptionCharger`. | backend (Worker) | be | High |
| DOC-3 | No in-app **Delete Account** flow in customer-mobile (Play Store rejection risk); FAQ tells users to use a flow that doesn't exist. Backend endpoints already exist. | customer-mobile | cm | High |
| MOB-1 | Pickup time-slot grid is **hardcoded**, never calls `useDeliverySlots`; slot IDs sent as `null` so no capacity reservation. Needs store resolution from address. | customer-mobile + backend | cm/be | High |
| MOB-2 | UPI/card payment is a **dead-end** — both map to `upi-deferred` (COD); no Razorpay sheet, no wallet top-up path. Customer thinks they paid online, gets COD. | customer-mobile + backend | cm/be | High |
| POS-1 | In-progress POS order **destroyed on any reload** — cart/customer/coupon in component state, no persistence. Biggest POS reliability risk. | pos-web | pw | High |
| POS-2 | Order-create & payment-record have **no idempotency key** — double-tap / axios-retry on store wifi creates duplicate orders/charges. | pos-web + backend | pw/be | High |
| WEB-1 | Ops dashboards (riders/needs-action/KPIs/trail) **swallow fetch errors and render "all clear"/0** — operators believe nothing needs action during an outage. | admin-web | aw | High |

## P1 — High-impact UX / workflow gaps

| id | title | area | owner | pri |
|---|---|---|---|---|
| WEB-2 | Destructive/financial actions (cash settlement, plan delete, expense mark-paid, price-list publish, KYC approve, go-live) fire with **no confirmation dialog**. Build one reusable confirm primitive + gate all. | admin-web + ux | ux/aw | High |
| WEB-3 | Settings panels (payment/WhatsApp/SMS/maps keys) + Access-Control RBAC editor + POS actions have **no client-side permission gating** — users see/submit privileged controls, get opaque 403s. | admin-web + pos-web | aw/pw | High |
| WEB-4 | Cash book can be **opened but never closed/reconciled** from the UI (no close, variance, finalize, shift-handover). ADR-009's whole point (attributable shrinkage) is inoperable. | admin-web | aw | High |
| WEB-5 | List pages hard-cap at **pageSize 100/200**, derive filters & counts from the loaded slice — rows silently vanish, KPIs undercount at scale. Adopt pagination/infinite-scroll + count/distinct endpoints. | admin-web + backend | aw/be | High |
| WEB-6 | Plan/subscription status changes **re-POST a stale full DTO** (lost-update race) — concurrent edits silently revert. Use PATCH status-only / version guard. | backend + admin-web | be/aw | High |
| POS-3 | Receipt always prints **"Change ₹0"** — tendered cash capped before reaching the receipt; cash drawer can't reconcile real tender. | pos-web | pw | High |
| POS-4 | **Partial/credit payments not modeled** — any amount >0 shown as fully "Paid"; balances invisible & unreconciled. | pos-web + backend | be/pw | High |
| POS-5 | POS catalog query key omits `brandId` — brand switch serves **stale catalog → wrong prices on real orders**. | pos-web | pw | High |
| POS-6 | POS offline payment does **no cache invalidation** — order shows unpaid after charging → double-charge risk. | pos-web | pw | High |
| MOB-3 | Customer **profile email field hardcoded `''`** — blanks on edit, can overwrite stored email on save. | customer-mobile | cm | High |
| MOB-4 | **Wallet selectable at ₹0 / insufficient balance** — Pay fails server-side with opaque error. Guard + disable. | customer-mobile | cm | High |
| MOB-5 | Rider **proof photo is gallery-only**, never camera; missing iOS/Android camera permission strings & config plugin → store rejection + broken doorstep proof. | rider-mobile | rm | High |
| MOB-6 | **No real-time order-status updates** for customer — tracking screen is a one-shot fetch; push handler doesn't invalidate queries; notification bell goes to /offers. | customer-mobile | cm | High |
| MOB-7 | Customer has **no way to see/share the delivery OTP** the rider asks for; relies on SMS/WhatsApp only. Surface OTP on tracking when `out_for_delivery`. | customer-mobile + backend | cm/be | High |
| MOB-8 | **Booking store never reset** between bookings — second booking inherits prior address/slot/cart. | customer-mobile | cm | High |
| DOC-8 | (= WEB-4) Cash-book close + shift-handover UI missing. | admin-web | aw | High |

## P2 — Medium: validation, a11y, polish, dead UI

| id | title | area | owner | pri |
|---|---|---|---|---|
| DOC-5 | Loyalty points are **never earned or burned** (admin CRUD + balance view only) — balance permanently 0. Wire earn on delivery, burn at order create. | backend | be | Medium |
| DOC-6 | Promotions are **CRUD-only** — first-order discount never applies; only coupons affect totals. | backend | be | Medium |
| DOC-7 | **No report export** anywhere (BRD demands Excel/PDF for analytics, cash book, GST). Start with client-side CSV on FilterableTable. | admin-web | aw | Medium |
| DOC-9 | Garment **inspection photos at pickup** missing (rider + POS) — dispute-evidence trail starts at warehouse, not the door (BRD Dhobi-Cart parity). | rider-mobile + pos-web | rm/pw | Medium |
| DOC-4 | **DPDP consent log never captured** — `dpdp_consents` stays empty; record grant w/ text snapshot at signup + pref changes. | customer-mobile + backend | cm/be | Medium |
| DOC-10 | **No email channel** — `RoutingChannelSender` routes email to logging fallback; build `EmailChannelSender` or remove email toggles. | backend | be | Medium |
| WEB-7 | Dead UI: RolesTab "New custom role"/"Clone", Access-Control "Audit log" button — implement or remove. | admin-web | aw | Medium |
| WEB-8 | Integration secrets have **no test-connection/verify**; Razorpay webhook URL built by `:5173→:5002` string-replace (wrong in prod). | admin-web + backend | aw/be | Medium |
| WEB-9 | Hand-rolled modals lack **focus trap / scroll-lock / Escape / backdrop dismiss** (WCAG fail on tablets). | admin-web + pos-web + ux | ux | Medium |
| WEB-10 | Catalog services/items **can't be deleted** from UI; edit is mouse-only row-click (keyboard users locked out). | admin-web | aw | Medium |
| WEB-11 | Customer subscriptions show **truncated UUID, no name, no actions** (cancel/pause/refund) even for past-due/dunning. | admin-web | aw | Medium |
| WEB-12 | Royalty drawer **goes stale after issue/payment**; no void, no PDF/send, no overpayment clamp. | admin-web | aw | Medium |
| WEB-13 | Validation gaps: rider DL/insurance expiry (no future check), Aadhaar, package credit ≥ price, coupon bounds, royalty/marketing %, rollout %, JSON/color fields; `Number()||0` silently zeros bad input. | admin-web | aw | Medium |
| WEB-14 | Warehouse board has **no auto-refresh**; hardcoded hot-stage; ScanIn `notFound` precedence bug mislabels 500s, records no operator. | admin-web | aw | Medium |
| WEB-15 | Dev creds printed on POS/admin login; **no client-side JWT `exp` check** (stale token renders logged-in). Strip behind `import.meta.env.DEV`. | pos-web + admin-web | pw/aw | Medium |
| WEB-16 | `window.prompt` for KYC reject reason; `console.log('[order-chime]')` in prod; swallowed mutation errors with no toast. | admin-web | aw | Medium |
| POS-7 | Catalog/customer-lookup failures **silent** (no error/retry); empty states missing on fresh brand; customer search un-debounced + E.164-only rejects bare 10-digit; PaymentModal state never resets. | pos-web | pw | Medium |
| MOB-9 | **No haptics** anywhere (booking confirm, OTP success/error, duty toggle, task complete). | customer-mobile + rider-mobile | cm/rm | Medium |
| MOB-10 | **No skeleton loaders** — full-screen spinner blanks tab content on slow 4G. | customer-mobile + rider-mobile + ux | ux/cm/rm | Medium |
| MOB-11 | Rider **task-detail map is a static placeholder**; needs real `react-native-maps` (dev build) with live position + route. | rider-mobile | rm | Medium |
| MOB-12 | Home **address chip is a no-op** (no onPress); items-screen filter chip & service tiles ignore selection/context. | customer-mobile | cm | Medium |
| MOB-13 | **Coupon can't be applied at checkout** (offers list codes but no input/validate on pay screen); wallet 10% discount is client-side only. | customer-mobile + backend | cm/be | Medium |
| MOB-14 | **Order reschedule missing** — no self-serve path after missed/`no_response` pickup. | customer-mobile + backend | cm/be | Medium |
| MOB-15 | Rider **earnings have no per-day drill-down** to the tasks behind a total (gig-worker payout dispute risk). | rider-mobile + backend | rm/be | Medium |
| MOB-16 | **No offline indicator**; customer has no offline awareness; keyboard covers address/profile form fields on Android. | customer-mobile + rider-mobile | cm/rm | Medium |
| MOB-17 | Tracking banner strings & some buttons **bypass i18n** (English in Hindi locale). | customer-mobile | cm | Medium |
| MOB-18 | **eas.json absent** in both apps; OTA `EAS_PROJECT_ID` are slug placeholders not UUIDs; OTA channel undefined → updates 404 / no staged rollout. | customer-mobile + rider-mobile | cm/rm | Medium |
| MOB-19 | Rider **notification bell opens a fake "All caught up" Alert**; customer bell wrong destination. | rider-mobile + customer-mobile | rm/cm | Medium |
| SEC-1 | Notification-retry POST gated by **read** permission (`cms.notification.read`) — read-only user can force re-sends/spam. Add `cms.notification.manage`. | backend (Engagement) | be | Medium |
| SEC-2 | OAuth `mcp:booking` token is a **full customer token** (scope never issued/enforced); refresh grant launders any customer token into an "OAuth" token. Enforce scope / distinct `token_use`. | identity | be | Medium |
| SEC-3 | OTP routing loads WhatsApp/SMS settings with **`brandId:null`** — multi-brand credential bleed (latent until 2nd brand). Thread brandId through `IOtpSender`. | identity | be | Medium |

## P3 — Low / hardening / roadmap (documented, not dispatched this round)

- **SEC-4** analytics refresh global trigger + raw error leak; **SEC-5** assign-permission no ceiling (latent); **SEC-6** settings authz by UserType not permission; **SEC-7** OAuth consent shows client_id not name; **SEC-8** open DCR moderation; **SEC-9** proof-photo content-type trust (sniff magic bytes); **SEC-10** no security headers middleware; **SEC-11** settings decrypt fail-open silent.
- **Mobile polish:** confirm-screen Lottie/animation, rider tab bar, address GPS auto-fill, delivered-screen earnings link, demoTasks relative timestamps, payment-radio a11y roles, recurring pickup scheduling.
- **Docs hygiene:** PRODUCTION_SPEC §7 stack stale (lists Hangfire/MassTransit/RabbitMQ/YARP/Redis/Serilog — none exist); table count 92 vs live 109; `docs/README.md` claims canonical with wrong layout + banned RLS pattern. Re-home GPS-retention claim. (owner: docs)
- **Industry roadmap (greenfield, product decisions):** referral engine (schema exists, unwired), loyalty tiers UI, ratings/reviews reputation dashboard, complaint/dispute ticketing + claim window, lost/damaged compensation workflow, route optimization, pincode serviceability + capacity gating, franchise settlement automation, GST e-invoicing (IRP/IRN), churn/cohort/RFM analytics, win-back campaigns, in-app support chat, TAT capacity engine, partial/split tender, tips, ONDC, eco/care preferences, deeper regional localization.

---

### Dispatch plan
- **Round A (this cycle):** P0 + P1 + the tractable P2 bugs, dispatched per-app to 5 parallel specialist agents (backend, admin-web, pos-web, customer-mobile, rider-mobile) — each stays in its own directory so there are no file conflicts. UX confirm-dialog + skeleton specs folded into the owning app agents.
- **Round B:** reviewer-orchestrator agent audits every diff; failures bounce to the owning agent.
- **Round C:** QA live-test browser → Android → iOS; failures become fix tasks; retest.
- **Backlog:** P3 + industry roadmap tracked here for product prioritization.
