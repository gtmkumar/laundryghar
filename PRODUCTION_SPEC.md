# Laundry Ghar — Production Specification

> Full product + system behaviour. **No SQL definitions live here** — the schema is canonical in `database/*.sql` (see authority order in `INDEX.md`). This document describes _what the system does and why_; the SQL describes _exactly how the data is shaped_; `BUILD_PLAN.md` describes _in what order we build it_.

**One-line:** A multi-tenant, white-labelable franchise SaaS that runs an India laundry/dry-clean business end-to-end — customer booking, doorstep pickup, warehouse processing with garment-level tracking, delivery, payments, and franchise royalty accounting.

---

## 1. Vision & scope

Laundry Ghar lets a brand operate a franchised laundry network from one platform. A franchisee runs stores and warehouses; customers book via mobile/WhatsApp/walk-in; riders pick up and deliver; warehouses process garments with full traceability; the platform bills royalties and gives everyone dashboards. The same codebase serves multiple brands (white-label) via runtime configuration — no per-brand forks.

**What's in scope (v1):** the full operational loop — catalog/pricing, ordering, pickup/delivery, garment tracking, warehouse QC, packages/loyalty/coupons, payments/wallet, cash books, royalty, notifications, CMS, analytics.

**What's deliberately deferred:** third-party marketplace integration, multi-currency (INR only for v1), franchise resale workflow. Tracked for a future appendix.

---

## 2. Organizational hierarchy

The defining structural feature, and the gap this spec closed vs the original BRD (which only had Super Admin → Store Admin → Staff).

```
Platform (Laundry Ghar HQ)
  └── Brand (white-label; supports N)
        └── Territory (geographic exclusivity)
              └── Franchise Owner
                    └── Franchise (operational business entity)
                          ├── Store (walk-in / pickup / express / hub / collection point)
                          └── Warehouse (processing; N:M to stores)
                                └── Staff · Riders · Customers
```

- A **brand** owns theming, locales, support channels, and catalog defaults.
- A **territory** grants geographic exclusivity (pincodes/polygon) to a franchisee.
- A **franchise** is the legal+operational entity that owns stores and warehouses and pays royalty.
- A **store** takes orders (walk-in or as a pickup origin); a **warehouse** processes; one warehouse serves many stores (`store_warehouse_mappings`).
- Roles are **scoped at every node** via database-driven RBAC (ADR — RBAC as data): `platform_admin`, `brand_admin`, `regional_manager`, `franchise_owner`, `store_admin`, `store_staff`, `warehouse_supervisor`, `warehouse_staff`, `rider`, `customer`, `auditor`.

Tenancy isolation is enforced in the database via RLS on `brand_id` (ADR-001).

---

## 3. The six communities (domain model)

The 92 tables form six loosely-coupled communities that share only the tenant anchor. This is both the domain model and the parallel-build boundary (`AGENT_TEAM.md`).

| Community                   | Responsibility                                         | Reacts to / emits                                                |
| --------------------------- | ------------------------------------------------------ | ---------------------------------------------------------------- |
| **Foundation**              | Tenancy, identity/RBAC, customers, DPDP, audit, outbox | The anchor everyone builds on                                    |
| **Catalog & Pricing**       | What can be cleaned and for how much                   | Read-only contract to Orders                                     |
| **Orders & Garments**       | The order lifecycle and per-garment journey            | Emits `order.*`, `garment.*`                                     |
| **Warehouse & QC**          | Batch processing, scans, quality, reconciliation       | Reacts to `garment.received`; emits `garment.qc_passed`/`rewash` |
| **Delivery & Riders**       | Pickup/delivery dispatch, GPS, capacity                | Reacts to `order.ready`; emits `delivery.completed`              |
| **Commerce**                | Packages, loyalty, coupons, payments, wallet           | Reacts to `order.placed`/`cancelled`; emits `payment.*`          |
| **Finance & Royalty + CMS** | Cash books, expenses, royalty, notifications, CMS      | Reacts to `payment.captured`; sends via outbox                   |

Communities never read each other's internal tables; they communicate through outbox domain events (ADR-007). That decoupling is what makes the build parallelizable and the system maintainable.

---

## 4. Core flows

### 4.1 Order lifecycle

`placed → pickup_scheduled → pickup_assigned → picked_up → received → sorting → in_process → qc → ready → delivery_scheduled → out_for_delivery → delivered → closed` (with `cancelled`, `returned`, `rewash`, `disputed` branches). Every transition is recorded in `order_status_history` with actor, reason, and whether the customer was notified. The orders table is partitioned monthly (ADR-004).

### 4.2 Garment journey

Each physical garment gets a printed tag (QR/barcode from a pre-printed pool). It is inspected at pickup (condition + annotated photos), tracked through warehouse stages via scans (`process_logs`, partitioned), quality-checked (pass/fail/rewash with pre/post photo comparison), and reconciled daily against expected location. A lost/damaged garment is flagged and surfaced in analytics.

### 4.3 Pickup & delivery

Customer requests a pickup in a time slot (capacity-enforced). A rider is dispatched; GPS pings stream in (daily-partitioned, 14-day retention per ADR-008). Pickup and delivery are gated by OTP and proof photo. Capacity rules cap a rider per slot.

### 4.4 Money

Order pricing resolves through a published, scoped price list (brand/franchise/store) plus add-ons, express surcharge, taxes (CGST/SGST/IGST), and discounts (coupon/loyalty/package). Payment via Razorpay (tokenized, PCI SAQ-A) or wallet or COD. All ledgers are append-only with idempotency keys (ADR-006). Refunds tracked explicitly.

### 4.5 Royalty

Monthly, the platform builds `royalty_calculations` from eligible revenue and issues a `royalty_invoice` to each franchise, reconciled against the cash-book source of truth (ADR-009).

---

## 5. Configuration model (white-label)

A brand configures the system at runtime, no redeploy:

- **Theming & locale:** colors, logo, enabled locales (en-IN, hi-IN), support channels — on `brands`.
- **Catalog & pricing:** brands seed defaults; franchises/stores can scope-override price lists.
- **CMS:** onboarding slides, home banners, and `mobile_app_config` drive the mobile apps remotely.
- **Feature flags:** gradual rollout / kill switches per brand/franchise/store (`feature_flags`).
- **Lookup-table enums:** fabric types, garment conditions, expense categories, payment methods are data, editable from admin (ADR-005).

---

## 6. Cross-cutting requirements

- **Bilingual (en-IN/hi-IN)** across all four clients; labels from `name_localized` + CMS.
- **DPDP Act 2023** (ADR-008): purpose-bound consent, erasure pipeline, India data residency.
- **RBAC as data**, scoped at every hierarchy node; no hardcoded permissions.
- **Reliability** (ADR-007): transactional outbox, idempotency keys, retry/backoff, circuit breakers.
- **Observability:** Serilog→Elastic, OpenTelemetry→Prometheus/Jaeger, Sentry.
- **Performance targets:** API p95 < 500ms, mobile cold start < 2s (see `DEPLOYMENT.md`).
- **Auditability:** every state-changing action in `audit_logs` (partitioned, 7-yr retention).

---

## 7. Tech stack

| Layer                     | Choice                                                                                                                       |
| ------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| Backend                   | .NET 10 Clean Architecture · MediatR/CQRS · EF Core 10 · FluentValidation · Serilog · Hangfire · MassTransit · YARP · Aspire |
| Admin web + POS           | React 19 · Vite · TS · TanStack Query · Zustand · Tailwind · shadcn/ui · RHF · Zod                                           |
| Mobile (customer + rider) | React Native · Expo SDK 52 · NativeWind · React Navigation                                                                   |
| Database                  | PostgreSQL 16+ · RLS · pg_partman · PostGIS · pgcrypto · citext · pg_trgm · btree_gin                                        |
| Cache / Queue / Storage   | Redis · RabbitMQ (MassTransit) · S3/Azure Blob                                                                               |

Decisions behind these are locked in `ADRs/`.

---

## 8. Where to go next

- **Build order & timeline** → `BUILD_PLAN.md`
- **Who builds what** → `AGENT_TEAM.md`
- **Exact schema** → `database/README.md` + `database/*.sql` (canonical)
- **Locked decisions** → `ADRs/`
- **Ops & deploy** → `DEPLOYMENT.md`

---

## 9. Authority notice

This spec describes behaviour and intent. On any conflict with the schema, **`database/*.sql` wins** — fix this document, not the SQL. AI agents: never transcribe SQL definitions into this file; quote for illustration only and point to the canonical `.sql`.
