# ADR-007 — Transactional outbox for domain events

**Status:** Accepted (retro-documented 2026-06-10) · **Deciders:** Architecture

## Context

Services need to emit domain events (order status changed, payment captured, pickup assigned) that drive notifications, analytics, and cross-service workflows. Publishing to a broker inside a request handler creates the classic dual-write problem: the DB commit succeeds but the publish fails (or vice versa), and the system silently diverges. We also did not want to operate a message broker for Wave 0.

## Decision

**Transactional outbox.** Every service writes domain events into the shared `kernel.outbox_events` table (`database_scripts/00_kernel.sql`) **in the same database transaction** as the state change — payload as JSONB, an `idempotency_key UNIQUE`, a `status` + `next_attempt_at` retry contract, and partial indexes on pending work (`idx_outbox_pending`, `idx_outbox_events_retry`). The background **Worker** relays them: `laundryghar.Worker/Services/OutboxEventRelayService.cs` polls pending rows, dispatches to consumers (notification mapping/dispatch, etc.), and marks them done or schedules a retry with backoff. There is no external broker; the database is the queue. If/when scale demands Kafka/RabbitMQ, the relay service is the single seam to swap.

## Consequences

- **+** Events are exactly-as-committed: no lost or phantom events; consumers get at-least-once delivery with idempotency keys to dedupe.
- **+** Zero broker ops; the event log is queryable SQL (great for debugging and replay).
- **−** Polling latency (seconds, not ms) — acceptable for notifications/analytics, not for sub-second choreography.
- **−** `outbox_events` is a hot append table; relies on the pending-status partial indexes and periodic cleanup of done rows.
