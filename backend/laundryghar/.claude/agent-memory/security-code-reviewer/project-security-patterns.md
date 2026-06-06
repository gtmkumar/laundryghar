---
name: project-security-patterns
description: LaundryGhar security architecture — auth mechanisms, secrets management, known gaps, and recurring patterns found during review
metadata:
  type: project
---

## Secrets abstraction (`ISecretsProvider`)

Introduced in the `laundryghar.ServiceDefaults/Secrets/` namespace. Wired into all 10 services via `AddServiceDefaults()`. Active provider selected by `Secrets:Provider` config key.

- `env` (default): no-op; dev is byte-for-byte unchanged.
- `file`: Docker/k8s secret-mount convention; directory set by `Secrets:FilePath`.
- Cloud seams (`azure-keyvault`, `aws-secretsmanager`, `vault`): throw `NotSupportedException` with instructions — not yet implemented.

**Known open issues (backlog per PRODUCTION_ENV.md):**
- DB password and `Jwt__PrivateKey` not yet in a real secrets manager — still env-var injected.
- AutoMapper 13.0.1 CVE present (unused dependency — remove).

## JWT: RS256 + JWKS (migrated from HS256)

Identity signs with RSA private key; verifiers fetch `/.well-known/jwks.json`. `RsaJwtKeyProvider` auto-generates a dev key but throws in production if key is absent (fail-closed). Key path: `laundryghar.Identity/Infrastructure/Auth/RsaJwtKeyProvider.cs`.

## Token storage

- **Mobile (customer-mobile):** Zustand + `expo-secure-store` (hardware-backed keychain). Correct.
- **Admin-web:** Zustand + `localStorage`. Access token and refresh token stored in `localStorage`. This is a known XSS-to-token-theft risk for an admin surface.

## Banner deep-link trust boundary

`ctaDeeplink` and `externalUrl` fields flow: CMS DB → Engagement API → mobile client. Backend `CreateAppBannerValidator` has NO URL validation on `CtaDeeplink` or `ExternalUrl` — only `ImageUrl` and `Placement` are validated. Mobile code (`resolveBannerPress`) guards: in-app paths must start with `/`; external URLs must match `^https?://`. Non-matching strings are silently dropped. However, `javascript:` URIs do not match `^https?://` so the mobile guard is correct for that vector. The risk is `content://`, `file://`, `intent://` schemes which the mobile guard does not explicitly block — platform-dependent exposure.

## Admin-web analytics/commerce API modules

No token leakage found. Token attachment is centralized in `client.ts` interceptors. Brand scoping via `X-Brand-Id` header is applied to all requests. No `dangerouslySetInnerHTML` or eval in analytics pages.

## FileSecretsProvider specifics (security-relevant gaps)

1. No file size limit — a pathologically large file under `/run/secrets` would be fully read into memory.
2. `Directory.EnumerateFiles(_directory)` uses `SearchOption.TopDirectoryOnly` (default) — subdirectories are not traversed. Good.
3. `File.ReadAllText(filePath).Trim()` strips leading/trailing whitespace including `\n` from PEM values. `RSA.ImportFromPem()` is tolerant of missing trailing newline but this is a latent correctness hazard for multi-line PEM content edge cases.
4. No symlink dereferencing protection — a symlink inside the mounted secrets dir could point outside the directory. In typical k8s secret mounts this is not attacker-controllable, but worth noting.
5. `Console.Error.WriteLine` on IOException leaks the file path (`filePath`) to stderr. Does not leak secret values.
6. `GetAwaiter().GetResult()` in `SecretsConfigurationProvider.Load()` is intentional (sync contract). `FileSecretsProvider.LoadAsync` is actually synchronous underneath — no real async I/O. Deadlock risk is theoretical for network-backed future providers but not present now.

**Why:** These gaps were identified during the session-scoped security review of the new secrets abstraction.
**How to apply:** Flag these on any future PR touching `FileSecretsProvider` or when cloud provider seams are wired.
