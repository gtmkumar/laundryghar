---
name: project-core-consolidation
description: Core service (Identity+Engagement+Mcp) QA after 11→3 consolidation — defect classes, soft vs hard delete, test gotchas
metadata:
  type: project
---

laundryghar.Core service (http://localhost:5050) = consolidated former Identity + Engagement + Mcp. QA'd live 2026-06-13 after the 11→3 service consolidation. Gateway :8080 strips /identity/* and /engagement/* prefixes; both route correctly.

**Why:** First full live CRUD sweep of the consolidated Core service. Establishes the defect baseline for this service.

**How to apply:** Re-check these defect classes on any PR touching Core endpoints or the global exception handler.

## Defect classes found (2026-06-13)
1. **Bare-list 500 (HIGH):** Identity list endpoints declare `int page, int pageSize` as required, non-defaulted query params → omitting them returns HTTP 500 `BadHttpRequestException: "Required parameter int page was not provided"`. Affects GET (no query): admin/users, admin/roles, admin/platforms, admin/franchises, admin/stores, admin/warehouses. Engagement CMS endpoints are NOT affected (they use `int page = 1, int pageSize = 20` defaults). Fix = add the same defaults in AdminUserEndpoints.cs + AdminTenancyEndpoints.cs. Backend owner.
2. **Raw DB-constraint error leak (MEDIUM):** Invalid enum/JSON inputs surface raw PostgreSQL SQLSTATE codes through HTTP 400 instead of clean validation messages. Seen: `23505` (duplicate membership grant), `23514` (notification_templates_status_check on bad status), `22P02` (invalid JSON in app-config configValue). Global exception handler / handlers should validate before INSERT and map DbUpdateException → friendly 422. Backend owner.

## Behaviors confirmed correct
- Auth: password login/refresh/logout, customer OTP send+verify (phone MUST be E.164 e.g. +919999000001; master OTP 123456), customer /me rejects system tokens with 403, forgot-password returns generic non-enumerating message, reset with bad token → 401.
- Well-known: jwks.json, openid-configuration, oauth-protected-resource all 200 + sane bodies. MCP /mcp without token → 401 with `WWW-Authenticate: Bearer resource_metadata="…/.well-known/oauth-protected-resource"` (RFC 9728 correct).
- Validation errors use ApiResponse envelope with errorTypeCode 422 (not 400). 401/404 have empty bodies (standard ASP.NET).

## Delete semantics (inconsistent — by design?)
- HARD delete (row vanishes from list): franchises, stores, warehouses, app-banners, onboarding-slides, app-config.
- SOFT delete (status→archived, isActive→false, STAYS in list): notification-templates. Cannot be hard-removed via API — QA template leftovers are expected.

## Endpoint gaps (no endpoint exists)
- Users: no hard DELETE (only POST /deactivate → status=suspended).
- Brands: full CRUD exists but treat as read-only in QA.
- Platforms: GET + POST only — no PUT/DELETE. Avoid creating QA platforms (undeletable leftover).

## Test gotchas
- Membership revoke takes `{membershipId}` (the row id from grant response), NOT {userId,scopeType,scopeId,roleId}.
- app-config configValue is a JSON column — must pass valid JSON string e.g. "{\"k\":true}", not a bare string.
- zsh: `$UID` is readonly; use a different var name. curl not always on PATH in profile-stripped subshells — use /usr/bin/curl.
