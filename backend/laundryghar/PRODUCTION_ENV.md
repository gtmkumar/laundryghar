# Production environment variables

The base `appsettings.json` of every service is intentionally **secret-free**.
Development values live in `appsettings.Development.json` (loaded only when
`ASPNETCORE_ENVIRONMENT=Development`). In Production these files provide no
credentials, so the following MUST be injected via environment variables (or a
secrets manager / Key Vault). Services **fail closed** — they throw on startup if
a required value is missing.

## Required for every service (9 APIs + Worker)

**Every service (9 APIs + Worker):**

| Env var | Purpose | Notes |
|---|---|---|
| `ConnectionStrings__Default` | Runtime DB connection | MUST be a **non-superuser** role (e.g. `app_user`) so PostgreSQL RLS is enforced. Never `postgres`. |

**JWT auth is RS256 + JWKS** (no shared secret). Identity signs with a private key; the
other services verify by fetching Identity's public key from its JWKS endpoint.

| Env var | Set on | Purpose |
|---|---|---|
| `Jwt__PrivateKey` | **Identity only** | RSA private key **PEM** used to sign RS256 tokens. Inject from Key Vault / secret store. (`Jwt__PrivateKeyPath` to a mounted PEM file is the alternative.) Identity throws on startup if neither is set outside Development. |
| `Jwt__Authority` | **the 8 verifying services** | Base URL of Identity (e.g. `https://identity.internal`); its `/.well-known/openid-configuration` → JWKS supplies the RS256 public key. Required in all environments (services throw if unset). Use HTTPS in production (`RequireHttpsMetadata` is on outside Development). |

The public key is served at `GET {Identity}/.well-known/jwks.json`; rotating the private key
is picked up automatically by each service's cached JWKS (no redeploy of verifiers needed).

## Do NOT set in Production

| Env var | Why |
|---|---|
| `ConnectionStrings__Admin` | Privileged (superuser) connection used **only** by Development data seeding, which is hard-disabled outside Development (seeders throw). Omit it in Production. |
| `Jwt__SigningKey` | Removed — the old shared HS256 secret no longer exists. |

## Example (Production)

```bash
# All services
export ConnectionStrings__Default="Host=db.internal;Port=5432;Database=laundry_ghar_db;Username=app_user;Password=<secret>"
export ASPNETCORE_ENVIRONMENT=Production

# Identity (issuer) — RSA private signing key
export Jwt__PrivateKey="$(cat /run/secrets/jwt-signing-key.pem)"

# The 8 verifying services — where to fetch Identity's JWKS
export Jwt__Authority="https://identity.internal"
```

## Still on the hardening backlog (tracked in orchestrator memory)

- DB password + `Jwt__PrivateKey` → a real secrets manager (Key Vault / SSM)
  rather than raw env vars (the RS256/JWKS migration is done).
- AutoMapper 13.0.1 CVE — remove (it is unused; inline projections everywhere).
