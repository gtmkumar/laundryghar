# Laundry Ghar — Agent Team

> Who does what, when. This is the **dispatch manual** for the Claude Code multi-agent build. The orchestrator reads this before starting each wave; each specialist reads its own `agents/*.md` at spawn.

**Hard rule for every agent:** agents NEVER run `git commit`, `git push`, or any git write. Goutam handles all git operations manually. Agents may stage files and describe what to commit, nothing more.

---

## The team (11 agents)

| Agent                         | File                             | Owns                                                           | Wave |
| ----------------------------- | -------------------------------- | -------------------------------------------------------------- | ---- |
| Orchestrator                  | `laundryghar-orchestrator.md`    | Dispatch, integration, conflict resolution                     | All  |
| Foundation Architect          | `foundation-architect.md`        | Solution skeleton, tenancy, identity, customers, audit, outbox | 0    |
| Catalog Builder               | `catalog-builder.md`             | Service catalog, items, pricing, add-ons                       | 1    |
| Orders Engineer               | `orders-engineer.md`             | Orders, pickups, slots, garments, inspections                  | 1    |
| Warehouse Ops Engineer        | `warehouse-ops-engineer.md`      | Batches, processes, QC, stock reconciliation                   | 1    |
| Delivery & Rider Engineer     | `delivery-rider-engineer.md`     | Riders, assignments, GPS, capacity                             | 1    |
| Commerce Engineer             | `commerce-engineer.md`           | Packages, loyalty, coupons, payments, wallet                   | 1    |
| Finance & Royalty Engineer    | `finance-royalty-engineer.md`    | Cash books, expenses, royalty, notifications, CMS              | 1    |
| Database Architect            | `database-architect.md`          | Migrations, RLS, partitions, indexes (cross-cutting)           | 0–2  |
| Security & Compliance Auditor | `security-compliance-auditor.md` | DPDP, encryption registry, RBAC, audit review                  | 0–4  |
| Frontend Engineer             | `frontend-engineer.md`           | React admin/POS, React Native customer/rider                   | 3    |

---

## Wave plan

The build runs in waves. Within a wave, listed agents run **in parallel**; waves run in **sequence**. Total ≈ 12–13 weeks.

### Wave 0 — Foundation (1 week, solo)

**Agent:** `foundation-architect` (+ `database-architect`, `security-compliance-auditor` advising)

Everything the six communities depend on. Nothing else can start until this lands.

- .NET 10 solution skeleton (Domain/Application/Infrastructure/Api/Worker/Gateway/AppHost)
- Tenancy middleware + `DbConnectionInterceptor` setting RLS session vars
- Identity service (users, roles, permissions, RBAC, OTP, refresh tokens)
- Customers + addresses + devices + DPDP consent + deletion pipeline
- Audit infra (`audit_logs` partitioned) + transactional outbox (`outbox_events`)
- Notification fabric skeleton (`notifications_outbox` + dispatcher)
- CI/CD pipeline, migrations runner, seed data
- **SQL files:** `01_tenancy_identity.sql`

**Exit gate:** RLS leakage test passes (tenant A cannot read tenant B), migrations run clean, CI green.

### Wave 1 — Six communities in parallel (4–5 weeks)

Six agents, one per community. They share only the foundation; no shared internal tables, so no merge conflicts on domain code.

| Agent                      | SQL file                                   | Key deliverables                                                                                    |
| -------------------------- | ------------------------------------------ | --------------------------------------------------------------------------------------------------- |
| `catalog-builder`          | `02_customers_catalog.sql`                 | Category/service/item/variant/pricing CRUD, versioned price lists, add-ons                          |
| `orders-engineer`          | `03_orders_garments.sql`                   | Order lifecycle state machine, pickup requests, slot booking, garment tagging, inspections + photos |
| `warehouse-ops-engineer`   | `04_warehouse_riders.sql` (warehouse half) | Batch processing, process scans, QC pass/fail/rewash, stock reconciliation                          |
| `delivery-rider-engineer`  | `04_warehouse_riders.sql` (rider half)     | Rider duty/shift, assignment dispatch, GPS ingest, capacity rules, delivery OTP                     |
| `commerce-engineer`        | `05_commerce_payments.sql`                 | Packages + wallet ledgers, loyalty earn/burn, coupons, Razorpay payments + refunds                  |
| `finance-royalty-engineer` | `06_finance_cms.sql`                       | Cash books + handover, expenses, royalty calc + invoicing, notification templates, WhatsApp log     |

**Exit gate:** each community's API contract tests pass; no cross-community FK violations; outbox events emitted on state changes.

### Wave 2 — Integration (2 weeks)

**Agent:** orchestrator as integrator (+ `database-architect`)

Cross-community features that don't belong to one owner.

- Analytics materialized views (`07_system_views.sql`) + Hangfire refresh jobs
- CMS wiring (onboarding slides, banners, mobile remote config) to all apps
- System settings + feature flags + polymorphic file registry
- End-to-end order→garment→warehouse→delivery→payment→royalty flow test
- **SQL files:** `07_system_views.sql`

**Exit gate:** a full order traverses all six communities and shows up correctly in every materialized view.

### Wave 3 — Clients (3 weeks)

**Agent:** `frontend-engineer` (may spawn three sub-instances)

- Admin web (React 19): franchise/store/catalog/pricing/orders/finance dashboards
- POS (React 19): walk-in order entry, garment tagging, cash book, shift handover
- Customer mobile (React Native/Expo): booking, tracking, packages, wallet, payments
- Rider mobile (React Native/Expo): assignments, GPS, pickup/delivery OTP, proof photos

**Exit gate:** each app passes Playwright/E2E smoke; bilingual (en-IN/hi-IN) verified.

### Wave 4 — Hardening (2 weeks)

**Agent:** `security-compliance-auditor` lead (+ all)

- Load testing (k6) to perf targets (API p95 < 500ms, mobile cold start < 2s)
- Security audit + pen test; DPDP compliance review; encryption registry complete
- DR drill (PITR restore), backup verification
- Observability wired (Serilog→Elastic, OpenTelemetry→Prometheus/Jaeger, Sentry)

**Exit gate:** perf targets met, pen-test criticals == 0, DR restore demonstrated.

---

## Dispatch protocol (orchestrator)

1. Read `INDEX.md`, this file, and `database/README.md`.
2. Confirm previous wave's **exit gate** passed. If not, do not advance.
3. For each agent in the wave, spawn with its `agents/*.md` + only the SQL file(s) it owns + the foundation interfaces it consumes. **Do not** hand an agent the whole 92-table schema — see `CONTEXT_MANAGEMENT.md`.
4. Collect outputs. Run cross-community integration checks.
5. Log any architectural decision that emerged as a new ADR.
6. Never commit. Summarize staged changes for Goutam to commit.

---

## Why six parallel agents is safe (not just fast)

The communities share **only** the tenant anchor and the foundation interfaces. They never read or write each other's domain tables. An order references a `customer_id` and a `store_id` (foundation) and a `service_id`/`item_id` (catalog, read-only contract) — but the orders agent never touches warehouse or finance tables directly; those react via outbox events. That decoupling is what makes parallel construction conflict-free. If two agents ever need the same table, that table belongs in the foundation, not in a community.
