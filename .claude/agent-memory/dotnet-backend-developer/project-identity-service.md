---
name: project-identity-service
description: Identity microservice architecture decisions, critical DB constraints, and runtime quirks discovered during Phase 1 build
metadata:
  type: project
---

## refresh_tokens self-referential family_id FK

The live DB has `identity_access.refresh_tokens.family_id` as a NOT NULL FK referencing `refresh_tokens(id)` itself. The root token in a family must have `family_id = own id`. EF Core cannot resolve this circular FK in a single INSERT. Solution: `RefreshTokenRepository.InsertRootAsync()` uses raw parameterized SQL (`ExecuteSqlAsync` with FormattableString) for root token inserts. Rotated tokens (which point to an existing row as family_id) go through EF normally.

**Why:** Npgsql throws 23503 FK violation if you try to EF-insert a root token with a new `family_id` UUID that doesn't yet exist in the table.

**How to apply:** Any future code that creates the first token in a refresh family (login, OTP verify) must use `RefreshTokenRepository.InsertRootAsync`. Rotation uses EF Add() normally.

---

## roles.scope_type DB CHECK constraint

`identity_access.roles` has a CHECK constraint: scope_type IN ('platform','brand','franchise','store','warehouse'). The `territory` scope type is NOT in this list even though `user_scope_memberships.scope_type` includes it. The `regional_manager` system role uses `scope_type = 'brand'` as a workaround (territory scope not supported on roles table).

**Why:** Live DB DDL differs from the BUILD_PLAN roles table description. Live DB wins.

---

## Permission seeder count

50 permissions seeded across modules: platforms, brands, territories, franchise_agreements, franchises, stores, warehouses, operating_hours, holidays, store_warehouse, users, roles, permissions, memberships, orders. `platform_admin` role gets all 50. Other roles get subsets defined in `IdentitySeeder.SeedRolePermissionsAsync`.

---

## MediatR 12 RequestHandlerDelegate

MediatR 12+ changed `RequestHandlerDelegate<T>` — it takes no arguments. Call `await next()` not `await next(cancellationToken)`.

---

## Dev admin credentials

- Email: admin@laundryghar.local  
- Password: Admin@123  
- Phone: +919999999999  
- UserType: platform_admin  
- Scope: platform (bypasses RLS, gets all permissions in JWT)

Seeded idempotently at startup in Development environment.

---

## Connection string note

Default connection runs as `postgres` superuser — bypasses RLS by design for dev. Phase 3: switch to `ConnectionStrings:AppRuntime` (app_user) after running `db/patches/app_user_role.sql` to test RLS isolation.

---

## Customer auth + BC-3 catalog additions

**token_use claim (pinned contract for Catalog service):** System tokens = `token_use=user`; customer tokens = `token_use=customer`. Both always present. `CustomerTokenClaims.TokenUseValue` / `TokenClaims.TokenUseValue` are canonical. Never remove.

**PermissionHandler gate:** Silently rejects tokens where `token_use != "user"` — customers can never satisfy admin permission policies.

**CustomerOnly policy:** Named `"CustomerOnly"` in `PermissionPolicyProvider`. `CustomerOnlyHandler` succeeds only for `token_use=customer`.

**Customer OTP brand scoping:** `otp_codes.ReferenceId = brand_id`, `ReferenceType = "brand"`. Verify filters on this — blocks cross-brand OTP use.

**tsvector SearchTokens:** `Item.SearchTokens` uses `b.Ignore()` in `ItemConfiguration` (Npgsql EF 10 cannot map `string → tsvector`). Column is DB-managed by trigger.

**Customer signup-on-first-login:** `CustomerOtpVerifyHandler` creates `customer_catalog.customers` row on first verify. Returns `isNewCustomer=true`.

**ClaimTypes.NameIdentifier vs "sub":** JwtBearerMiddleware maps `sub` → `ClaimTypes.NameIdentifier`. Use `FindFirstValue(ClaimTypes.NameIdentifier)` in Minimal API handlers.

**BC-3 permissions:** 22 codes added (catalog.*, pricing.*, customer.*). brand_admin = full; franchise_owner/store_admin/store_staff/auditor = read-only subsets.

**BC-4 permissions:** 16 codes added (orders.read/status.update/notes.manage; pickup.read/create/assign; delivery.slot.read/manage/assign; garment.read/tag/inspect; warehouse.batch.manage/process.scan; qc.perform; stockrecon.manage). Total catalog after BC-4: 89 live permissions in DB. Note: pre-existing placeholder codes orders.list/create/update/cancel/refund were kept as-is; orders.read is a separate new code. platform_admin auto-gets all; brand_admin gets all BC-4; warehouse roles get garment+warehouse+qc+stockrecon; store roles get orders+pickup+delivery.slot+garment ops; auditor gets all *.read.

**Customer OTP brand-scoping (H1):** Both the cooldown query and the invalidation query in `CustomerOtpSendHandler` must filter `&& o.ReferenceId == cmd.ResolvedBrandId && o.ReferenceType == "brand"`. Without this, a Brand B send would stomp Brand A's pending OTP for the same phone (cross-brand DoS).

**DPDP opt-ins default false (L1):** New customer rows default all five opt-in flags to `false`. Affirmative consent captured later via `dpdp_consents`. Transactional messages don't require the marketing flags.

**Phone PII masking (L2):** `CustomerOtpSendHandler.MaskPhone()` keeps country code + last 4, masks middle with X. Both customer handlers inject `IHostEnvironment`; full phone shown only in Development.

**JWT algorithm pinned (M1):** `ValidAlgorithms = [SecurityAlgorithms.HmacSha256]` in Identity `TokenValidationParameters`. Prevents algorithm downgrade. Matches Catalog service config.

---

## Security remediation decisions (Phase 1 QA round)

**RLS interceptor lifetime (H1):** `RlsConnectionInterceptor` is `Scoped`, not Transient. EF Core caches the interceptor for the DbContext lifetime; Transient gives the same effective lifetime as Scoped, but Scoped makes the intent explicit and is safe. `set_config` uses `is_local=false` (session-level) intentionally — Npgsql resets state on pool return and ConnectionOpened fires on every logical open.

**GrantMembership privilege escalation (H2):** `ICurrentUser` passed through `GrantMembershipCommand` record. Three guards: (a) platform_admin role requires platform_admin actor; (b) actor brand_id must cover target scope (non-platform-admins only); (c) target role Priority >= actor's minimum role Priority (lower number = higher rank). `UnauthorizedAccessException` maps to 401 via ExceptionHandler.

**UpdateUser mass-assignment (H3):** `UpdateUserRequest` has no Status or UserType fields. Separate `SetUserTypeCommand` behind `permission:users.set_type` validates actor cannot elevate beyond their own type priority. Status change only through `/deactivate` endpoint.

**Environment guards (C4):** `DevLogOtpSender` DI-registered only in Development; `Msg91OtpSender` stub (throws `NotImplementedException`) in other envs. `ForgotPasswordHandler` raw-token log guarded by `IHostEnvironment.IsDevelopment()`. Seeder throws if run outside Development. CORS allow-all only in Development. `Jwt:SigningKey` absence throws at startup in non-Development.

**Rate limiter (C5):** `FixedWindowLimiter` "auth" policy: 10 req / 60 s per client IP, `QueueLimit=0`, 429 on overflow. Applied at the `/api/v1/auth` group level. OTP send also enforces per-identifier cooldown from `OtpSettings.ResendCooldownSeconds` (default 60 s).

**OtpSettings config binding (L6):** `TtlMinutes`, `MaxAttempts`, `ResendCooldownSeconds` read from `Otp` config section via `IOptions<OtpSettings>`.

**Middleware order (L4):** UseAuthentication → TenantResolutionMiddleware → UseAuthorization. Seeder admin password from `Seeder:AdminPassword` config; falls back to `"Admin@123"` only in Development.
