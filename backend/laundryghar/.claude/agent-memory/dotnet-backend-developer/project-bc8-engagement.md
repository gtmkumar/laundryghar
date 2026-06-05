---
name: project-bc8-engagement
description: BC-8 Engagement/CMS microservice decisions — anonymous brand resolution, enum constraints, RLS bypass for public endpoints
metadata:
  type: project
---

BC-8 laundryghar.Engagement is on port 5007, added to laundryghar.slnx.

**Anonymous brand resolution (public endpoints):** Public endpoints (/api/v1/public/*) are called before the user logs in (no JWT). RLS is driven by SET LOCAL app.current_brand_id which is only set when an authenticated token is present — anonymous requests bypass RLS entirely and see all brands. Resolution: BrandResolver reads X-Brand-Id header (UUID, no DB lookup) first, then ?brandCode= query param (DB lookup), then defaults to "LG-MAIN". All anonymous LINQ queries must include explicit `.Where(x => x.BrandId == resolvedBrandId)` — this is enforced in the public query variants (GetPublicOnboardingSlidesQuery, GetPublicBannersQuery, GetPublicAppConfigQuery). Never rely on RLS for public endpoints.

**Why:** Anonymous public surface is required for onboarding slides, banners, and app-config which mobile apps fetch before login.

**Outbox processing approach:** Chose a manual "retry" admin endpoint over a hosted BackgroundService. The BackgroundService would introduce complexity (locking, error loop) for a dev stub. The retry endpoint resets a failed outbox row to pending status. The DevNotificationSender marks dispatched entries as "sent" immediately (dev stub — no real provider).

**Verified enum values** (prior BCs crashed on bad enums — always reference these):
- onboarding_slides.app_type: customer, rider, staff, pos
- onboarding_slides.status: active, inactive, archived
- app_banners.placement: home_top, home_middle, home_bottom, services_top, cart_top, order_success, profile
- app_banners.status: active, inactive, archived
- mobile_app_config.platform: android, ios, web
- mobile_app_config.status: active, inactive, archived
- notification_templates.channel: sms, whatsapp, email, push, in_app, voice
- notification_templates.status: active, inactive, archived
- notifications_outbox.recipient_type: customer, user, rider, franchisee, manual
- notifications_outbox.status: pending, queued, sending, sent, failed, expired, suppressed, cancelled
- notifications_log.status: sent, delivered, read, clicked, failed, bounced, blocked
- whatsapp_message_log.direction: inbound, outbound
- whatsapp_message_log.status: sent, delivered, read, failed, received

**PaginatedList is factory-only:** PaginatedList<T> constructor is private. Always use `PaginatedList<T>.CreateAsync(IQueryable<T>, page, pageSize, ct)`.

**BC-8 CMS permissions added to IdentitySeeder:**
- cms.template.manage, cms.banner.manage, cms.onboarding.manage, cms.appconfig.manage, cms.notification.read
- brand_admin → all 5; store_admin → cms.banner.manage; auditor → cms.notification.read

**How to apply:** When adding new public endpoints to Engagement or any other service, always implement a separate IBrandResolver or equivalent that doesn't require an auth token, and add explicit brandId predicates to all queries.
