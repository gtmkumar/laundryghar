---
name: project-oauth-mcp-settings
description: Security posture of OAuth 2.1 facade (Identity /oauth/*), MCP server, admin Settings secrets, OTP routing. Tracks which prior findings are now FIXED.
metadata:
  type: project
---

Reviewed 2026-06-11 (post OAuth/MCP/WhatsApp-SMS/payment-settings commits). Several prior CRITICAL/High findings are now REMEDIATED — do not re-flag them as open:

**FIXED since earlier reviews:**
- JWT now RS256 with fail-closed key mgmt (RsaJwtKeyProvider throws outside Dev if no key; never auto-generates in prod). JWKS at /.well-known/jwks.json. MCP pins ValidAlgorithms=RS256.
- DevPaymentGateway now env-gated (Commerce/Program.cs:54 IsDevelopment → Dev stub; else SettingsFirstPaymentGateway). Prior CRITICAL closed.
- Settings secrets (payment KeySecret/WebhookSecret, WhatsApp AccessToken, SMS AuthKey) encrypted via AES-256-GCM IFieldCipher (enc:v1: prefix). GET responses masked (••••last4 + hasValue). Decrypt failures fall back to disabled defaults.
- Master OTP (Otp:TestCode "123456") dual-gated: Identity refuses to START in Production if set (Program.cs:62), AND OtpSecurityHelper.IsTestCodeAccepted returns false when IsProduction(). Genuinely blocked in prod.
- UpdateUser no longer accepts Status/UserType (mass-assignment fixed). SetUserType has rank-priority guard + platform_admin guard.
- GrantMembership privilege escalation fixed: H2a (only platform_admin grants platform_admin), H2b (brand-scope match), H2c (cannot grant role of higher rank than own).
- Seeder hard-gated to Development (throws otherwise). Default admin pw from Seeder:AdminPassword config.
- Rate limiting present: "auth" 10/60s/IP, "oauth_register" 3/hour/IP, IP-partitioned (RealIp before limiter).
- Razorpay webhook: HMAC-SHA256 constant-time (FixedTimeEquals), fails closed in prod when WebhookSecret unset.
- Refresh tokens: rotation + reuse detection + family revocation (customer + system).
- Customer Orders/Invoice endpoints self-filter by sub claim (IDOR-protected). MCP forwards token; downstream validates.

**STILL OPEN / new findings:**
- OAuth scope claim NOT issued: CreateCustomerAccessTokenWithScope (OAuthEndpoints.cs:536) ignores scope, returns base customer token. mcp:booking token == full customer token. /mcp only checks token_use=customer, no scope gate → MCP-issued token has full customer API surface (read orders/invoices/PII), not just booking. Confidentiality scope-creep.
- OAuth refresh grant (HandleRefreshTokenGrantAsync) accepts ANY customer refresh token (incl. ones from mobile /otp/verify) and returns it tagged scope=mcp:booking. No client binding on refresh. Token confusion across surfaces.
- RoutingOtpSender.SendAsync loads WhatsApp/SMS settings with brandId:null though cmd.ResolvedBrandId IS known. SettingsStore.FindAsync prefers a brand-scoped row (OrderBy BrandId==null last) so in MULTI-BRAND deploys OTP may route via the wrong brand's WhatsApp/SMS creds. IOtpSender.SendAsync has no brand param. Single-brand today, latent cross-tenant config bleed.
- AssignPermissionHandler (RoleCommands.cs:52) has NO ceiling check — but gated by permission:permissions.assign which only platform_admin holds in seeder. Defense-in-depth only unless that perm is ever granted to brand_admin.
- Settings endpoints gated by UserType check (IsPlatformAdmin || UserType=="brand_admin") not a permission — franchise_owner correctly excluded; acceptable but coarse.
- No security headers (HSTS/CSP/X-Frame) on service Program.cs except the OAuth authorize HTML page (which DOES set strict CSP + X-Frame DENY). Assume gateway TLS.

**Why:** Avoid re-reporting fixed items; focus future passes on scope enforcement + multi-brand OTP routing.
**How to apply:** Reference before auditing OAuth/MCP/Settings/OTP. Verify against current code (these are 2026-06-11 snapshots).
