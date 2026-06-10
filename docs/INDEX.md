# Laundry Ghar — Documentation Index

> **Read this first.** This is the entry point for every human and AI agent working on Laundry Ghar. It tells you which file is authoritative for what, and in which order to read.

**Project:** Laundry Ghar — multi-tenant franchise SaaS for the India laundry & dry-clean market
**Stack:** .NET 10 Clean Architecture · React 19 (admin + POS) · React Native / Expo (customer + rider) · PostgreSQL 16+
**Tenancy:** Single database, Row-Level Security, multi-level franchise hierarchy
**Status:** Built and running — 9 backend services + Worker and 4 clients are live end-to-end (see root `HANDOFF.md`). The 102-table / 7-MV count includes the 10 subscription tables + 2 MRR views from `08/09_subscriptions_*.sql`, which are **spec-only and not yet deployed**; the deployed canonical schema is `database_scripts/` + `db/patches/` (92 tables, 5 MVs). Gap register: `docs/GAP_ANALYSIS.md` (2026-06-10).

---

## Authority order (who wins on conflict)

When two documents disagree, resolve in this order — **higher wins**:

1. **`database/*.sql`** — the canonical schema. If a `.md` describes a column that the SQL doesn't have, the SQL is right.
2. **`ADRs/`** — locked architectural decisions. Don't relitigate these in code review.
3. **`PRODUCTION_SPEC.md`** — product + system behaviour.
4. **`BUILD_PLAN.md`** — sequencing and scope.
5. Everything else — supporting detail.

> AI agents: if you are about to write SQL inside a `.md` file, **stop**. SQL lives only in `database/*.sql`. Markdown may *quote* SQL for illustration but never *defines* it.

---

## File map

| File | Role | Who reads it | When |
|---|---|---|---|
| `INDEX.md` | Router (this file) | Everyone | First |
| `PRODUCTION_SPEC.md` | Full product + system spec | All engineers, PM | Before building any feature |
| `BUILD_PLAN.md` | Wave sequencing, scope, timeline | Orchestrator, leads | Sprint planning |
| `AGENT_TEAM.md` | The 11 Claude Code agents, wave-mapped | Orchestrator | Before dispatching a wave |
| `CONTEXT_MANAGEMENT.md` | How agents share memory & stay in budget | Orchestrator, all agents | When context fills up |
| `DEPLOYMENT.md` | Cloud, CI/CD, secrets, environments | DevOps, foundation agent | Wave 0 + Wave 4 |
| `ADRs/ADR-0XX-*.md` | One locked decision each | Anyone questioning a design choice | On review |
| `agents/*.md` | Per-agent operating manual | The agent itself + orchestrator | At agent spawn |
| `database/README.md` | Schema overview, all 92 tables | Every backend agent | Before writing any entity |
| `database/0N_*.sql` | Canonical DDL, 7 community files | Backend agents, DBA | Migration authoring |

---

## Directory tree

```
laundryghar/
├── INDEX.md                        ← you are here
├── PRODUCTION_SPEC.md              Full product + system spec (no SQL bodies)
├── BUILD_PLAN.md                   Wave-based build strategy + timeline
├── AGENT_TEAM.md                   11 agents, wave-mapped
├── CONTEXT_MANAGEMENT.md           Agent memory & context-window strategy
├── DEPLOYMENT.md                   Cloud, CI/CD, secrets, environments
├── ADRs/
│   ├── ADR-001-rls-over-schema-per-tenant.md
│   ├── ADR-002-uuid-v7-over-bigint-identity.md
│   ├── ADR-003-jsonb-for-flexible-fields.md
│   ├── ADR-004-pg-partman-for-hot-tables.md
│   ├── ADR-005-lookup-tables-over-pg-enums.md
│   ├── ADR-006-numeric-14-2-for-money.md
│   ├── ADR-007-transactional-outbox-for-events.md
│   ├── ADR-008-dpdp-purpose-bound-consent.md
│   ├── ADR-009-cash-book-per-shift.md
│   └── ADR-010-recurring-billing-and-dunning.md
├── agents/
│   ├── laundryghar-orchestrator.md
│   ├── foundation-architect.md
│   ├── catalog-builder.md
│   ├── orders-engineer.md
│   ├── warehouse-ops-engineer.md
│   ├── delivery-rider-engineer.md
│   ├── commerce-engineer.md
│   ├── finance-royalty-engineer.md
│   ├── database-architect.md
│   ├── security-compliance-auditor.md
│   └── frontend-engineer.md
└── database/
    ├── README.md                   Schema overview, all 92 tables A→Z
    ├── 01_tenancy_identity.sql     21 tables — Wave 0 foundation
    ├── 02_customers_catalog.sql    14 tables
    ├── 03_orders_garments.sql      14 tables
    ├── 04_warehouse_riders.sql     10 tables
    ├── 05_commerce_payments.sql    13 tables
    ├── 06_finance_cms.sql          16 tables
    └── 07_system_views.sql          4 tables + 5 materialized views
```

---

## The six communities (and why they map to parallel agents)

The 92 tables cluster into six **communities** that share only the tenant anchor (`brand_id`, `franchise_id`, `store_id`) and never each other's internal tables. Because they don't touch each other's data, six agents can build them in parallel without merge conflicts.

| Community | Tables | Agent | Wave |
|---|---|---|---|
| Foundation (tenancy, identity, customers) | 26 | `foundation-architect` | 0 (solo) |
| Catalog & Pricing | 9 | `catalog-builder` | 1 |
| Orders & Garments | 14 | `orders-engineer` | 1 |
| Warehouse & QC | 11 | `warehouse-ops-engineer` | 1 |
| Delivery & Riders | 8 | `delivery-rider-engineer` | 1 |
| Commerce (packages/loyalty/payments) | 13 | `commerce-engineer` | 1 |
| Customer subscriptions (module A) | 6 | `commerce-engineer` | 1 |
| Franchise SaaS plans (module B) | 4 | `finance-royalty-engineer` | 1 |
| Finance & Royalty + CMS | 16 | `finance-royalty-engineer` | 1 |
| Analytics + System | 9 | (orchestrator integrator) | 2 |

> The build order is **Wave 0 solo** (foundation everyone depends on) → **Wave 1 six agents in parallel** (one per community) → **Wave 2 integrator** (cross-community: analytics, CMS wiring, mobile config). See `BUILD_PLAN.md` and `AGENT_TEAM.md`.

---

## Quick start (local dev)

```bash
# 1. Create database + extensions
createdb -U postgres laundryghar_dev

# 2. Run schema in order
for f in database/0*.sql; do
  psql -U postgres -d laundryghar_dev -f "$f"
done

# 3. Verify
psql -U postgres -d laundryghar_dev -c \
  "SELECT count(*) AS tables FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE';"
# expect: 92
```

---

## Status counts (keep this honest)

```
102 tables · 7 materialized views · 5 partitioned tables · ~290 indexes
9 SQL files · 10 ADRs · 11 agents
0 PG enum types (lookup tables instead)
0 SQL definitions living in markdown (all in database/*.sql)
```
