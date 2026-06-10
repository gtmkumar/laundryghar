# ADR-004 — pg_partman range partitioning for hot tables

**Status:** Accepted (retro-documented 2026-06-10) · **Deciders:** Architecture, DBA

## Context

A handful of tables grow unboundedly and are queried almost exclusively by recent time window: audit trails, order rows, garment process logs, rider GPS pings, notification send logs. Without partitioning, indexes bloat, vacuums get slow, and retention deletes become table-locking events.

## Decision

**Time-range partition the five hot tables, managed by pg_partman** (monthly partitions, auto-maintained):

| Table | Partition key |
|---|---|
| `identity_access.audit_logs` | `occurred_at` |
| `order_lifecycle.orders` | `created_at` |
| `order_lifecycle.process_logs` | `occurred_at` |
| `logistics.rider_location_pings` | `pinged_at` |
| `engagement_cms.notifications_log` | `sent_at` |

`PARTITION BY RANGE` is declared in the table DDL; `partman.create_parent()` calls (wrapped in fault-tolerant DO blocks) live in `database_scripts/99_cross_cutting_schema_qualified.sql`. The `partman` schema is deployed live with its config tables. Everything else stays unpartitioned — partitioning is opt-in for proven hot paths only.

## Consequences

- **+** Retention = detach/drop a partition, not a million-row DELETE; recent-window queries prune to 1–2 partitions.
- **+** partman automates future-partition creation; no cron-authored DDL.
- **−** Partitioned PKs must include the partition key; cross-partition unique constraints are limited (UUIDs make collisions a non-issue in practice).
- **−** Raw `information_schema` table counts include partition children — count parent tables (`pg_class.relispartition = false`) when auditing schema size.
