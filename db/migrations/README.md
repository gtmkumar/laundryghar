# db/migrations — versioned schema migrations with rollback

All **new** schema changes go here as an up/down pair, applied with
`db/tools/migrate.sh`. This replaces the old "apply `db/patches/*.sql` by
hand" workflow; `db/patches/` remains the historical record and part of the
fresh-environment bootstrap (`db/build_from_scratch.sh`).

## File convention

```
0001_add_rider_badges.up.sql     ← forward change
0001_add_rider_badges.down.sql   ← exact rollback of the pair (required)
```

Scaffold the next pair (numbering is automatic):

```bash
db/tools/migrate.sh new add_rider_badges
```

## Commands

```bash
db/tools/migrate.sh status     # applied vs pending (+ checksum-drift warnings)
db/tools/migrate.sh up         # apply all pending (or: up 1)
db/tools/migrate.sh down       # roll back the last applied (or: down 2)
db/tools/migrate.sh verify     # CI guard: fail on checksum drift / missing files
db/tools/migrate.sh baseline   # mark all on-disk migrations applied WITHOUT running
```

Connection comes from `DB_NAME` / `DB_HOST` / `DB_PORT` / `DB_USER` / `DB_PASS`
(same convention as `build_from_scratch.sh`). DDL needs a privileged role — not
the RLS-scoped `app_user`. On macOS use the postgresql@18 client:
`export PATH="/opt/homebrew/opt/postgresql@18/bin:$PATH"`.

## Guarantees

- Each migration runs in a **single transaction**, and its
  `public.schema_migrations` bookkeeping row commits atomically with it — a
  failed migration leaves zero trace (verified: mid-file error rolls back
  every prior statement in the file).
- `up` refuses to apply a migration that has no `.down.sql`.
- Checksums are recorded at apply time; editing an already-applied file is
  flagged by `status`/`verify`. Never edit an applied migration — write a new
  one.
- Statements that cannot run inside a transaction
  (`CREATE INDEX CONCURRENTLY`, `ALTER TYPE ... ADD VALUE`) opt out with
  `-- migrate: no-transaction` as the **first line** of the file.

## Rules for writing migrations

1. The `.down.sql` must exactly undo the `.up.sql` — test it locally
   (`up` → `down` → `up` should be clean).
2. One logical change per pair; keep data backfills in the same transaction
   as the DDL they depend on.
3. New tables in RLS-covered schemas need their RLS policy + grants in the
   same migration (see `db/patches/rls_proposal.sql` for the house pattern).
4. Adopting an existing environment: run `migrate.sh baseline` once so
   already-present schema isn't re-applied.
