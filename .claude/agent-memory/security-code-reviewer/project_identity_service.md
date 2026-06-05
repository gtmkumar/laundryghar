---
name: project-identity-service
description: Security posture of the laundryghar.Identity microservice — auth, RBAC, RLS, token handling patterns as found in the first review pass
metadata:
  type: project
---

This is a multi-tenant franchise SaaS (Laundry Ghar) with brand/franchise/store hierarchy. Tenant isolation is enforced via PostgreSQL RLS using session variables set by RlsConnectionInterceptor.

**Key security findings documented (initial review):**
- JWT is HS256 (symmetric) in dev; spec requires RS256 for prod — not yet implemented. SigningKey committed in appsettings.Development.json.
- DevLogOtpSender and ForgotPasswordHandler both registered unconditionally (not environment-gated) — would ship to prod if not swapped.
- OTP is SHA-256 hashed (not Argon2id/bcrypt) — acceptable for 6-digit short-lived codes but note deviation from spec language.
- Seeder creates default admin (admin@laundryghar.local / Admin@123) unconditionally in dev; seeds run on every Development startup.
- RlsConnectionInterceptor uses `is_local=false` (session-level) — connection pool reuse risk if EF Core pools connections across requests.
- CORS is allow-all with no environment gate — registered unconditionally.
- No HTTP rate limiting middleware in Program.cs at all.
- GrantMembership has no scope/role authorization check preventing privilege escalation by users with memberships.grant permission.
- UpdateUser endpoint allows changing UserType and Status fields — mass-assignment risk.
- app_user role password is 'app_user' hardcoded in SQL patch — must be changed pre-prod.
- `PasswordLoginHandler` lockout resets FailedAttempts to 0 after lock is applied, which could allow bypassing the lock counter after it expires.

**Why:** These are must-fix items before any production deployment.
**How to apply:** Reference these when reviewing auth, RLS, or CORS changes in this service.
