---
name: project-security-hardening
description: Task #27 security-hardening decisions — header middleware pattern, password policy alignment, banner URL env-gating, http:// sweep findings
metadata:
  type: project
---

## Security hardening decisions (Task #27)

**Security headers middleware hook:**
`UseSecurityHeaders()` was added to `laundryghar.ServiceDefaults/Extensions.cs` (same namespace `Microsoft.Extensions.Hosting` as the existing `UseForwardedHeadersIfEnabled`). ServiceDefaults has NO app-pipeline hook — `AddServiceDefaults` is builder-only and `MapDefaultEndpoints` is routing-only. Per-service wiring via `app.UseSecurityHeaders()` is required in each Program.cs, matching the established `UseForwardedHeadersIfEnabled` pattern. Only Engagement was wired in this task; Identity/Catalog/Orders/Commerce/Logistics/Finance/Warehouse/Analytics are deferred (see live plan note in PRODUCTION_ENV.md).

**Why:** No shared pipeline hook exists in ServiceDefaults for app-level middleware; all existing middleware calls (ForwardedHeaders, CORS, Auth) are per-service.
**How to apply:** When adding new pipeline middleware to all services, always wire it in each service's Program.cs individually.

## Password policy decision

`PasswordLoginValidator.Password` was aligned from `MinimumLength(6)` to `MinimumLength(8)` + uppercase + digit, matching `ResetPasswordValidator`. Decision: safe to align because:
- All password-SET paths (reset, create-user, invite-accept) already enforce 8+complexity.
- The seeded dev admin password `Admin@123` (9 chars, 1 upper, 1 digit) passes.
- No legacy user can have a sub-8 or complexity-missing password via normal flows.

**Why:** Login validator was an input-bound check, not a business rule. Aligning it closes the gap where a theoretical pre-enforcement legacy user (possible only via direct DB insert) could authenticate with a weak password.

## Banner URL http:// env-gating

`AppBannerRules.IsAllowedHttpUrl` and `IsAllowedDeeplink` now accept a `bool isDevelopment` parameter. In Production/Staging only `https://` is accepted for HTTP URLs; in Development `http://` is also accepted. Validators (`CreateAppBannerValidator`, `UpdateAppBannerValidator`) inject `IWebHostEnvironment` via DI (FluentValidation DI registration supports this).

**Why:** Banner image/CTA URLs are served to mobile clients — plain http:// in production enables MITM and mixed-content attacks.

## http:// sweep findings

Only one `http://` literal in non-dev source found outside test/schema code:
- `laundryghar.Identity/Application/Settings/SettingsStore.cs:65` — `const string fallback = "http://localhost:5173"` — dev-only default for admin SPA base URL used when no DB setting exists. Not a risk: non-dev deployments always have the setting populated. Outside the task lane (Identity source); no fix applied.

No `http://` literals in any base `appsettings.json` files.
