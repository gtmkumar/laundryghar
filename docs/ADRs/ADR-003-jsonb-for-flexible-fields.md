# ADR-003 — JSONB for flexible / sparse fields

**Status:** Accepted (retro-documented 2026-06-10) · **Deciders:** Architecture

## Context

Several domains carry data whose shape varies per tenant or per record — settings values, feature-flag payloads, notification template variables, garment inspection details, gateway responses — and modelling each variant as columns would mean constant migrations or a forest of mostly-NULL columns. An EAV pattern was rejected as unqueryable.

## Decision

Use **JSONB columns for genuinely flexible payloads only**; everything with a stable shape stays relational. Examples in the deployed schema: `kernel.system_settings.setting_value` and `kernel.feature_flags` payloads, `kernel.outbox_events.payload`, gateway request/response blobs on `commerce.payments`, template variables in `engagement_cms`, and per-record metadata blobs across `order_lifecycle` (~80 JSONB columns total across `database_scripts/0*.sql`). The rule of thumb: if a field is filtered/joined on, it gets a real column (optionally a GIN index over the JSONB path); if it is read-and-render or write-and-audit, JSONB is fine. SQL never lives in markdown, and business-critical money/state fields never live in JSONB.

**Where it lives:** grep `JSONB` in `database_scripts/` — concentrated in `00_kernel.sql`, `04_bc4_order_lifecycle.sql`, `06_bc6_commerce.sql`, `08_bc8_engagement_cms.sql`.

## Consequences

- **+** Tenant-variable payloads evolve without migrations; gateway/webhook blobs are stored verbatim for audit.
- **+** Postgres JSONB operators + GIN indexes keep the escape hatch queryable when needed.
- **−** No type safety inside the blob; .NET sides own (de)serialization contracts.
- **−** Discipline required — reviewers reject new JSONB columns that hold filterable business state.
