---
name: project-oauth-authz-server
description: OAuth 2.1 authorization-server facade on Identity (RFC 8414/7591/7636/9728): key design decisions, what was deliberately NOT changed, and DB migration pattern
metadata:
  type: project
---

OAuth 2.1 authorization-server facade added to Identity (port 5050) and MCP (port 5009).

**Why:** MCP clients (Claude.ai, Claude Code, Gemini CLI) need to authenticate customers via an OAuth 2.1 flow. No OpenIddict/IdentityServer — hand-rolled minimal-API style to match the service's existing minimal-API pattern.

## Design decisions worth remembering

**PKCE code stored hashed, not raw.** The raw authorization code (≥ 256 bits / Base64URL) is sent to the client only once; SHA-256(code) is what goes in oauth_authorization_codes.code_hash. At /oauth/token the raw code is received, hashed, and looked up.

**Single-use enforcement is atomic.** `UPDATE … SET consumed_at = now() WHERE code_hash = … AND consumed_at IS NULL AND expires_at > now()` — checking `affected == 0` is the only gate. No SELECT-then-UPDATE; no TOCTOU window.

**Loopback redirect URIs are port-agnostic (RFC 8252 §8.3).** http://localhost and http://127.0.0.1 are matched by scheme+host only; the port in the requested URI is ignored but the path must match the stored path. All other http:// URIs are rejected at registration.

**`/oauth/authorize/approve` calls `CustomerOtpVerifyCommand` directly** rather than duplicating verify logic. The MediatR response (access token + refresh token) is discarded — the OAuth flow issues its own auth code instead. The OTP verify side-effects (customer find-or-create, login_history, VerifiedAt stamp) all happen via the existing handler.

**scope claim not injected into the base customer JWT.** `IJwtTokenService.CreateCustomerAccessToken` and `CustomerTokenClaims` are shared-contract types (other services pin them). The scope is carried only in the OAuthTokenResponse JSON and stored in oauth_authorization_codes. The MCP resource server uses `token_use=customer` (already in the JWT) for authorization, not scope.

**WWW-Authenticate challenge header on 401.** The MCP service's JwtBearer `OnChallenge` event calls `context.HandleResponse()` to suppress the default header, then writes `WWW-Authenticate: Bearer resource_metadata="<McpBaseUrl>/.well-known/oauth-protected-resource"`. This is the RFC 9728 / MCP spec discovery handshake.

**RLS bypass for /oauth/* paths** added to the `IsScopeResolvingAuthPath` predicate in Identity Program.cs — these endpoints resolve brand from config with no authenticated token, so the same bypass used for customer auth paths applies.

**No System.Web dependency.** `Uri.EscapeDataString` + manual separator logic is used instead of `HttpUtility.ParseQueryString`.

## New tables (db/patches/oauth_authorization_server.sql)
- `identity_access.oauth_clients` — RFC 7591 public client registry (client_id UNIQUE, redirect_uris TEXT[])
- `identity_access.oauth_authorization_codes` — single-use PKCE codes (code_hash UNIQUE, consumed_at NULL = unused)

Migration pattern: idempotent SQL patch in db/patches/ using `CREATE TABLE IF NOT EXISTS` + `CREATE INDEX IF NOT EXISTS`. EF entities + configurations added to SharedDataModel; DbSets added to LaundryGharDbContext (DB-first, no migrations).

## Endpoints added

Identity (port 5050):
- GET  `/.well-known/oauth-authorization-server` — RFC 8414 metadata
- POST `/oauth/register` — RFC 7591 dynamic client registration
- GET  `/oauth/authorize` — serves self-contained HTML consent/OTP page
- POST `/oauth/authorize/otp/send` — OTP send (delegates to CustomerOtpSendCommand)
- POST `/oauth/authorize/approve` — OTP verify → issues auth code
- POST `/oauth/token` — authorization_code + refresh_token grant

MCP (port 5009):
- GET `/.well-known/oauth-protected-resource` — RFC 9728 metadata
- 401 responses include `WWW-Authenticate: Bearer resource_metadata="..."` header

**How to apply:** Run db/patches/oauth_authorization_server.sql against the canonical PostgreSQL DB before starting Identity. No EF migration needed.
