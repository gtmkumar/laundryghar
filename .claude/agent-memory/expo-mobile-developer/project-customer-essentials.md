---
name: project-customer-essentials
description: Task #15 customer-app essentials — addresses, ratings, profile edit, wallet top-up plumbing, help screen
metadata:
  type: project
---

Task #15 (2026-06-10) — customer-mobile essentials shipped.

**What was done:**
- Address CRUD already existed in backend (GET/POST/PUT/DELETE + self-filter). App layer added createAddress/updateAddress/deleteAddress/checkServiceability to catalog.ts + mutation hooks in useCatalog.ts.
- `app/(app)/addresses.tsx` — full addresses screen (list, add modal, edit modal, delete confirm, set-default).
- `app/(app)/booking/pickup.tsx` — address picker modal replaces hardcoded demo. Booking flow now uses real address from saved addresses.
- Serviceability: territories table has `pincodes TEXT[]`, stores table has `pincode`. Added `GET /api/v1/customer/serviceability?pincode=` to Catalog CustomerEndpoints (checks stores first, then territories). Pincode format validated 6-digit. **Serviceability check exists via stores data (territories array is currently empty in dev DB).**
- Ratings: columns `rating`, `rating_comment`, `rated_at` exist on `orders` table and are mapped in Order entity. Added `RateOrderCommand` + `RateOrderValidator` (new file). Added `POST /api/v1/customer/orders/{id}/rate` (idempotent upsert, CustomerOnly self-filter, only for delivered/closed). `OrderDto` extended with optional `Rating`, `RatingComment`, `RatedAt` fields at end (default null — all existing ToDto calls unaffected). Rating prompt added to orders/[id].tsx and tracking/[id].tsx (5-star + comment + thank-you state).
- Profile edit: patchProfile() existed in catalog.ts. Added inline EditProfileForm card in profile.tsx (name/email fields, phone read-only, save → PATCH → toast).
- Wallet top-up: FEATURES.walletTopUp=false in config. 'Add money' → WalletTopUpSheet (coming-soon modal with amount previews). Plumbing ready for Razorpay.
- Help screen: `app/(app)/help.tsx` — FAQ (8 questions, 4 categories), Contact Us (email/call/WhatsApp via Linking), Grievance Officer from GET :5007/api/v1/public/app-config (configKey=grievance_officer, DPDP Act Clause 13).

**Grievance config**: seed_grievance_config.sql already applied — 4 rows in DB (android+ios per brand).

**DB patches applied**: NONE needed — all columns (rating, rating_comment, rated_at) already existed in schema.

**TypeScript gate**: 0 errors. Added `closed` to OrderStatus union; updated ORDER_RANK and ORDER_STATUS_TONE/LABEL maps.

**Backend build gate**: 0 errors. `dotnet build laundryghar.slnx`.

**CustomerAddressDto field mapping note**: field is `addressLine1` (not `line1`). Fixed home.tsx addrLabel reference.

**Why:** [[project-customer-mobile]]
