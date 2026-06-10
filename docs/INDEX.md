# Laundry Ghar — Documentation Index (docs/)

> **Read this first.** This is the docs-local copy of the repo router (`../INDEX.md`). It tells you which file is authoritative for what, and in which order to read. Paths below are relative to the **repo root** unless they start with `./`.

**Project:** Laundry Ghar — multi-tenant franchise SaaS for the India laundry & dry-clean market
**Stack:** .NET 10 Clean Architecture · React 19 (admin + POS) · React Native / Expo (customer + rider) · PostgreSQL 16+
**Tenancy:** Single database, Row-Level Security, multi-level franchise hierarchy
**Status:** Built and running — 9 backend services + Worker and 4 clients are live end-to-end (see root `HANDOFF.md`). Deployed schema (verified against the live DB 2026-06-10): **109 tables / 7 materialized views**, including the subscriptions module from `./08_subscriptions_customer.sql` + `./09_subscriptions_franchise.sql` (10 tables + 2 MRR views), deployed 2026-06-10 via `db/patches/subscriptions_module.sql`. Gap register + outcome: `./GAP_ANALYSIS.md` (2026-06-10).

> ✅ **Doc drift corrected 2026-06-10:** paths below now point at real locations, and ADRs 001–010 exist as files in `./ADRs/`.

---

## Authority order (who wins on conflict)

When two documents disagree, resolve in this order — **higher wins**:

1. **Deployed canonical schema** = `database_scripts/*.sql` + `db/patches/*.sql`, applied in order (`database_scripts/apply_all.sh`, `db/patches/apply_patches.sh`). If a `.md` describes a column the SQL doesn't have, the SQL is right. `./SCHEMA_FULL.sql` and `./08_subscriptions_customer.sql` / `./09_subscriptions_franchise.sql` are the **original spec documents** — useful for intent and commentary; the deployed scripts win on any difference.
2. **`./ADRs/`** — locked architectural decisions (ADR-001…010). Don't relitigate these in code review.
3. **`PRODUCTION_SPEC.md`** — product + system behaviour.
4. **`bodies/BUILD_PLAN.md`** — sequencing and scope.
5. Everything else — supporting detail.

> AI agents: if you are about to write SQL inside a `.md` file, **stop**. SQL lives only in `database_scripts/` and `db/patches/`. Markdown may _quote_ SQL for illustration but never _defines_ it.

---

## File map

| File                              | Role                                          | Who reads it                       | When                        |
| --------------------------------- | --------------------------------------------- | ---------------------------------- | --------------------------- |
| `INDEX.md` (root) / this file     | Router                                        | Everyone                           | First                       |
| `HANDOFF.md`                      | Current state, gotchas, how to run            | Everyone resuming work             | Every session               |
| `PRODUCTION_SPEC.md`              | Full product + system spec                    | All engineers, PM                  | Before building any feature |
| `bodies/BUILD_PLAN.md`            | Wave sequencing, scope, timeline              | Orchestrator, leads                | Sprint planning             |
| `bodies/AGENT_TEAM.md`            | The Claude Code agent team, wave-mapped       | Orchestrator                       | Before dispatching a wave   |
| `bodies/CONTEXT_MANAGEMENT.md`    | How agents share memory & stay in budget      | Orchestrator, all agents           | When context fills up       |
| `protocol/DEPLOYMENT.md`          | Cloud, CI/CD, secrets, environments           | DevOps, foundation agent           | Wave 0 + Wave 4             |
| `./ADRs/ADR-0XX-*.md`             | One locked decision each (001–010)            | Anyone questioning a design choice | On review                   |
| `.claude/agents/*.md`             | Per-agent operating manual (8 agents)         | The agent itself + orchestrator    | At agent spawn              |
| `database_scripts/README.md`      | Schema overview + apply order                 | Every backend agent                | Before writing any entity   |
| `database_scripts/0N_*.sql`       | Canonical DDL, one file per bounded context   | Backend agents, DBA                | Migration authoring         |
| `db/patches/*.sql`                | Post-baseline patches (RLS, FKs, features, seeds) | Backend agents, DBA            | Migration authoring         |
| `./SCHEMA_FULL.sql`               | Original full-schema spec document            | Architects                         | Historical reference        |
| `./GAP_ANALYSIS.md`               | 2026-06-10 gap register + remediation status  | Everyone                           | Backlog grooming            |

---

## Directory tree

```
laundryghar/
├── INDEX.md                        Repo-root router
├── HANDOFF.md                      Current state, how to run, gotchas
├── PRODUCTION_SPEC.md              Full product + system spec (no SQL bodies)
├── bodies/
│   ├── BUILD_PLAN.md               Wave-based build strategy + timeline
│   ├── AGENT_TEAM.md               Agent team, wave-mapped
│   └── CONTEXT_MANAGEMENT.md       Agent memory & context-window strategy
├── protocol/
│   └── DEPLOYMENT.md               Cloud, CI/CD, secrets, environments
├── .claude/agents/                 Per-agent operating manuals (8)
├── docs/                           ← you are here
│   ├── INDEX.md                    This file
│   ├── GAP_ANALYSIS.md             Gap register + remediation status (2026-06-10)
│   ├── SCHEMA_FULL.sql             Original spec schema (deployed scripts win)
│   ├── 08_subscriptions_customer.sql   Subscriptions module A spec (customer)
│   ├── 09_subscriptions_franchise.sql  Subscriptions module B spec (franchise SaaS)
│   ├── ADRs/
│   │   ├── ADR-001-rls-over-schema-per-tenant.md
│   │   ├── ADR-002-uuid-v7-over-bigint-identity.md
│   │   ├── ADR-003-jsonb-for-flexible-fields.md
│   │   ├── ADR-004-pg-partman-for-hot-tables.md
│   │   ├── ADR-005-lookup-tables-over-pg-enums.md
│   │   ├── ADR-006-numeric-14-2-for-money.md
│   │   ├── ADR-007-transactional-outbox-for-events.md
│   │   ├── ADR-008-dpdp-purpose-bound-consent.md
│   │   ├── ADR-009-cash-book-per-shift.md
│   │   └── ADR-010-recurring-billing-and-dunning.md
│   └── (wireframes, BRD, mockups)
├── database_scripts/               Canonical baseline DDL (00_kernel … 09_bc9_analytics,
│                                   99_cross_cutting*; apply_all.sh)
├── db/patches/                     Post-baseline patches: RLS enablement, FK patches,
│                                   feature DDL (rider ops, invoices,
│                                   subscriptions_module.sql), seeds
├── backend/laundryghar/            9 services + Worker + Aspire AppHost
├── admin-web/  pos-web/            React 19 clients
└── customer-mobile/  rider-mobile/ Expo clients
```

---

## The communities (and why they map to parallel agents)

The tables cluster into **communities** that share only the tenant anchor (`brand_id`, `franchise_id`, `store_id`) and never each other's internal tables — so agents can build them in parallel without merge conflicts. The deployed bounded contexts are the ten schemas in `database_scripts/` (kernel, tenancy_org, identity_access, customer_catalog, order_lifecycle, logistics, commerce, finance_royalty, engagement_cms, analytics), plus the subscriptions module split across commerce (module A) and finance_royalty (module B) per ADR-010. The original wave plan and agent mapping live in `bodies/BUILD_PLAN.md` and `bodies/AGENT_TEAM.md`.

---

## Quick start (local dev)

```bash
# 1. Apply baseline schema in order
cd database_scripts && ./apply_all.sh

# 2. Apply post-baseline patches
cd ../db/patches && ./apply_patches.sh

# 3. Verify (parent tables, partition children excluded)
psql -U postgres -d laundry_ghar_db -tAc "SELECT count(*) FROM pg_class c \
  JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.relkind IN ('r','p') \
  AND NOT c.relispartition AND n.nspname NOT IN \
  ('pg_catalog','information_schema','partman','public');"
# expect: 109
```

---

## Status counts (verified against the live DB 2026-06-10)

```
109 tables · 7 materialized views · 5 partitioned tables (pg_partman, monthly)
  = 92 baseline (database_scripts/) + 7 patch-added (db/patches/)
  + 10 subscriptions-module tables (db/patches/subscriptions_module.sql)
10 ADRs (docs/ADRs/) · 8 agent manuals (.claude/agents/)
0 PG enum types (lookup tables + CHECK constraints instead — ADR-005)
0 SQL definitions living in markdown (all in database_scripts/ + db/patches/)
```
