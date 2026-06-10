---
name: round6-verification
description: Round 6 QA verification results — #39 integration settings and #38-remainder displayLabel/minimumQuantity validator
metadata:
  type: project
---

Round 6 verification completed 2026-06-11.

Task #39 (Integration Settings): PASS-WITH-DEFECT
Task #38 remainder (DisplayLabel + MinimumQuantity validators): PASS

**Why:** New restart loaded settings endpoints and catalog validators into live stack.

**How to apply:** #38 can be marked complete. #39 is conditionally complete with one open defect (DEF-39-A) that is dev-env only.

## Key findings

### DEF-39-A — Cross-service cipher key mismatch causes Worker notification failure (MAJOR, dev-env)
- All microservices auto-generate separate AES-256-GCM keys in Development (keys/dev-pii-key.b64 per bin dir)
- Identity encrypts settings secrets with its key; Worker reads with its own different key
- `WhatsAppSettings.FromJson` and `SmsSettings.FromJson` only catch `JsonException`, not `CryptographicException`
- When Worker calls `NotificationSettingsCache.GetAsync`, cipher.Decrypt() throws `CryptographicException`
- This bubbles up to `NotificationDispatcherService` send-catch, stored as `last_error` in outbox row
- Status remains `pending` (retrying), not gracefully falling back to LoggingChannelSender
- Evidence: outbox row b38cf27e had last_error = "The computed authentication tag did not match the input authentication tag."
- Root cause: per-service dev key isolation + missing `catch (CryptographicException)` in FromJson methods
- Production impact: None (production uses a shared Pii__EncryptionKey env var across all services)
- Fix: Add `catch (CryptographicException)` → return default (disabled) settings, or share a single dev key file

## Verified behaviors (PASS)

- PUT /settings/payment-gateway with enabled:false → 200, enc:v1 prefix in DB, masked tail ••••-123/••••-456
- GET /settings → no cleartext secrets, keySecretTail/webhookSecretTail masked, keySecret field absent
- Keep-on-blank: second PUT with empty keySecret → keyId updates, secret preserved (confirmed via masked tail)
- WhatsApp PUT enabled:false: enc:v1 in DB, accessTokenTail=••••-abc, keep-on-blank confirmed
- SMS PUT enabled:false: enc:v1 in DB, authKeyTail=••••-xyz, keep-on-blank confirmed
- DevPaymentGateway: Commerce in Development always uses DevPaymentGateway (short-circuited in Program.cs, SettingsFirstPaymentGateway never reached)
- Auth gate: GET/PUT /settings without token → 401; code uses RequireAuthorization() + Forbidden() check (IsPlatformAdmin || brand_admin)
- Admin UI: PaymentsPanel, WhatsAppPanel, SmsPanel all present in SettingsPage; PaymentsPanel has masked placeholder (keySecretTail ?? '••••') + webhook URL copy field
- DisplayLabel missing → 422 "Display label is required."
- DisplayLabel empty string → 422 "Display label is required."
- MinimumQuantity=0 → 422 "'Request Minimum Quantity' must be greater than or equal to '1'."
- Valid item (displayLabel present, minimumQuantity=1) → 201 Created

## Cleanup state
- payment/gateway: enabled=false, keyId=cleared-by-qa, secrets encrypted as 'cleared-by-qa'
- whatsapp/cloud: enabled=false, phoneNumberId=cleared-by-qa, accessToken encrypted as 'cleared-by-qa'
- sms/provider: enabled=false, senderId=CLEAR, authKey encrypted as 'cleared-by-qa'
- Test notification outbox row b38cf27e: status=cancelled, suppression_reason=qa-test-cleanup
- Test price list QA-TEST-PL: soft-deleted (deleted_at set)
- Test price list item efd5af0b: hard-deleted from DB

## Stack state at close
- 9 services healthy (ports 5050, 5001-5008), Worker alive, smoke 22/22 PASS
