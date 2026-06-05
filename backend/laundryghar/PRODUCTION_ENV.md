# Production environment variables

The base `appsettings.json` of every service is intentionally **secret-free**.
Development values live in `appsettings.Development.json` (loaded only when
`ASPNETCORE_ENVIRONMENT=Development`). In Production these files provide no
credentials, so the following MUST be injected via environment variables (or a
secrets manager / Key Vault). Services **fail closed** — they throw on startup if
a required value is missing.

## Required for every service (9 APIs + Worker)

| Env var | Purpose | Notes |
|---|---|---|
| `ConnectionStrings__Default` | Runtime DB connection | MUST be a **non-superuser** role (e.g. `app_user`) so PostgreSQL RLS is enforced. Never `postgres`. |
| `Jwt__SigningKey` | JWT HS256 signing key | ≥ 32 chars. Same value across all services (shared verification). Startup throws in non-Development if unset. |

## Do NOT set in Production

| Env var | Why |
|---|---|
| `ConnectionStrings__Admin` | Privileged (superuser) connection used **only** by Development data seeding, which is hard-disabled outside Development (seeders throw). Omit it in Production. |

## Example (Production)

```bash
export ConnectionStrings__Default="Host=db.internal;Port=5432;Database=laundry_ghar_db;Username=app_user;Password=<secret>"
export Jwt__SigningKey="<32+ char secret, identical across services>"
export ASPNETCORE_ENVIRONMENT=Production
```

## Still on the hardening backlog (tracked in orchestrator memory)

- JWT HS256 (shared secret) → **RS256 + JWKS** (Identity signs with a private key;
  services verify via published public keys — no shared secret to leak).
- `Jwt__SigningKey` / DB password → a real secrets manager (Key Vault / SSM),
  not raw env vars.
