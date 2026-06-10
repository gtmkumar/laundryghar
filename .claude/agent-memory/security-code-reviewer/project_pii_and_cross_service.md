---
name: project-pii-and-cross-service
description: PII-at-rest exposure (PAN/bank plaintext), cross-brand FK validation gaps (CreateOrder customer IDOR), and consistent platform-admin X-Brand-Id gating across services.
metadata:
  type: project
---

**PII at rest — plaintext PAN/bank (High).** Rider entity (Entities/Logistics/Rider.cs) and UserProfile store PanNumber, BankAccountNumber, BankIfsc, DrivingLicenseNumber in PLAINTEXT columns. Aadhaar is at least masked (AadhaarNumberMasked). No column-level encryption. RiderDto (Logistics/.../RiderDtos.cs) response record correctly EXCLUDES these (only create/update request DTOs carry them) — good. BUT Identity GetUserByIdHandler (Users/Commands/UserCommands.cs:102-108) returns full PanNumber/BankAccountNumber/BankIfsc/UpiId in UserDto to anyone with permission:users.read — broad plaintext exposure. AuditLog entity has OldValues/NewValues JSON cols; if user-update audit writes capture these fields, PII lands in audit logs too (verify the audit writer).

**Cross-brand FK validation gaps (Medium IDOR family).** Pattern: handlers RequireBrandId() + set row.BrandId server-side, but do NOT validate that foreign IDs in the request belong to the actor's brand. Confirmed instances:
- CreateRiderHandler: userId not checked for brand membership (documented in [[project-logistics-service]]).
- Orders CreateOrderCommand: validates StoreId & AddonId belong to brand (lines 34, 93) but CustomerId (line 159) is inserted with NO brand-ownership check. Operator can bind a foreign-brand customer to an order in their own brand.
Row lands in attacker's brand so no direct cross-brand READ leak, but binds foreign identity + may surface that party's data via enrichment. Audit other admin create/update handlers (Commerce, Warehouse, Finance) for the same shape.

**X-Brand-Id trust model — CONSISTENT and correct.** Every service's TenantResolutionMiddleware gates the X-Brand-Id override to userType==PlatformAdmin; non-platform-admins' header is ignored and brand_id comes from JWT claims. bypass_rls Items flag is only set for PlatformAdmin (middleware) or for unauthenticated scope-resolving auth paths (Identity Program.cs:251-259, Engagement). No spoofing path for a regular operator.

**No security headers** (UseHsts/UseHttpsRedirection/CSP/X-Frame-Options) in any service Program.cs — assume gateway TLS; add defense-in-depth headers before prod.
