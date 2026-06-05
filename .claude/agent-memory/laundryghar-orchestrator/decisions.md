# Orchestrator Decisions — Laundry Ghar

## Architecture (locked 2026-06-05)
- **Microservices**, not a monolith. Shared libraries:
  - `laundryghar.Utilities` — shared API/utility lib (ApiResponse, Result, Exceptions, EmailService, EncryptionHelper). REUSE for responses. NOTE: its `ICurrentUserService` uses `int` IDs (legacy template) — do NOT reuse; our keys are UUIDs.
  - `laundryghar.SharedDataModel` — shared **database library** across microservices: EF Core entities, enums, events, configurations, `LaundryGharDbContext`, RLS interceptor. All services reference it.
- First service built: **laundryghar.Identity** (Identity & Access + Tenancy & Org). Clean Architecture internally.

## Database (canonical = live `laundry_ghar_db`)
- 10 bounded-context schemas (kernel, tenancy_org, identity_access, customer_catalog, order_lifecycle, logistics, commerce, finance_royalty, engagement_cms, analytics). 92 logical tables + 66 partition children + 5 MVs. PG 16.14, all extensions incl pg_partman 5.4.3.
- **Schema mapping decision:** connection string says `Search Path=auth` but NO `auth` schema exists — it is a placeholder. EF maps to the real schemas (identity_access/tenancy_org/kernel). No DB renames.
- **DB gap policy:** apply pending patches (db/patches/) SCOPED per bounded context as each is integrated, showing each before running. tenancy_org has 0 missing FKs; identity_access has 8 (→ tenancy_org + customer_catalog, both exist). `triggers_set_updated_at.sql` is global+idempotent. `rls_proposal.sql` applied scoped.
- Apply order is FK-dependency, not lexical: 01→02→00→03→…→99 (see db/HANDOFF.md).

## Auth model (locked)
- JWT (access) + rotating hashed refresh tokens + OTP (phone) + password (Argon2id) + optional MFA. RBAC = data (roles × permissions × scope_memberships), seed system roles+permissions.

## Agent mapping (docs' named specialists don't exist as spawnable agents)
- Backend → `dotnet-backend-developer`; DB → `database-architect`; security gate → `security-code-reviewer`; QA → `qa-test-engineer`; frontend (later) → `senior-react-architect` / `expo-mobile-developer` / `uiux-design-architect`.

## Constraints
- No git writes (no commits/push). Stage + describe; Goutam commits.
- Schema in DB is canonical; never redefine tables in markdown. Introspect live DB for exact column types.
