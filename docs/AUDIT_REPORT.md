# Access Control Architecture — audit report

**Source:** published artifact `https://claude.ai/code/artifact/0bbd9d51-6e6e-480f-a1a2-e9b35efaa99a`
(snapshot 2026-09-05, read against the working tree). Saved into the repo on 2026-09-05 so the
finding list has a tracked home; the artifact remains the authored original.

**Remediation tracker:** `docs/FIX_TASKS.md` · **Outcome:** `docs/FIX_REPORT.md`

---

## Scope

How the platform decides who someone is, where they sit in the tenancy tree, what they are allowed
to touch, and what they are shown for it — traced from login token through RBAC, a built-but-dormant
ABAC layer, and PostgreSQL row-level security.

Four planes, in the order every request answers them:

| Plane | Question | Status |
|---|---|---|
| A · Actors | Who can hold a session at all — 10 staff user types, plus customers and RaaS partners in separate identity spaces | 3 actor spaces |
| B · Tenancy scope | Where they sit — Platform → Brand → Franchise → Store/Warehouse via scope memberships that inherit downward | 6 scope types |
| C · Authorization | What they can do — coarse role×permission grant, optionally narrowed by attribute conditions | RBAC enforcing · ABAC built but inert |
| D · Navigation | What they are shown for it — database-driven menu gated by vertical, then licensing, then permission | data-driven |

Headline counts at snapshot: 10 staff user types · 17 system + 4 custom roles · 165 permission codes
across 47 modules · 6 tenancy scope levels · 4 live client apps · 4 verticals (1 live).

---

## Findings — the work list

Concrete inconsistencies and gaps surfaced while tracing the system.

### A-1 · Guard asymmetry

The matrix "cells" endpoint doesn't replicate the single-permission endpoint's system-role,
brand-isolation, or rank-escalation checks — both sit behind the same `permissions.assign` policy,
so in principle the cells endpoint is the softer of two doors into the same room.

### A-2 · Two owners, two shapes

Self-service brand signup types its owner as `staff` holding a `brand_admin` role; the
franchise-invite flow types an owner as `franchise_owner` holding a `franchise_owner` role. Same
real-world concept, two different (type, role) pairs, reconciled only by a comment noting a
dedicated Owner preset hasn't shipped yet.

### A-3 · Naming drift

`UserType` spells the role `franchise_owner`; `NotificationRecipientType` spells the same real
person `franchisee`.

### A-4 · Blind writes

The person-detail drawer can grant or revoke additional memberships and permission overrides, but no
endpoint lists a person's *existing* ones — both panels are add/clear only, blind to current state
(called out directly in the panels' own code).

### A-5 · Catalogue drift

165 permission codes exist live; the seeded C# catalogue still lists 164 — `dispatch.mode.manage`
shipped as a raw SQL patch that was never folded back in, following the same pattern several earlier
codes went through before eventually being reconciled.

### A-6 · RLS doesn't reach franchise/store

Session variables for franchise and warehouse scope are published on every connection but read by
zero hand-written RLS policies today — that boundary is enforced only in application code (roughly
95 call sites), not the database.

### A-7 · Stale comment

rider-mobile's auth module header still asserts riders log in with a password "not OTP"; the shipped
login screen is 100% OTP, and the password function it describes has no reachable UI path.

---

## Worth keeping, not a gap

Two deliberate fail-closed decisions stand out: the three-state "unresolved vs. resolved-empty"
session-variable sentinel that stops a not-yet-set GUC from silently matching nothing, and a fix that
made an *absent* scope claim deny instead of allow — described in its own code comment as closing
"the single broadest fail-open in the authority model."

---

## Prior audit — findings F-1…F-7, all closed

From the 2026-08-31 review (`docs/ABAC_AUDIT_2026-08-31.md`). Recorded here for continuity; not part
of this work list.

| # | Severity | Issue | Resolution |
|---|---|---|---|
| F-1 | Critical | Any brand admin could list every user on the platform | Scoped to caller's brand |
| F-2 | Critical | Full PII (PAN/Aadhaar/bank) leaked cross-tenant by user id | Same fix, same scope guard |
| F-3 | Medium | Users table had no brand column or RLS at all | Membership-derived RLS added |
| F-4 | Medium | Missing brand header returned a session-killing 401 | Changed to 400 |
| F-5 | Low | Plan-gating 402 response ordering | Reviewed, no change needed |
| F-6 | Low | Policies endpoint 500'd on an unmigrated database | Narrow catch, empty result instead |
| F-7 | Low | List UI printed "0 policys" | Pluralization fixed |

---

## ABAC status at snapshot — built, inert

Not merely "shadow mode"; the per-endpoint cutover has not started at all. Verified three
independent ways:

- **Config:** zero `Abac` keys in any host's `appsettings*.json` — running on code defaults,
  `Enabled=false`, `Mode=shadow`.
- **Service:** `AbacAuthorizationService.EvaluateAsync` returns `Skipped` on its very first line,
  before touching the DB, the PDP, or any attribute resolver.
- **Adoption:** a repo-wide search for `[AbacResource]` / `.RequireAbac(…)` returns zero matches —
  no endpoint has opted in, so no request today writes a `decision_log` row.

All 7 ABAC migrations (`0023`–`0029`) are applied. None of the F-1…F-7 fixes credited ABAC as the
control; they were ordinary RBAC/tenancy fixes.
