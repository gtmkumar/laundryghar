# ADR-005 — Lookup tables + CHECK constraints over PostgreSQL enums

**Status:** Accepted (retro-documented 2026-06-10) · **Deciders:** Architecture, DBA

## Context

The domain is full of closed-but-evolving value sets: order statuses, garment process stages, consent methods, shift labels, payment states. Native `CREATE TYPE ... AS ENUM` makes adding a value a DDL migration, makes removing/renaming one nearly impossible, and serializes awkwardly across nine .NET services.

## Decision

**Zero PostgreSQL enum types in the deployed schema** (verifiable: `grep -c "CREATE TYPE.*ENUM" database_scripts/*.sql` → 0). Closed value sets are `VARCHAR` columns guarded by `CHECK (col IN (...))` constraints — e.g. `cash_books.shift_label`, `dpdp_consents.consent_status` / `consent_method` — and richer, tenant-visible vocabularies (service categories, expense categories, roles/permissions, navigator modules) are real **lookup/seed tables** with their own metadata, populated by `db/patches/seed_*.sql`. The .NET side mirrors the string values with constants/smart-enums; no DB type binding.

**Where it lives:** CHECK constraints throughout `database_scripts/0*.sql`; lookup-table seeds in `db/patches/seed_access_control.sql`, `seed_navigator_modules.sql`, `seed_settings_defaults.sql`, etc.

## Consequences

- **+** Adding a status value is a data/constraint change, not a type migration; renames are `UPDATE`s.
- **+** Lookup tables carry display names, ordering, and tenant scoping that enums never could.
- **−** Slightly larger storage than enum oids; typo protection depends on the CHECK being present (reviewed in DDL).
- **−** Value sets are duplicated in .NET constants — drift is caught by integration tests, not the type system.
