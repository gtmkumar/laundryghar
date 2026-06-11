---
name: project-remediation-2026-06
description: 2026-06-11 audit confirmed the 42/42 remediation initiative closed nearly all prior security findings. Records what is NOW fixed so future reviews don't re-flag.
metadata:
  type: project
---

As of 2026-06-11 (git: "remediation initiative complete 42/42"), a re-audit confirmed the following PRIOR findings are FIXED in code. Do NOT re-flag these without re-verifying regression:

- **DevPaymentGateway env-gated** — `laundryghar.Commerce/Program.cs:54` now wraps `AddSingleton<IPaymentGateway,DevPaymentGateway>()` in `if (builder.Environment.IsDevelopment())`; prod uses `SettingsFirstPaymentGateway` (line 76). Prior CRITICAL closed.
- **Refund aggregate cap** — `AdminPaymentHandlers.cs:98-105` now SUMs prior PaymentRefunds and rejects if `alreadyRefunded + req.Amount > payment.Amount`. Prior High closed.
- **VerifyPayment binding** — `CustomerPaymentHandlers.cs:133-150` now filters payment by CustomerId+BrandId AND asserts `payment.GatewayOrderId == req.GatewayOrderId`. Prior gap closed.
- **CreateOrder customer FK** — `CreateOrderCommand.cs:42-45` now `AnyAsync(c => c.Id==req.CustomerId && c.BrandId==brandId)` else throws. Prior IDOR closed.
- **CreateRider userId cross-brand** — `RiderCommands.cs:44-50` now verifies the user has a UserScopeMembership resolving to the caller's brand. Prior IDOR closed.
- **JWT RS256 + algo pinning** — Identity Program.cs:113 and ALL 7 downstream services pin `ValidAlgorithms=[RsaSha256]`, ValidateIssuer/Audience/Lifetime/SigningKey all true. Prior HS256 finding closed.
- **Rate limiting** — Identity has `auth` (10/60s/IP) + `oauth_register` (3/hr/IP) policies, attached via `.RequireRateLimiting("auth")` on AuthEndpoints, CustomerAuthEndpoints, OAuthEndpoints.
- **PII masking in UserDto** — `UserCommands.cs:37-39` applies `PiiMask.MaskPan/MaskBankAccount/MaskUpi` to the DTO. Prior plaintext-PAN-in-response closed.
- **CORS env-gated** — every service gates AllowAnyOrigin to `IsDevelopment()`; prod uses `WithOrigins(config)`. Identity dev uses SetIsOriginAllowed(_=>true)+AllowCredentials (dev only).
- **RLS interceptor** — `RlsConnectionInterceptor` re-sets all 5 app.* vars (incl bypass_rls) on every `ConnectionOpened`, so the pool-reuse staleness risk is mitigated.
- **X-Brand-Id trust** — TenantResolutionMiddleware gates override + bypass_rls to `user_type==PlatformAdmin`; `RequireBrandId()` reads override else claim else THROWS (fail-closed). Sound.

STILL OPEN (low-count) as of this audit:
- **Engagement outbox retry: write gated by READ perm** — `AdminNotificationLogEndpoints.cs:29` POST `/notification-outbox/{id}/retry` requires `permission:cms.notification.read` (seeded as a low-risk READ perm, IdentitySeeder.cs:209). No `cms.notification.manage` exists. Broken function-level access control.
- **Analytics /refresh global + error leak** — `AnalyticsEndpoints.cs:222` refreshes ALL brands' matviews; any brand admin w/ analytics.refresh triggers it (resource abuse, no data leak). Line 229 returns raw `ex.Message` to client.
- **Proof-photo trusts client ContentType** — `RiderProofPhotoCommands.cs:106` validates MIME via client header not magic-byte sniff (low; size-capped, stored to S3 not served as HTML).
- **No security headers** (HSTS/CSP/X-Frame-Options) in any Program.cs — assumes gateway TLS.
