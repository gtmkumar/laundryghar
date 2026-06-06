---
name: project-rider-mobile
description: Rider Mobile app scaffold — location, stack, API contracts, screen inventory
metadata:
  type: project
---

`rider-mobile/` scaffolded at repo root. Expo SDK 52 + TypeScript + NativeWind + expo-router + TanStack Query + Zustand + axios + expo-location.

**Why:** First integration slice for the Laundry Ghar Rider app. Mirrors customer-mobile patterns exactly.

**How to apply:** When adding screens or hooks to rider-mobile, follow the same api/client.ts factory + store/authStore.ts + hooks/useRider.ts pattern. Run `npm run typecheck` to gate changes.

Key facts:
- Brand color: green (#15803D) vs customer blue (#1D4ED8)
- Rider auth = password login (NOT OTP): `POST {Identity}/api/v1/auth/password/login` body `{ identifier, password }`
- Identity service: **localhost:5050** (not 5000); Logistics service: localhost:5004; Engagement service: localhost:5007
- **No seeded riders** (verified 2026-06-06): `logistics.riders` table is empty; no `user_type='rider'` users in identity seeder. End-to-end rider login cannot be tested without first creating a rider via the admin/ops API or direct DB seed.
- Rider self-service prefix: `{Logistics}/api/v1/rider/*` (RiderOnly policy = bearer with user_type=rider)
- Four API endpoints wired: GET /rider/me, GET /rider/assignments/today, PATCH /rider/assignments/{id}/status, POST /rider/location/ping
- Engagement: `GET {Engagement}/api/v1/public/banners?placement=home_top&brandCode=LG-MAIN` — anonymous, flat ListResponse<AppBannerDto>. AppBannerDto has id, title, subtitle, imageUrl, ctaText, ctaDeeplink, externalUrl, backgroundColor.
- GPS: foreground one-shot ping via expo-location. Background tracking deferred (needs expo-task-manager + native build).
- SecureStore keys: `lg_rider_access_token`, `lg_rider_refresh_token`
- Tab routes: assignments (today's jobs — has home_top banner carousel at top), location (GPS ping), profile
- Stack route: /(app)/assignments/[id] (assignment detail + status update)
- Banner fallback: renders nothing (returns null) when API fails or returns empty — never crashes

[[project-customer-mobile]]
