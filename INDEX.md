# Laundry Ghar — Documentation Index

> **Read this first.** This is the entry point for every human and AI agent working on Laundry Ghar. It tells you which file is authoritative for what, and in which order to read.

**Project:** Laundry Ghar — multi-tenant franchise SaaS for the India laundry & dry-clean market
**Stack:** .NET 10 Clean Architecture · React 19 (admin + POS) · React Native / Expo (customer + rider) · PostgreSQL 16+
**Tenancy:** Single database, Row-Level Security, multi-level franchise hierarchy
**Status:** Built and running — 9 backend services + Worker (Aspire) and 4 clients (admin-web, pos-web, customer-mobile, rider-mobile) are live end-to-end. Deployed schema (verified against the live DB 2026-06-10): **109 tables / 7 materialized views**, including the subscriptions module (10 tables + 2 MRR views, deployed 2026-06-10 via `db/patches/subscriptions_module.sql`). See `HANDOFF.md` for current state and `docs/GAP_ANALYSIS.md` (2026-06-10) for the remediation backlog and its outcome.

> ✅ **Doc drift corrected 2026-06-10:** the file map and directory tree below now point at the real locations (planning docs in `bodies/`, deployment in `protocol/`, agent manuals in `.claude/agents/`, schema in `database_scripts/` + `db/patches/`), and ADRs 001–010 now exist as files in `docs/ADRs/`.

---

## Authority order (who wins on conflict)

When two documents disagree, resolve in this order — **higher wins**:

1. **Deployed canonical schema** = `database_scripts/*.sql` + `db/patches/*.sql`, applied in order (see `database_scripts/apply_all.sh` and `db/patches/apply_patches.sh`). If a `.md` describes a column the SQL doesn't have, the SQL is right. `docs/SCHEMA_FULL.sql` and `docs/08_subscriptions_customer.sql` / `docs/09_subscriptions_franchise.sql` are the **original spec documents** — useful for intent and commentary, but the deployed scripts win on any difference.
2. **`docs/ADRs/`** — locked architectural decisions (ADR-001…010). Don't relitigate these in code review.
3. **`PRODUCTION_SPEC.md`** — product + system behaviour.
4. **`bodies/BUILD_PLAN.md`** — sequencing and scope.
5. Everything else — supporting detail.

> AI agents: if you are about to write SQL inside a `.md` file, **stop**. SQL lives only in `database_scripts/` and `db/patches/`. Markdown may _quote_ SQL for illustration but never _defines_ it.

---

## File map

| File                              | Role                                          | Who reads it                       | When                        |
| --------------------------------- | --------------------------------------------- | ---------------------------------- | --------------------------- |
| `INDEX.md`                        | Router (this file)                            | Everyone                           | First                       |
| `HANDOFF.md`                      | Current state, gotchas, how to run            | Everyone resuming work             | Every session               |
| `PRODUCTION_SPEC.md`              | Full product + system spec                    | All engineers, PM                  | Before building any feature |
| `bodies/BUILD_PLAN.md`            | Wave sequencing, scope, timeline              | Orchestrator, leads                | Sprint planning             |
| `bodies/AGENT_TEAM.md`            | The Claude Code agent team, wave-mapped       | Orchestrator                       | Before dispatching a wave   |
| `bodies/CONTEXT_MANAGEMENT.md`    | How agents share memory & stay in budget      | Orchestrator, all agents           | When context fills up       |
| `protocol/DEPLOYMENT.md`          | Cloud, CI/CD, secrets, environments           | DevOps, foundation agent           | Wave 0 + Wave 4             |
| `docs/ADRs/ADR-0XX-*.md`          | One locked decision each (001–010)            | Anyone questioning a design choice | On review                   |
| `.claude/agents/*.md`             | Per-agent operating manual (8 agents)         | The agent itself + orchestrator    | At agent spawn              |
| `database_scripts/README.md`      | Schema overview + apply order                 | Every backend agent                | Before writing any entity   |
| `database_scripts/0N_*.sql`       | Canonical DDL, one file per bounded context   | Backend agents, DBA                | Migration authoring         |
| `db/patches/*.sql`                | Post-baseline patches (RLS, FKs, features, seeds) | Backend agents, DBA            | Migration authoring         |
| `docs/SCHEMA_FULL.sql`            | Original full-schema spec document            | Architects                         | Historical reference        |
| `docs/GAP_ANALYSIS.md`            | 2026-06-10 gap register + remediation status  | Everyone                           | Backlog grooming            |

---

## Directory tree

```
laundryghar/
├── INDEX.md                        ← you are here
├── HANDOFF.md                      Current state, how to run, gotchas
├── PRODUCTION_SPEC.md              Full product + system spec (no SQL bodies)
├── README.md
├── bodies/
│   ├── BUILD_PLAN.md               Wave-based build strategy + timeline
│   ├── AGENT_TEAM.md               Agent team, wave-mapped
│   └── CONTEXT_MANAGEMENT.md       Agent memory & context-window strategy
├── protocol/
│   └── DEPLOYMENT.md               Cloud, CI/CD, secrets, environments
├── .claude/agents/                 Per-agent operating manuals (8)
│   ├── laundryghar-orchestrator.md
│   ├── database-architect.md
│   ├── dotnet-backend-developer.md
│   ├── senior-react-architect.md
│   ├── expo-mobile-developer.md
│   ├── uiux-design-architect.md
│   ├── qa-test-engineer.md
│   └── security-code-reviewer.md
├── docs/
│   ├── INDEX.md                    Docs-local copy of this router
│   ├── GAP_ANALYSIS.md             Gap register + remediation status (2026-06-10)
│   ├── SCHEMA_FULL.sql             Original spec schema (deployed scripts win)
│   ├── 08_subscriptions_customer.sql / 09_subscriptions_franchise.sql   Subscriptions spec
│   ├── ADRs/                       ADR-001 … ADR-010 (all ten)
│   └── (wireframes, BRD, mockups)
├── database_scripts/               Canonical baseline DDL — apply_all.sh order:
│   ├── 00_kernel.sql               kernel: settings, flags, files, outbox
│   ├── 01_bc1_tenancy_org.sql      tenancy_org
│   ├── 02_bc2_identity_access.sql  identity_access
│   ├── 03_bc3_customer_catalog.sql customer_catalog
│   ├── 04_bc4_order_lifecycle.sql  order_lifecycle
│   ├── 05_bc5_logistics.sql        logistics
│   ├── 06_bc6_commerce.sql         commerce
│   ├── 07_bc7_finance_royalty.sql  finance_royalty
│   ├── 08_bc8_engagement_cms.sql   engagement_cms
│   ├── 09_bc9_analytics.sql        analytics (materialized views)
│   └── 99_cross_cutting*.sql       FKs, partman, triggers
├── db/patches/                     Post-baseline patches: RLS enablement, FK
│                                   patches, feature DDL (rider ops, invoices,
│                                   subscriptions_module.sql), seeds
├── backend/laundryghar/            9 services + Worker + Aspire AppHost
├── admin-web/  pos-web/            React 19 clients
└── customer-mobile/  rider-mobile/ Expo clients
```

---

## The communities (and why they map to parallel agents)

The tables cluster into **communities** that share only the tenant anchor (`brand_id`, `franchise_id`, `store_id`) and never each other's internal tables. Because they don't touch each other's data, agents can build them in parallel without merge conflicts. The deployed bounded contexts are the ten schemas listed in the tree above (kernel → analytics); the original wave plan and agent mapping live in `bodies/BUILD_PLAN.md` and `bodies/AGENT_TEAM.md`.

---

## Quick start (local dev)

```bash
# 1. Create database + extensions, apply baseline schema in order
cd database_scripts && ./apply_all.sh        # or: psql -f each 0N_*.sql in order

# 2. Apply post-baseline patches
cd ../db/patches && ./apply_patches.sh

# 3. Verify (parent tables, partition children excluded)
psql -U postgres -d laundry_ghar_db -tAc "SELECT count(*) FROM pg_class c \
  JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.relkind IN ('r','p') \
  AND NOT c.relispartition AND n.nspname NOT IN \
  ('pg_catalog','information_schema','partman','public');"
# expect: 109
```

> Note: macOS dev boxes have both postgresql@16 and @18 clients installed — use the
> `postgresql@18` client binaries (see HANDOFF.md gotchas).

---

## Status counts (verified against the live DB 2026-06-10)

```
109 tables · 7 materialized views · 5 partitioned tables (pg_partman, monthly)
  = 92 baseline (database_scripts/) + 7 patch-added (db/patches/: invoices,
    number sequences, rider_settlements, push_tokens, modules, event cursors)
  + 10 subscriptions-module tables (db/patches/subscriptions_module.sql)
10 ADRs (docs/ADRs/) · 8 agent manuals (.claude/agents/)
0 PG enum types (lookup tables + CHECK constraints instead — ADR-005)
0 SQL definitions living in markdown (all in database_scripts/ + db/patches/)
```
