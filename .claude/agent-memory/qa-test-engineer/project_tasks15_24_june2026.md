---
name: tasks-15-24-verification-june2026
description: Live verification results for Task #15 (Customer Essentials) and Task #24 (Mobile Reliability) as of 2026-06-10
metadata:
  type: project
---

Task #15 PASS-with-defects and Task #24 PASS as of 2026-06-10.

**Why:** Verification run by QA agent 5B on the live stack.

**How to apply:** Use these as the known defect baseline when re-testing after fixes.

## Defects found

### DEF-A (Major, #15a): Label validator missing allowlist check — raw DB 23514 leaks to client
- File: `backend/laundryghar/laundryghar.Catalog/Application/Customer/Self/Commands/SelfCommands.cs` line 456
- `CreateAddressValidator` validates `Label` only as `NotEmpty().MaximumLength(50)` — no `.Must()` check against `["home","office","other"]`
- DB CHECK constraint `customer_addresses_label_check` rejects invalid value → EF throws `DbUpdateException` → raw `23514` PostgreSQL error leaks in 400 response body
- Reproduced: POST /api/v1/customer/addresses with `"label":"work"` → 400 with raw constraint text

### DEF-B (Minor, #15b): Serviceability 400 response body is empty
- Invalid pincode (12AB) → HTTP 400 with body `{"message":null,"status":false}` — no field-level error detail
- Code: `CustomerEndpoints.cs` line ~115 returns `Results.BadRequest(new Response { Status = false })` — intentional but provides no usable error to mobile client

### DEF-C (Info, #15e): Duplicate `grievance_officer` rows seeded in mobile_app_config
- Both android and ios platforms have 2 identical `grievance_officer` rows each (4 total duplicates)
- Endpoint still returns both; client should deduplicate but duplication is a seed script issue

### DEF-D (Known gap, #24b): rider app_type rows absent from mobile_app_config
- No rider rows in `engagement_cms.mobile_app_config` — gate silently passes (safe behavior, documented in code comment)

## Verified PASS items
- #15a: POST/PUT/DELETE address CRUD works; isDefault demotion confirmed in DB; IDOR PUT → 404; GET /{id} returns 405 (no route exists)
- #15b: serviceable(122001)=true, serviceable(999999)=false, invalid(12AB)=400
- #15c: Rate delivered order → 200 + DB confirms rating/comment/rated_at; re-rate updates; cancelled order → 422; IDOR foreign order → 404
- #15d: GET :5007/api/v1/public/app-config?brandCode=LG-MAIN → grievance_officer row present with all DPDP fields
- #15e: PKP-2026-5B37-000001 confirmed belonging to customer 5e246fde; list self-filters correctly
- #24a: Both apps have @sentry/react-native ~6.10.0 + expo-updates ~0.27.5; app.config.ts has updates/runtimeVersion/plugin entries; FEATURES.crashReporting/otaUpdates/versionGate=true; .env.example documents EXPO_PUBLIC_SENTRY_DSN
- #24b: customer app_settings rows parseable; min_version/force_update_version/maintenance_mode keys present; rider gap documented
- #24c: All boot steps in both _layout.tsx failure-isolated (Sentry.init no-throw by design; OTA await inside try/catch); no unguarded top-level awaits
- #24d: `tsc --noEmit` exits 0 in both customer-mobile and rider-mobile
