# ADR-008 — DPDP purpose-bound consent and erasure

**Status:** Accepted (retro-documented 2026-06-10) · **Deciders:** Architecture, Compliance

## Context

India's DPDP Act 2023 requires consent that is purpose-specific, withdrawable, and auditable, plus a right-to-erasure flow; Google Play additionally requires in-app account deletion. A single `marketing_opt_in` boolean satisfies none of that, and erasure cannot simply `DELETE` a customer who has financial/order history that must be retained.

## Decision

Model consent and erasure as first-class schema, not flags:

- **`customer_catalog.dpdp_consents`** (`database_scripts/03_bc3_customer_catalog.sql`) is a purpose-bound consent **log**: one row per (customer, purpose) state change with `consent_status` (`granted`/`denied`/`withdrawn`/`expired`), `consent_method` (explicit checkbox, phone OTP, signed form, imported…), and a `consent_text_snapshot` of the exact wording shown. Current consent = latest row; history is never updated in place.
- **`customer_catalog.account_deletion_requests`** tracks DPDP/Play-Store deletion requests through a status pipeline. The Worker executes erasure asynchronously: `CustomerErasureService` + `CustomerAnonymizer` + `RetentionSweepService` in `laundryghar.Worker/Services/` anonymize PII while preserving financially-required records; grants/indexes for the pipeline are in `db/patches/dpdp_erasure_pipeline.sql`.
- Notification sends honor per-channel opt-ins (`engagement_cms.notification_preferences`) rather than the consent log directly.

## Consequences

- **+** Audit-ready: for any send or processing activity, the granting consent row (with its exact text) is reproducible.
- **+** Erasure = anonymization with retention carve-outs, so ledgers and GST records stay intact.
- **−** Every new processing purpose needs a consent purpose value and capture UI — product work, not just schema.
- **−** Consent checks are application-side; nothing in the DB blocks a send that ignores the log (covered by review + the dispatcher honoring preferences).
