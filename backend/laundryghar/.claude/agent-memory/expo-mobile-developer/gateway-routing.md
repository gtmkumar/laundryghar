---
name: gateway-routing
description: Both Expo apps route all backend traffic through the API Gateway :8080/<prefix> in dev (not direct service ports)
metadata:
  type: project
---

As of 2026-06-13 both customer-mobile and rider-mobile route 100% of backend traffic through the API Gateway (YARP) on `:8080/<prefix>` instead of direct service ports.

**Why:** Consolidate to a single ingress; YARP strips the prefix and forwards. Native apps have no CORS concern so the gateway is safe to hit directly.

**How to apply:**
- `CONFIG.*ApiUrl` defaults are built by a `gw(prefix)` helper in each app's `src/constants/config.ts` => `http://${DEV_HOST}:8080/${prefix}`. The `extra['...']` override (from app.config.ts env vars) still wins for prod.
- Gateway prefix map: `/identity,/engagement` -> core(5050); `/catalog,/orders,/warehouse,/logistics` -> operations(5002); `/commerce,/finance,/analytics` -> commerce(5005).
- Customer uses prefixes: identity, catalog, orders, commerce, engagement. Rider uses: identity, logistics, engagement.
- Do NOT reintroduce direct `:5050/:5002/:5005` URLs in fetch/axios calls. All clients funnel through `CONFIG.*ApiUrl` in `src/api/client.ts` (+ engagement.ts). A guard test exists at `src/__tests__/config.test.ts` in each app asserting `:8080/<prefix>` and no `:50xx`.
- RUNTIME-PROOF NOTE: RN networking does NOT log request URLs in logcat (release-style). Prove gateway routing by (a) curl the real route at `:8080/identity/api/v1/customer/auth/otp/send` returns 200, and (b) on-device the app logs in + loads real catalog data (price-list shows real ₹ amounts) — success there is only possible if the app is hitting :8080.

See [[android-emulator-testing]] for the emulator driving gotchas. [[customer-mobile-v2]] [[rider-mobile-v2]]
