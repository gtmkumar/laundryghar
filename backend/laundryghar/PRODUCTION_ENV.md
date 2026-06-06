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

## Secrets abstraction (`Secrets:Provider`)

All services use the cloud-agnostic `ISecretsProvider` abstraction wired into
`AddServiceDefaults()`. The active provider is chosen by a single config key.

### `Secrets:Provider` values

| Value | Behaviour | When to use |
|---|---|---|
| `env` **(default)** | No-op — all secrets come from environment variables or `appsettings.*.json`. Dev is unchanged. | Local dev; Aspire; any env where secrets are already injected as env vars. |
| `file` | Reads secrets from a mounted directory. Each file's **name** is the config key (using `__` as the hierarchy separator); its **content** is the value. | Docker / Kubernetes secret-volume mounts. |
| `azure-keyvault` | Not yet wired — throws `NotSupportedException` with instructions. | Azure: add `Azure.Extensions.AspNetCore.Configuration.Secrets` + `Azure.Identity`, then implement the seam in `SecretsProviderFactory`. |
| `aws-secretsmanager` | Not yet wired — throws `NotSupportedException` with instructions. | AWS: add `AWSSDK.SecretsManager`, implement `AwsSecretsManagerProvider`. |
| `vault` | Not yet wired — throws `NotSupportedException` with instructions. | HashiCorp Vault: add `VaultSharp`, implement `VaultSecretsProvider`. |

### File-mount convention (`Secrets:Provider = file`)

Set `Secrets:FilePath` to the absolute path of the mounted directory.

```bash
export Secrets__Provider=file
export Secrets__FilePath=/run/secrets
```

Each file under that directory is mapped to a config key:

| File name | Maps to config key |
|---|---|
| `ConnectionStrings__Default` | `ConnectionStrings:Default` |
| `Jwt__PrivateKey` | `Jwt:PrivateKey` |

File content is trimmed of leading/trailing whitespace. Missing or unreadable files
produce a warning on `stderr`; the service then fails closed at first access of the
missing key (e.g. `GetConnectionString("Default") ?? throw`).

### Precedence

The secrets layer sits above `appsettings.json` / `appsettings.{env}.json` in config
resolution but below environment variables. Env vars (including those injected by
Aspire's `WithEnvironment(...)`) always win, which means the `env` (default) provider
is a guaranteed no-op in Development.

### Wiring cloud providers

Each unsupported provider has a documented seam in
`laundryghar.ServiceDefaults/Secrets/SecretsProviderFactory.cs`. To activate one:

1. Add the required NuGet package (comments in the source list each package).
2. Replace the `throw new NotSupportedException(...)` arm with the real implementation.
3. No per-service changes are needed — all 10 services inherit the change via `AddServiceDefaults()`.

## Still on the hardening backlog (tracked in orchestrator memory)

- DB password + `Jwt__PrivateKey` → a real secrets manager (Key Vault / SSM)
  via the `file` or `azure-keyvault` / `aws-secretsmanager` provider above
  (the RS256/JWKS migration is done; `ISecretsProvider` seams are ready).
- AutoMapper 13.0.1 CVE — remove (it is unused; inline projections everywhere).
