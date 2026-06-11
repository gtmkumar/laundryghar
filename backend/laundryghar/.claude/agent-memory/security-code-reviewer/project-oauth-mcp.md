---
name: project-oauth-mcp
description: Security notes for the hand-rolled OAuth 2.1 authz-server facade (Identity) + MCP server — PKCE, redirect-URI, DCR, idempotency, known gaps
metadata:
  type: project
---

## OAuth 2.1 facade on Identity (`laundryghar.Identity/Endpoints/OAuthEndpoints.cs`)

Added for MCP clients (Claude.ai/Code, Gemini CLI). Endpoints all `.AllowAnonymous()` + `RequireRateLimiting("auth")`. The "auth" policy = FixedWindow 10 req / 60s **per client IP** (`Identity/Program.cs` ~line 119).

**Sound:**
- PKCE S256 verify is correct + timing-safe (`PkceHelper.VerifyCodeVerifier` uses `CryptographicOperations.FixedTimeEquals`).
- Auth code: 256-bit CSPRNG, stored as SHA-256 hex (`HashCode`), single-use via atomic `UPDATE ... WHERE consumed_at IS NULL AND expires_at > now()` (no TOCTOU). Code bound to client_id + redirect_uri + code_challenge + customer_id + brand_id. 5-min TTL.
- Customer token audience (`laundryghar-services`) + `token_use=customer` — matches what MCP validates. Refresh grant reuses `CustomerRefreshHandler` (rotation + reuse-detection, customer-only).
- Inline authorize HTML embeds query params via `System.Text.Json.JsonSerializer.Serialize` into JS string literals. Default JavaScriptEncoder escapes `<`/`>`/`&` to `<` etc, so `</script>` breakout is NOT possible. **Fragile**: if anyone swaps in `UnsafeRelaxedJsonEscaping`, this becomes stored/reflected XSS.

**Key gaps (see review):**
- DCR `/oauth/register` is fully open + anonymous, only IP-rate-limited. No auth/throttle on client creation → DB-spam / unbounded-growth risk. No cleanup of expired auth codes (only an index exists).
- `RedirectUriMatches` for https = exact ordinal string equality (no normalization). Loopback = scheme+host+path, port-agnostic (RFC 8252). No open-redirect found — but GET `/authorize` does NOT pre-validate redirect_uri against registered set (only `/approve` does); page still renders with attacker redirect_uri, rejected at approve. Acceptable.
- No CSP / `frame-ancestors` on the HTML page; `X-Frame-Options: DENY` IS set by `ServiceDefaults.UseSecurityHeaders` but ONLY in non-Development.
- `code_verifier` length (RFC 7636: 43-128) not validated — not exploitable (S256 compare), spec-deviance only.
- Each `/approve` call runs `CustomerOtpVerifyCommand` which itself mints a refresh token + login_history row (side effect) on top of the OAuth code. Harmless but noisy.
- `state` is reflected back via `Uri.EscapeDataString` in `BuildRedirectUrl` — safe.

## MCP server (`laundryghar.Mcp/`)

- `/mcp` gated by `RequireAuthorization("CustomerOnly")`; `CustomerOnlyHandler` checks `token_use == "customer"` ordinal. Rider/user tokens rejected. Correct.
- `TokenForwardingHandler` forwards inbound `Authorization` header to downstream. **SSRF-safe**: BaseAddress is fixed per keyed HttpClient (Catalog/Orders from config); tool args only fill path/query, never host. No absolute-URL injection vector found.
- Tool outputs embed untrusted downstream strings (order numbers, address lines, status reasons, category names) into natural-language replies → residual **prompt-injection** surface (a malicious address label could carry instructions to the LLM). Inherent to MCP; note as residual risk, not a code bug.
- Write tools `book_pickup` (Idempotent) + `cancel_order` (Destructive) validate GUID/date/time/payment locally; rely on downstream Orders for authz (customer self-filter from JWT sub). Sound.

## Orders idempotency (`db/patches/pickup_idempotency_and_source.sql`, `PickupCommands.cs`)

- Partial unique index on `(customer_id, idempotency_key) WHERE idempotency_key IS NOT NULL` — cross-customer isolation correct; two customers may reuse the same key string.
- `CustomerSchedulePickupHandler` idempotency lookup filters by `CustomerId == cmd.CustomerId AND IdempotencyKey == key` → cannot return another customer's row. customerId always from JWT sub (`GetCustomerId`), never from body.
- `source` validated against allowlist {app,web,mcp,whatsapp,pos,call} in handler + FluentValidation + DB CHECK. Defaults to 'app'.
- **Idempotency lookup is not slot-capacity-safe under race**: two concurrent same-key requests can both miss the existing-row check, both attempt slot increment; the unique index then fails the 2nd SaveChanges with a DbUpdateException (500, not a clean 200 replay). Hardening, not a security hole.
