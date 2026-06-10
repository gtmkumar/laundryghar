---
name: project-pii-encryption
description: PII encryption + masking design: AES-256-GCM ValueConverter, users.read_financial permission gate, audit-log finding, IFSC decision
metadata:
  type: project
---

Financial PII (PAN, bank account, UPI ID) on `identity_access.user_profiles` and `logistics.riders` is now encrypted at rest (AES-256-GCM) and masked in API responses unless the caller holds `users.read_financial`.

**Why:** OWASP HIGH finding — PII stored plaintext and returned unmasked to any `users.read` holder.

**How to apply:** When touching user/rider profile queries or DTOs, ensure:
1. `PiiValueConverter.Instance` is applied in EF configs (`UserProfileConfiguration`, `RiderConfiguration`) for PAN, BankAccountNumber, UpiId columns. IFSC is intentionally NOT encrypted (public branch code).
2. All RiderDto/UserDto responses go through `RiderDtoFinancialMask.Apply(dto, actor)` or `UserDtoFinancialMask.Apply(dto, actor)` before the HTTP layer. Permission constant is `users.read_financial`.
3. `AddSharedDataModel(connStr, builder.Configuration, builder.Environment)` — all three args required in every service Program.cs. Dev auto-generates `keys/dev-pii-key.b64`; non-Dev fails closed if `Pii:EncryptionKey` is absent.
4. Legacy plaintext rows (pre-encryption) are passed through on read and re-encrypted on next write (no bulk backfill required immediately).
5. Audit logs (`identity_access.audit_logs`) have no application-layer writer yet — nothing to exclude today. Check when that changes.
6. Columns widened to `text` via patch `pii_encryption_column_widening.sql`. Max ciphertext length for typical PII is ~100 chars.
7. `db/patches/pii_read_financial_permission.sql` grants the permission to `platform_admin` + `brand_admin`.
8. Cipher tests live in `laundryghar.ServiceDefaults.Tests/Crypto/`. SharedDataModel exposes internals to that test project via InternalsVisibleTo.
