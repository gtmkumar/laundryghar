---
name: project-push-notifications
description: Push notification implementation — Task #7; token registration endpoints in Catalog + Logistics, expo-notifications 0.29.x in both mobile apps
metadata:
  type: project
---

Task #7 shipped Expo push token registration for both mobile apps.

**Backend endpoints**
- Customer tokens: `POST /api/v1/customer/push-token` + `DELETE` in `laundryghar.Catalog` (CustomerOnly policy). Commands in `Application/Customer/Self/Commands/PushTokenCommands.cs`.
- Rider tokens: `POST /api/v1/rider/push-token` + `DELETE` in `laundryghar.Logistics` (RiderOnly policy). Commands in `Application/RiderSelf/RiderPushTokenCommands.cs`.
- Both upsert on `engagement_cms.push_tokens.token` (unique). user_type = "customer" | "rider". FluentValidation: token ≤ 4096, platform ∈ {ios, android}.

**Mobile apps**
- `expo-notifications ~0.29.14` installed in both apps.
- `src/lib/pushNotifications.ts` in each app: permission request, getExpoPushTokenAsync, backend registration, foreground handler, Android channel, response listener.
- `src/api/pushNotifications.ts` in each app: thin axios wrappers to the matching service.
- Feature flag: `FEATURES.pushNotifications = true` in `src/constants/config.ts` (both).
- app.config.ts: `expo-notifications` plugin added with `icon + color + androidMode:default` (both).
- `(app)/_layout.tsx`: calls `initialisePushNotifications()` in useEffect when `isHydrated && accessToken`.
- `authStore.ts`: calls `deregisterPushNotifications()` at top of `logout()`, before clearing SecureStore.

**Notification data payload contract**
```json
{ "type": "order" | "pickup", "id": "<entityId>", "outboxId": "...", "templateCode": "..." }
```
Customer app deep-links: order → `/(app)/orders/[id]`, pickup → `/(app)/orders/tracking/[id]`.
Rider app: type "task" | "assignment" → `/(app)/tasks/[id]`.
Worker currently sends only `{ outboxId, templateCode }` — type+id fields are a seam for the Worker team to fill.

**Why:** Push notifications for customer order status + rider task assignment notifications.

**How to apply:** When modifying auth flow or logout in either app, ensure deregisterPushNotifications() is still called before token clearing. When editing Worker sender, add type+id to outbox data per the payload contract above.
