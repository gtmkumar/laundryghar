---
name: project-wave0-rls
description: Wave-0 RLS tenant-isolation and auth validation results for laundryghar Identity service
metadata:
  type: project
---

RLS isolation and auth flows validated for Wave-0 foundation (2026-06-05).

**Exit gate: PASSED** — RLS isolation proven at app_user level, core auth flows and reuse detection work.

**Key facts:**
- `app_user` role was created NOLOGIN by `rls_proposal.sql` (ran before `app_user_role.sql`). The idempotent CREATE ROLE in `app_user_role.sql` is skipped, leaving the role without LOGIN. Fix: add `ALTER ROLE app_user WITH LOGIN PASSWORD 'app_user';` to `app_user_role.sql`'s DO block (after the IF-EXISTS guard).
- 5 legacy `{public}` policies on tenancy_org tables (`franchises_tenant`, `territories_tenant`, `franagree_tenant`, `stores_tenant`, `warehouses_tenant`) perform a raw `::uuid` cast on `current_setting(...)` — throws `invalid input syntax for type uuid` when `app.current_brand_id` is set to empty string. The newer `rls_brand` policy uses `kernel.current_brand_id()` which is null-safe via `NULLIF(...,'')::uuid`.
- Service runs on port 5000 (Kestrel default). Must use `ASPNETCORE_ENVIRONMENT=Development` env var, not `--environment` CLI flag (it was ignored in initial test run).
- OTP codes do not have a `delivered_at` column (it was assumed but absent).

**Why:** Validates Wave-0 identity/tenancy foundation before further BCs are layered on top.
**How to apply:** Reference these defects in any RLS or auth work going forward. Prioritize fixing app_user_role.sql and legacy public policies before Phase 3 (AppRuntime connection string switch).
