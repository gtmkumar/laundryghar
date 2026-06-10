# ADR-001 — Row-Level Security over schema-per-tenant

**Status:** Accepted (retro-documented 2026-06-10) · **Deciders:** Architecture

## Context

Laundry Ghar is multi-tenant by **brand**, with a franchise/store hierarchy under each brand, in a single PostgreSQL database. The alternatives were schema-per-tenant (operationally heavy: N× migrations, N× connection pools, cross-tenant analytics pain) or app-layer-only filtering (one missed `WHERE brand_id` = data leak). Tenant counts are expected in the tens-to-hundreds, not millions, and the platform team needs cross-brand analytics.

## Decision

One shared schema set, with PostgreSQL **Row-Level Security** as the isolation boundary. Every tenant-scoped table carries a `brand_id` column; RLS policies compare it to the session variable `app.current_brand_id`, which each service sets per-request from the JWT. Services connect as the non-superuser `app_user` role (RLS is actually enforced — no `BYPASSRLS`). Trusted system paths (Worker jobs, platform admin) escape via `kernel.rls_bypass()`, which reads the `app.bypass_rls` session flag.

**Where it lives:** policies in `db/patches/rls_enable_*.sql` + `db/patches/fix_legacy_*_rls_policies.sql`; the bypass function and `app_user` hardening in `db/patches/harden_app_user_and_rls_bypass.sql`; session-variable convention documented in `database_scripts/00_kernel.sql`.

## Consequences

- **+** Isolation is enforced at the database, not by handler discipline; a forgotten filter returns zero rows instead of leaking.
- **+** Single schema → one migration path, one connection pool, cross-brand analytics queries are trivial under bypass.
- **−** Every connection must set `app.current_brand_id` correctly; middleware in each service is load-bearing.
- **−** RLS predicates add planner overhead on hot tables; mitigated with `brand_id`-leading indexes.
- **−** Bypass is powerful: `kernel.rls_bypass()` usage is restricted to Worker/platform-admin code paths and reviewed.
