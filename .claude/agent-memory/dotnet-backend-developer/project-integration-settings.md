---
name: project-integration-settings
description: Runtime integration settings (payment gateway, WhatsApp, SMS) — schema, resolution order, registration shape, and why IFieldCipher is now a DI singleton
metadata:
  type: project
---

Task #39: payment-gateway + WhatsApp + SMS settings wired backend-to-UI.

**Settings schema (kernel.system_settings JSON):**
- `payment` / `gateway`: `{provider, enabled, keyId, keySecret(enc:v1:…), webhookSecret(enc:v1:…), codEnabled}`
- `whatsapp` / `cloud`: `{enabled, phoneNumberId, accessToken(enc:v1:…)}`
- `sms` / `provider`: `{provider:'msg91', enabled, authKey(enc:v1:…), senderId, dltTemplateId}`

**Masking rule:** `>=4` chars → `••••` + last 4; shorter → `••••`. GET response includes `*Tail` + `*Set` bool; secret never returned.

**IFieldCipher in DI:** `AddSharedDataModel` now calls `services.AddSingleton<IFieldCipher>(_ => PiiValueConverter.GetCipher())` so any service can inject the cipher without coupling to the static converter.

**Why:** settings commands (UpdatePaymentGatewayHandler etc.) and the settings-first gateway cache need to encrypt/decrypt secrets without duplicating cipher construction.

**Commerce gateway registration change:** non-Development now registers `GatewaySettingsCache` (Singleton) + `SettingsFirstPaymentGateway` (Scoped) as `IPaymentGateway`, replacing the former `AddSingleton<RazorpayPaymentGateway>`. Resolution order: DB settings row (TTL 60s) → env config fallback → fail-closed throw. Development still gets `DevPaymentGateway` (AddSingleton).

**Existing PaymentGatewayEnvGatingTests are still green** — they mirror the old Program.cs startup logic directly and do not test the new scoped wrapper path. The new `SettingsFirstGatewayResolutionTests` cover the resolution order at the model level.

**Worker notification routing:** `RoutingChannelSender` now takes `NotificationSettingsCache?` (Singleton, registered in Program.cs). For whatsapp/sms channels it checks DB settings first (TTL 60s), builds a fresh sender with those options, then falls back to env-config senders, then LoggingChannelSender. Env-config null!-registration pattern preserved.

**How to apply:** when adding new secret-carrying settings, follow the same pattern: store encrypted in the JSON blob, expose `*Tail`/`*Set` on the view, keep/preserve semantics on PUT.
