---
name: project-worker-service
description: laundryghar.Worker background service context — outbox drain design, RLS bypass strategy, enum values, IChannelSender/IEventPublisher abstraction choices
metadata:
  type: project
---

The Worker (Microsoft.NET.Sdk.Worker, net10.0) drains two transactional outboxes (ADR-007):
- `engagement_cms.notifications_outbox` via `NotificationDispatcherService`
- `kernel.outbox_events` via `OutboxEventRelayService`

**RLS / superuser strategy:** The worker has no HTTP/tenant context. It registers a
`WorkerCurrentTenant : ICurrentTenant` that returns all nulls and `BypassRls = true`.
Connecting as `postgres` (superuser) bypasses PG RLS natively; the interceptor additionally
emits `SET app.bypass_rls = 'true'` as defence-in-depth. The ICurrentTenant is Scoped (not
Singleton) to match RlsConnectionInterceptor's Scoped lifetime — each poll cycle creates an
`IServiceScopeFactory.CreateAsyncScope()`.

**Why:** The outbox tables are cross-brand; a single-brand RLS context would filter out most rows.

**Verified enum values:**
- notifications_outbox.status CHECK: pending, queued, sending, sent, failed, expired, suppressed, cancelled
- notifications_outbox.channel CHECK: sms, whatsapp, email, push, in_app, voice (same for notification_templates)
- notifications_outbox.recipient_type CHECK: customer, user, rider, franchisee, manual
- kernel.outbox_events.status CHECK: pending, publishing, published, failed, dead_letter

**Npgsql execution strategy:** Same pattern as Orders — `db.Database.CreateExecutionStrategy().ExecuteAsync(...)` wrapping `BeginTransactionAsync`. Direct `BeginTransactionAsync` throws under `EnableRetryOnFailure`.

**Two-transaction per-row design:** First tx marks the row 'sending'/'publishing' (optimistic
lock against concurrent workers). Dispatch happens outside any DB lock. Second tx persists
outcome (sent/failed/backoff). This avoids holding DB locks during I/O.

**Backoff formula:** `Math.Min(Math.Pow(2, attempts), 1440)` minutes (capped at 24 h).

**Aspire registration:** Worker has no HTTP endpoint — `AddProject<..>("worker")` with only
`WithEnvironment(...)` calls, no `WithHttpEndpoint`. The AppHost still needs the project
reference in the csproj to generate `Projects.laundryghar_Worker`.

**notifications_log composite PK:** `(Id, SentAt)` — required by PG range partitioning on
`sent_at`. EF Core insert must set both fields; `ValueGeneratedOnAdd` on `Id` only.

**How to apply:** When adding real channel providers, implement `IChannelSender` and register
it as Scoped in Program.cs. For message broker, implement `IEventPublisher` (wrap
MassTransit `IPublishEndpoint`). The two background services have no knowledge of providers.
