# LaundryGhar × Razorpay "Fix My Itch" — Opportunity Analysis

_Date: 2026-10-09 · Scope: customer-facing laundry business + the LaundryGhar SaaS platform_

## 1. Executive summary

Razorpay's **Fix My Itch** (razorpay.com/m/fix-my-itch) is a public index of 10,000+ everyday problems in India. The problems come from scraped X and Reddit posts, a public survey and on-the-ground interviews, and each one has an **Itch Score**. The score combines **Severity, Frequency, Market Whitespace and TAM**. Of the ~113 top-ranked problems we could retrieve, **one is a direct laundry problem**, and **about 20 more describe the same underlying failure in an adjacent service**. That shared failure is low trust in informal, local service providers: hidden costs, no quality standard, no accountability for loss or damage, no tracking, and rigid payment plans.

The problems people complain about are mostly not about convenience. LaundryGhar already has pickup slots, express service, barcodes, a rider fleet and GST invoices. What people want is **guarantees**. LaundryGhar's back office already records most of the evidence those guarantees need: barcode chain of custody, inspection photos, QC pass/fail/rewash, declared-value pricing slabs, rider KYC, and OTP handover. **Almost none of it is shown to the customer or backed by a promise.** The cheapest way to stand out is to turn that existing operational data into customer-facing guarantees.

**Top 5 recommendations (in build order):**

| # | Opportunity | Fix My Itch problems it answers | Why now |
|---|---|---|---|
| 1 | **Online payments in the customer app** (Razorpay payment link → native UPI later) | #73, #1 (foundation for everything below) | Backend done; app shows "coming soon" |
| 2 | **Premium Fabric Care tier** (silk / embroidery / designer) with before-and-after photos and declared-value cover | **#27 — direct laundry itch (69.5)** | ~80% of the plumbing exists |
| 3 | **Flexible laundry subscriptions** ("Dhobi Replacement Plan") with self-serve pause, skip and rollover on UPI Autopay | #73 (76.2), #29 (74.3), #48 (74.5) | Mandates + `PausedAt/PauseResumesAt` already modelled |
| 4 | **Garment Protection Guarantee**: a claims workflow with a 72-hour resolution SLA plus a free 48-hour re-do | #67 (75.2), #72 (68.2), #62 (72.8), #59 (66), #64 (60.8) | `fulfillment.lost` event emitted with no consumer |
| 5 | **Price-Lock and transparent savings** (approve price changes before processing; exact ₹ savings, never "up to") | #57 (68.5), #58 (70.5), #28 (68.3), #75 (81.2) | Pricing is already server-side and deterministic |

These are followed by **WhatsApp-first booking** (reuses the existing MCP tools), **B2B / housing-society billing**, and a **rewards and referral** layer.

---

## 2. Source and method

**Access limits:**
- razorpay.com is blocked by this environment's network policy, so the live page could not be opened.
- The problem list was taken from a public scrape of the site's problem table (`razorpay_itches.json`, 113 problems, roughly the top ~10 per category across 11 categories), cross-checked with Razorpay's launch post and award-entry write-ups.
- Itch Scores below are as scraped. **They are unverified against the live site.** Some rows in the scrape use a 10–100 TAM scale instead of 1–10; we compare problems by the overall Itch Score only.

**Codebase review:**
- The whole repo was reviewed: backend services, admin-web, pos-web, customer-mobile, rider-mobile, `db/`, and `docs/`.
- Key gaps were checked directly in code. Examples:
  - Online payment shows "coming soon" in `customer-mobile/app/(app)/booking/pay.tsx:586`.
  - Lost-garment compensation is marked "OUT OF SCOPE" in `LostGarmentProcessor.cs:18`.
  - Subscription pause fields exist in `CustomerSubscription.cs:38-39` but have no customer flow.

**Prioritisation scoring** (each factor 1–5):
`Priority = 0.3·Demand + 0.3·Business value + 0.2·Competitive edge + 0.2·(6 − Complexity)`

| Factor | How it is scored |
|---|---|
| Demand | Driven by the Itch Score and how often Indian urban households hit the problem |
| Value | Revenue, retention or margin impact |
| Complexity | Estimated with the existing code taken into account |

---

## 3. Where LaundryGhar stands today (short snapshot)

| Area | State | Evidence |
|---|---|---|
| Booking, slots, express, reschedule | ✅ Complete | `customer-mobile/app/(app)/booking/*`, `CustomerOrderEndpoints.cs` |
| Catalog: fabric types, multipliers, **declared-value slabs**, versioned price lists | ✅ Complete | `operations.WebApi/Endpoints/Catalog/*`, `ValueSlabsAdmin.cs` |
| 19-state order engine (incl. `rewash`, `disputed`, `returned`) | ✅ Complete | `SharedDataModel/Enums/OrderStatus.cs` |
| Barcode tags, process-scan logs, QC, **inspection photos**, stock reconciliation | ✅ Complete (back-office only) | `operations.Application/Warehouse/*`, `UploadInspectionPhoto.cs` |
| Riders: auto-dispatch, GPS, geofence, OTP handover, proof photo, KYC | ✅ Complete | `RiderSelfEndpoints.cs`, `AutoDispatchService.cs` |
| GST invoices (PDF) | ✅ B2C complete | `AdminInvoiceEndpoints.cs`, `CustomerInvoiceEndpoints.cs` |
| Razorpay: orders, refunds, **mandates (create/charge)**, payment links | ✅ Backend complete | `RazorpayPaymentGateway.cs`, `IPartnerPaymentLinkClient.cs` |
| Customer online payment / wallet top-up | ❌ "Coming soon" | `booking/pay.tsx`, `(tabs)/wallet.tsx` |
| Customer subscriptions, packages, loyalty redemption | ⚠️ Backend + admin only, no customer UI | `SubscriptionPlansAdmin.cs`, `PackagesCustomer.cs` |
| Loss / damage claims & compensation | ❌ Detection only | `LostGarmentProcessor.cs` |
| Referrals | ❌ Fields only | `Customer.ReferralCode` |
| WhatsApp | ⚠️ Outbound only, no inbound or bot | `Worker/Channels/*` |
| B2B / corporate / society accounts | ❌ Absent | — |
| AI | ⚠️ MCP server with 8 customer tools (book, track, price…) | `core.WebApi/Mcp/Tools/LaundryTools.cs` |
| Customer tracking | ⚠️ Status timeline + OTP, no live map or ETA | `orders/tracking` |

---

## 4. Fix My Itch problems relevant to LaundryGhar

### 4.1 Fit classification

| Fit | Problem (Itch Score) | How it maps to laundry |
|---|---|---|
| **Direct** | **#27 Why is specialized care missing for silk, embroidery and designer clothing? (69.5)** | The core laundry problem |
| Direct analogue | #29 Households lack emergency backup when domestic help quits (74.3) | Washing and ironing is a large part of domestic help's job; laundry plans replace the *dhobi* or *presswala* |
| Direct analogue | #67 Couriers lose or damage 5% of packages; claims take 30–45 days (75.2) | Garment loss and damage is the #1 laundry complaint |
| Direct analogue | #72 Fragile handling not monitored; no chain of custody (68.2) | Delicate garments; the barcode scan log *is* a chain of custody |
| Direct analogue | #62 Repair shops give no warranty on work (72.8) | A rewash / re-do guarantee |
| Direct analogue | #59 Can't verify work quality before payment (66) | Inspect on delivery, then pay or request a re-do |
| Direct analogue | #64 Deep-cleaning services lack quality standards and checklists (60.8) | A QC checklist per fabric, shown to the customer |
| Direct analogue | #57 Painters' quotes escalate 40–60% (68.5), #58 AC installation charges vary (70.5), #28 inflated inspection fees (68.3) | Laundry "estimate vs final bill" surprises at pickup |
| Direct analogue | #68 Regional logistics lack real-time tracking and ETAs (77) | "Where is my rider / when are my clothes back?" |
| Payments | #73 Can't pause auto-debit subscriptions, only cancel (76.2) | Laundry plans while travelling, at a hometown, or in exam months |
| Payments | #75 Confusing cashback terms (81.2), #78 No reward visibility (77.2) | Coupon, loyalty and wallet clarity |
| Payments | #1 No accountability after partial payment (76) | Pay-after-delivery, escrow-style holds |
| B2B | #76 Society maintenance bills not itemised (72.2), #3 Micro-SMEs waste 10+ h/week on invoices (67.5) | Society tie-ups; hotels, salons, PGs, clinics on monthly GST invoices |
| Subscription UX | #48 Tiffin subscribers can't customise day-to-day (74.5) | Skip a week, swap wash-and-fold for dry-clean credits |
| Service access | #19 Elderly can't find at-home services (75) | Assisted booking, doorstep everything |
| Sustainability | #112 No doorstep e-waste pickup with rewards (71.2), #109 can't verify recycling (79) | Old-clothes donation / textile recycling pickup for points |
| Platform verticals | #13 Salon preferred stylist (62), #17 Salon waits (66), #48 Tiffin (74.5), #70 Reverse logistics pricing (70.8) | For the white-label PaaS (salon, tiffin, courier strategies already exist) |

**Out of scope:** the remaining ~90 problems (healthcare, edtech, real estate, travel, fintech investing, mobility) have no credible laundry or platform fit and are not analysed further.

### 4.2 Common thread

Across these problems the complaint is the same: **an informal local provider holds your property or your money, and you have no proof, no standard, and no recourse.** A neighbourhood dry cleaner is exactly this kind of provider. LaundryGhar's operational depth means it can be the counter-example. It has to make the evidence visible and put the promise in writing.

---

## 5. Gap analysis and recommended solutions

Each opportunity below is laid out as: *problem → what we have → gap → solution → implementation notes.*

### O1. Premium Fabric Care ("Couture Care") — answers #27 directly

- **Have:**
  - Fabric types with price multipliers.
  - Declared-value slabs.
  - Pickup inspection by riders.
  - Warehouse inspections with photos.
  - QC with pass/fail/rewash.
  - Per-garment barcode and scan log.
- **Gap:**
  - Fabric only changes *price*, not *process*.
  - No routing of delicate items to trained operators or gentle processes.
  - Inspection photos are never shown to customers.
  - Declared value is used for pricing but not offered as *cover*.
  - No care-label capture.
- **Solution: a "Premium Care" service tier.**
  - **Care profile per fabric:** add `care_tier` (standard / delicate / couture) and a process recipe (solvent, temperature, hand-finish, steam-only) to fabric types. Warehouse batching keeps couture items out of mixed batches.
  - **Specialist routing:** a warehouse role or skill flag, so only certified operators can scan couture items into processing stages.
  - **Before-and-after photo proof:** photos are mandatory at pickup and at QC for items above a value threshold. Show them in the customer order detail (`customer-mobile/app/(app)/orders/[id].tsx`) through a customer-scoped wrapper over `GetInspectionPhotos`.
  - **Declared-value cover:** state the value slab explicitly on the invoice and booking screen ("Covered up to ₹25,000"). This links to O4.
  - **Care-label scan:** the rider captures a photo of the care label at pickup. Later, an AI vision model can suggest the care tier.
  - **Packaging:** saree rolling / pre-pleating and breathable garment bags as paid add-ons (the add-ons model exists).
- **Value:** premium items carry 3–5× the ticket size and high margin. The scraped problem calls out silk sarees and designer suits, the highest-value segment in Indian wardrobes (weddings, festivals).

### O2. Garment Protection Guarantee (claims + chain of custody) — #67, #72, #62

- **Have:**
  - Lost-garment detection at reconciliation close, which emits a `fulfillment.lost` event.
  - Order states `disputed` and `rewash`.
  - Razorpay refunds.
  - Wallet ledger.
  - Support tickets.
- **Gap:**
  - No claim entity, no compensation policy, and no SLA. The code explicitly defers it: *"Wallet compensation is OUT OF SCOPE this round — needs policy"* (`LostGarmentProcessor.cs:18`).
  - Customers can't see where their garment is.
- **Solution:**
  - **`claims` module.** Each claim records:
    - type: damage / loss / delay / shrinkage / colour-run
    - evidence: customer photos plus our pickup/QC photos
    - SLA due date: **72 h**, versus 30–45 days for courier claims (#67)
    - resolution: re-do / repair / wallet credit / refund
    - approval chain: store → franchise → brand, reusing step-up OTP
  - **Policy engine per brand**, for example: compensation = min(k × service price, % of declared value, cap). This works because the value slab is captured at booking. Wallet credit is instant; refunds go through `RazorpayPaymentGateway.InitiateRefundAsync`.
  - **Auto-claim:** a handler consumes `fulfillment.lost` and opens the claim proactively, so the customer is told before they notice.
  - **Garment timeline:** a customer view of the existing scan log ("Received at Sector 14 store 10:42 → Washing 14:05 → QC passed 18:30 → Out for delivery").
- **Differentiator:** "Lost or damaged? Resolved in 72 hours, guaranteed." No neighbourhood dry cleaner can offer this, and few chains publish it.

### O3. Quality Promise: inspect-then-accept + free 48-hour re-do — #59, #64, #62

- **Have:** QC results per garment, `rewash` order status, order ratings, OTP delivery handover.
- **Gap:**
  - The customer has no structured way to accept a delivery or reject one item.
  - Rewash is internal only.
  - The QC checklist is not visible to the customer.
- **Solution:**
  - After delivery, the app (and WhatsApp) shows "All good?" with per-item **Accept / Report issue**.
  - **Report issue** within 48 h creates a zero-price rewash child order with an automatic pickup slot.
  - The QC checklist per fabric (stain check, button check, press quality, fold) is recorded by the operator and shown as a "Quality Card".
  - Optional **pay-after-inspection** for COD and payment-link customers addresses #1. Analytics: rewash rate by store and operator feeds franchise scorecards.

### O4. Price-Lock and transparent savings — #57, #58, #28, #75

- **Have:** server-side pricing (`CreateOrderCommand.cs`) that applies express, coupon, loyalty, package, promotions, GST and charges in a fixed order. Public price lists. MCP `get_price_list`.
- **Gap:**
  - The booking estimate is based on what the customer *declared*.
  - The rider counts and inspects items at the door, and the final bill can differ with no consent step. This is the laundry version of the painter's "40–60% escalation".
  - Coupon copy can still read as "up to".
- **Solution:**
  - **Price-Lock:** if the post-inspection total differs from the estimate by more than X% (brand-configurable), send a push / WhatsApp approval ("3 extra shirts found: +₹120. Approve?") before processing starts. Silence for N hours means auto-approve, or hold, per brand policy.
  - **Exact-savings checkout:** show "You save ₹87" itemised (coupon ₹50 + points ₹37) *before* confirming. Never show "up to".
  - **Published rate card per store** as a shareable web page (also an SEO landing page). This is a seed for the PWA tier in `PLATFORM_STRATEGY.md`.
  - **No inspection fee, ever:** a marketing promise backed by the model.

### O5. Online payments in the customer app — prerequisite, #73, #1

- **Have:** Razorpay order, verify, refund, mandate create/charge, webhooks, payment links (used for platform and partner billing), wallet top-up endpoints.
- **Gap:** the customer app allows only wallet or COD. Online and top-up are "coming soon" because no native SDK is integrated.
- **Solution (two stages):**
  1. **Ship this week, no native build:** reuse the payment-link client (`IPartnerPaymentLinkClient`) for customer orders and wallet top-ups. Open it with `expo-web-browser` and confirm through the existing webhook. This ships as an OTA update.
  2. **Native checkout:** Razorpay React Native SDK via an Expo config plugin (EAS build) for UPI intent, saved cards, and UPI Autopay mandate authorisation (needed for O6).
- **Value:** fewer COD cash-handling and rider reconciliation costs, higher prepaid share, and it unlocks subscriptions and packages.

### O6. Flexible laundry subscriptions ("Dhobi Replacement Plan") — #73, #29, #48

- **Have:**
  - Subscription plans with included pickups and express.
  - Mandates.
  - Dunning.
  - MRR views.
  - A `Recurring` fulfilment strategy with `DeliveryCadence`.
  - `PausedAt` / `PauseResumesAt` on `CustomerSubscription`.
- **Gap:**
  - No customer UI for subscriptions or packages.
  - Pause is an admin status only, with no auto-resume.
  - No auto-created recurring pickups for laundry.
  - No skip or rollover.
- **Solution:**
  - **Plans that replace the dhobi:**
    - "Ironing Weekly" (e.g. 40 pieces / month)
    - "Wash & Iron Family"
    - "Professional" (shirts + 2 dry-clean credits)
  - **Recurring pickups:** standing slot (e.g. every Tue 8–10 am) auto-booked from the plan.
  - **Pause / skip / rollover (the #73 answer):** pause up to 30 days with a resume date, or skip one week. Because LaundryGhar *initiates* each mandate charge (`ChargeMandateAsync`), pausing just suppresses the charge cycle. **No mandate cancellation and no re-KYC.** Unused pieces roll over for one cycle.
  - **Credit swap (the #48 answer):** convert wash credits to dry-clean credits at a published ratio.
  - **"SOS" add-on (the #29 answer):** when the maid or presswala quits, a same-day / next-morning ironing pickup is guaranteed for subscribers (see O8).
- **Value:** predictable revenue, rider route density (fixed slots mean lower cost per pickup), lower churn.

### O7. WhatsApp-first booking and support — enabler for #19, #68, #17

- **Have:** WhatsApp Cloud API (outbound), the MCP server with `book_pickup`, `track_order`, `get_pickup_slots`, `get_price_list`, `cancel_order`, and Hindi locale.
- **Gap:** no inbound webhook, no conversational ordering. `PLATFORM_STRATEGY.md` already sells a `whatsapp_bot` feature that does not exist.
- **Solution:**
  - Add an inbound WhatsApp webhook.
  - Run an LLM agent that uses the **existing MCP tools** as its action layer. Almost no new domain logic is needed.
  - Hindi / Hinglish, voice-note support, payment link inline (O5).
  - Escalate to a human ticket in the existing support module.
- **Value:** most Indian laundry customers already message their dhobi on WhatsApp. This lowers friction for elderly users and non-app users (#19), and turns into a paid platform feature for franchises and white-label tenants.

### O8. Same-day "SOS" express — #29

- **Have:** express flag, express price per line, express turnaround hours and surcharge.
- **Gap:** no same-day slot cut-off, no capacity reservation.
- **Solution:**
  - Per-store same-day cut-off times (e.g. pickup by 11 am → back by 8 pm).
  - Reserved warehouse capacity for express batches.
  - Free SOS credits for subscribers.
  - Needs the missing operating-hours / holidays API (schema exists).

### O9. Rewards that are honest — #75, #78

- **Have:** loyalty earn on delivery, burn in order pricing, wallet balance, coupons, `ReferralCode` fields, unused `birthday_bonus`.
- **Gap:** no redemption UI, no expiry visibility, no referral flow, no admin page for loyalty programmes.
- **Solution:**
  - One "Rewards" screen showing points, value in ₹, what expires and when, and pending credits.
  - Redeem at checkout with an exact rupee figure.
  - Referral: give ₹X / get ₹X, credited on the friend's first *delivered* order.
  - Birthday bonus automation.
  - All terms in one line, with no fine print.

### O10. Digital Garment Passport — #22

- **Solution:**
  - A per-customer wardrobe of tagged garments: photos, care tier, treatments done, claims, invoices. Kept permanently and downloadable.
  - Serves as proof for claims (O2) and for brand warranty claims on designer wear.
  - Drives re-orders ("Your Banarasi was last cleaned 11 months ago: wedding season reminder").
  - Built from existing garment, tag and inspection tables. Mostly a read model plus UI.

### O11. B2B and housing-society billing — #76, #3

- **Have:** GST invoices with customer GSTIN, a partner wallet and partner invoicing for RaaS (a template for credit accounts), and royalty invoice generation.
- **Gap:** no corporate accounts, credit terms, consolidated invoices, PO numbers, or GSTR export.
- **Solution:**
  - **Business accounts** (hotels, salons, PGs/hostels, clinics, gyms, restaurants), each with:
    - credit limit and net-15/30 terms
    - monthly consolidated GST invoice with PO reference
    - per-department / per-branch item-level statement
    - GSTR-1 export
    - linen par-level tracking
  - **Society partnerships:**
    - Building-day pickups (one rider, 40 flats, about ⅓ of the delivery cost).
    - An itemised monthly statement per flat.
    - A society-admin dashboard showing exactly what was collected and earned. This is the transparency #76 asks for.
- **Value:** large, recurring, route-dense volume. Salons (#13/#17) are both a B2B laundry customer (towels) and a platform vertical.

### O12. Live tracking and ETA — #68

- **Have:** rider GPS pings, geofence auto-status, OTP.
- **Gap:** the customer sees a timeline, not a map or ETA.
- **Solution:** a live rider map within the last ~2 km, an ETA computed from pings, and proactive WhatsApp alerts ("Running 15 min late, new ETA 6:40").

### O13. Trust and accessibility — #19, #21, #90

- Show the rider's name, photo and a "KYC verified" badge before arrival. Rider KYC already exists.
- Women-rider preference where available.
- "Share live tracking" with family.
- Assisted booking for seniors: a family member books for a parent's address, or a call-back booking.

### O14. Donate & Recycle pickup — #112, #109

- The rider is already at the door, so the reverse pickup costs almost nothing.
- Add an "Add old clothes to donate / recycle" checkbox at booking, with loyalty points per kg.
- Monthly impact report ("You kept 6 kg of textile out of landfill").
- Partner with textile recyclers or NGOs. Cheap to build and good for the brand.

### O15. Platform (PaaS) plays — #13, #17, #48, #70, #71

- These sit with the "Shopify for scheduled hyperlocal services" strategy, not the laundry consumer roadmap.
- **Salon:** add preferred-staff booking and duration-aware slots to the existing `Salon` fulfilment strategy.
- **Tiffin:** day-level menu choice inside the `Recurring` strategy.
- **Courier / RaaS:** dynamically priced reverse pickups and pooled rates for small shippers, using the existing partner booking API.

---

## 6. Prioritisation

| Rank | Opportunity | Demand | Value | Edge | Complexity | **Score** | Main Itch refs |
|---|---|---|---|---|---|---|---|
| 1 | O1 Premium Fabric Care | 5 | 5 | 5 | 2 | **4.8** | #27 |
| 2 | O6 Flexible subscriptions | 5 | 5 | 4 | 3 | **4.4** | #73, #29, #48 |
| 3 | O2 Protection Guarantee / claims | 5 | 4 | 5 | 3 | **4.3** | #67, #72, #62 |
| 4 | O5 Online payments ⚑ prerequisite | 5 | 5 | 3 | 3 | **4.2** | #73, #1 |
| 5 | O7 WhatsApp-first booking | 5 | 4 | 4 | 3 | **4.1** | #19, #68 |
| 6 | O3 Quality Promise / 48 h re-do | 4 | 4 | 4 | 2 | **4.0** | #59, #64, #62 |
| 7 | O4 Price-Lock & exact savings | 4 | 4 | 4 | 2 | **4.0** | #57, #58, #28, #75 |
| 8 | O11 B2B & society billing | 4 | 5 | 4 | 4 | **3.9** | #76, #3 |
| 9 | O9 Honest rewards + referrals | 3 | 4 | 3 | 2 | **3.5** | #75, #78 |
| 10 | O10 Garment Passport | 3 | 3 | 4 | 2 | **3.4** | #22 |
| 11 | O8 SOS same-day | 4 | 3 | 3 | 3 | **3.3** | #29 |
| 12 | O15 PaaS vertical plays | 3 | 4 | 3 | 4 | **3.1** | #13, #17, #48, #70 |
| 13 | O12 Live tracking & ETA | 3 | 3 | 2 | 2 | **3.0** | #68 |
| 14 | O13 Trust & accessibility | 3 | 2 | 3 | 2 | **2.9** | #19, #21, #90 |
| 15 | O14 Donate & Recycle | 2 | 2 | 3 | 1 | **2.8** | #112, #109 |

⚑ O5 scores 4th, but it is **sequenced first** because O6, O9 and part of O2/O3 depend on it.

## 7. Roadmap

### Wave A (0–6 weeks): "Pay and Prove"
- **O5 stage 1:** payment-link checkout and wallet top-up via OTA.
- **O1:** fabric care tiers, specialist routing, customer-visible before-and-after photos, declared-value cover on invoice.
- **O3:** Accept / Report flow, zero-price rewash child order.
- **O4:** Price-Lock approval step, exact-savings checkout.
- **Exit KPIs:**
  - prepaid share ≥ 40% of app orders
  - premium-care GMV share
  - rewash rate visible per store
  - bill-variance disputes ↓

### Wave B (6–12 weeks): "Keep them"
- **O5 stage 2:** native Razorpay SDK, UPI Autopay.
- **O6:** subscriptions UI, standing pickups, pause / skip / rollover, credit swap.
- **O2:** claims module, policy engine, auto-claim on `fulfillment.lost`, garment timeline.
- **O9:** rewards screen, redemption, referrals, birthday automation.
- **Exit KPIs:**
  - subscriber count and MRR
  - 90-day retention
  - claim median resolution < 72 h
  - referral share of new users

### Wave C (quarter 2): "Reach and scale"
- **O7:** WhatsApp bot on MCP tools.
- **O11:** business accounts, society partnerships, GSTR-1 export.
- **O8:** SOS same-day; operating-hours / holiday API.
- **O10:** Garment Passport.
- **Exit KPIs:**
  - share of orders via WhatsApp
  - B2B revenue share
  - cost per pickup in society clusters

### Later
O12 live map and ETA, O13 trust features, O14 recycle pickup, O15 PaaS vertical plays (aligned with `PLATFORM_STRATEGY.md`).

## 8. Differentiation vs competitors

`docs/LaundryGhar_BRD_v2.docx` benchmarks **Tumble Dry** (consumer franchise chain) and **Dhobi Cart** (low-cost software for dry cleaners). The real incumbent, though, is the **neighbourhood dhobi / presswala / dry cleaner**.

| Promise | Neighbourhood dry cleaner | Typical app-based chain | **LaundryGhar (proposed)** |
|---|---|---|---|
| Delicate / designer care | Same harsh solvent for all (#27) | Usually a price multiplier | Process-routed care tier, specialist operators, photo proof |
| Loss / damage | Argument at the counter | Ticket, slow, discretionary | Written policy, auto-claim, **72 h SLA**, cover up to declared value |
| Final price | Changes at counter | Changes after pickup count | **Price-Lock**: you approve every change |
| Quality | Take it or leave it | Rating after the fact | **48 h free re-do**, visible QC card |
| Subscription | Monthly cash to dhobi | Fixed plans, cancel to stop | **Pause / skip / rollover** on UPI Autopay, no re-KYC |
| Ordering | WhatsApp / phone | App only | App **and** WhatsApp bot (Hindi), AI-assistant ready (MCP) |
| Transparency | None | Partial | Garment timeline, Garment Passport, exact ₹ savings |

**Structural moats.** These come from what is already built and are hard to copy quickly:
1. Per-garment barcode chain of custody plus inspection photos give guarantees evidence behind them.
2. An owned rider fleet with KYC, OTP and geofencing makes trust and SLA promises enforceable.
3. Declared-value slabs make insured-style cover priceable.
4. The MCP server and custom CQRS make a conversational channel cheap to add.
5. The **franchise SaaS layer**: every trust feature above can be sold to other laundries as a plan entitlement (`402 feature not in plan` already exists). That turns the consumer differentiator into SaaS revenue and goes after Dhobi Cart's market from the top end.

## 9. Risks and open questions

1. **Compensation exposure (O2):** caps, fraud controls (photo evidence at pickup is the main control), and franchise vs brand liability split need a business decision. `GAP_ANALYSIS.md` OQ-4 notes real prices are still placeholders.
2. **Price-Lock default** when the customer doesn't respond: auto-approve or hold. Hold protects trust; auto-approve protects turnaround time.
3. **Specialist capacity (O1):** premium care needs trained staff per warehouse. Start with one hub.
4. **Native payments (O5 stage 2)** need an EAS build and store release. The white-label app factory (T-22) is blocked, which affects per-tenant apps.
5. **Data verification:** confirm the Itch Scores on the live Razorpay page before using them in external material.
6. **File storage:** photo-heavy features (O1, O2, O10) need the S3 / Azure storage providers, which currently throw "not supported" (`FileStorageProviderFactory.cs`). Fix this before Wave A goes to production.
