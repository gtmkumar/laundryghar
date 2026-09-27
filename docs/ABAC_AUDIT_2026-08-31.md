# ABAC readiness audit — 2026-08-31

*Live test of the running system: UI, API and database. Nothing here is asserted from reading code;
every line is a result. Test tenants only — no customer or production data was accessed.*

**Method:** 987 endpoint calls (141 collection GETs × 7 subjects) + 61 targeted cases across
cross-tenant access, header forgery, ID manipulation, sub-brand scope, privilege escalation,
token-lane confusion and direct database sessions. 781 automated tests green.

---

## 1. Verdict

**This is RBAC with a hierarchical scope boundary, plus tenant RLS. It is not ABAC, and not yet a
hybrid — because the attribute half is not running.**

The authorization that *is* live held under every attack: cross-tenant reads and writes, forged
tenant headers, manipulated resource IDs, six privilege-escalation routes, token-lane confusion, and
a tampered signature. All denied.

The ABAC engine is built and unit-tested but **inert**: its six migrations are unapplied, so the
`authz` schema does not exist, there are zero policy rows, and `Abac:Enabled` is false. It is not
credited as a control anywhere in this report.

The two critical findings had nothing to do with ABAC. They are ordinary tenancy bugs in the user
directory — the one table with no `brand_id` column and no RLS, where application code was the only
line of defence and two of three queries had forgotten it.

## 2. Findings

| # | Severity | Finding | State |
|---|---|---|---|
| F-1 | **Critical** | `GET /admin/users` returned every user on the platform to any brand admin | **Fixed**, 6 tests |
| F-2 | **Critical** | `GET /admin/users/{id}` returned any user's full profile cross-tenant, incl. PAN / Aadhaar / bank / UPI | **Fixed**, tested |
| F-3 | Medium | `identity_access.users` has no `brand_id` and RLS off — no DB-layer boundary at all | **Fixed**, migration 0029 applied + 14 tests |
| F-4 | Medium | Missing `X-Brand-Id` reported as `401 Unauthorized` (60 endpoints) — should be 400 | **Fixed**, 8 tests |
| F-5 | Low | Plan gating evaluated before tenancy → `402` instead of `404` on a foreign id | **No change needed** — see below |
| F-6 | Low | `/admin/policies` returned `500` on an un-migrated database | **Fixed** |
| F-7 | Low | `FilterableTable` printed “0 policys” | **Fixed** |

### F-1 / F-2 — cross-tenant user disclosure (critical, fixed)

`GetUsersQueryHandler` did not inject `ICurrentUser` at all; `GetUserByIdQueryHandler` filtered only
on the id. Two different tenants' admins received byte-identical lists: 20 accounts across 12
unrelated brands. By id, the projection widens to PAN, masked Aadhaar, KYC status, bank account
name and number, IFSC and UPI — and the financial mask is no defence, because `brand_admin` holds
`users.read_financial` (verified on both tenants' live tokens).

`GetAccessPeople` had the correct rule all along, written inline. One rule, three queries, and the
two that mattered most had omitted it.

**Fix:** `core.Application/Identity/Users/Common/UserBrandScope.cs` — one predicate resolving a
user's tenant through their live memberships, composed in SQL so it survives pagination. Applied to
both handlers.

**Verified live after the fix:** cross-tenant by-id `200 → 404` both directions; list returned 7
(Brand A) / 1 (Brand B) / 20 (platform admin); own-tenant control still 200. Locked by
`tests/operations.IntegrationTests/Rbac/UserTenantIsolationTests.cs` (6 tests, real PostgreSQL).

### F-3 — the user table has no database-level tenant boundary (medium, fixed and applied)

`identity_access.users` has no `brand_id` column and `relrowsecurity = false`. A database session
scoped to Brand B still sees all 20 users. F-1 and F-2 are the symptom; this is the condition.

Every other sensitive table has two independent defences. This one has exactly one, in application
code.

**Fix — migration `0029_users_brand_rls`.** The second of the two options considered: a
`SECURITY DEFINER` membership→brand resolver used in the policy, rather than a denormalised
`primary_brand_id`. A user is not owned by a brand — a user holds memberships, and a membership
implies one. Denormalising would need a trigger on every membership write to stay true and would
still be wrong for anyone holding memberships under two brands. `identity_access.user_in_brand()`
is the exact SQL twin of `UserBrandScope.ScopedToCallerBrand`: same four scope types, same
liveness test.

Three decisions in it are worth naming, because each is a way the migration could have looked
right and been wrong in production:

- **INSERT is deliberately unrestricted.** A user's row is written *before* their first membership
  — the membership carries the foreign key, so no other order exists. A `WITH CHECK` requiring a
  resolvable brand would make account creation impossible, since the predicate can only become
  true after the row it guards. It costs nothing: F-3 is a disclosure finding, and a users row
  grants no authority until a membership points at it.
- **A user can always read their own row.** Without that arm, anyone whose memberships do not
  resolve to the brand their session carries vanishes from their own profile, password change and
  step-up — an account that looks deleted.
- **`user_scope_memberships` keeps RLS off.** It carries an `rls_user_self` policy which, if
  enabled, would restrict every session to its own membership rows — and the access-control screens
  read other people's by design. That needs its own audit; doing it as a side effect here is
  exactly how the inert `rls_admin_only` came to exist.

**Verified:** `tests/operations.IntegrationTests/Rbac/UsersBrandRlsTests.cs` — 14 tests applying
the migration file verbatim to a real PostgreSQL and driving it as a non-owner role. Covers all
four scope levels, revoked and expired memberships, the membership-less user, platform scope,
absent brand context, cross-tenant UPDATE and DELETE, and the two negative controls that a
block-everything policy would fail: an in-tenant UPDATE still works, and a user can still be
created.

**Applied 2026-08-31.** The table now has two independent defences for the first time. Keep
requiring `ScopedToCallerBrand` on new queries over `Users` anyway: RLS is the backstop, not the
rule — the application filter is what makes pagination correct, and a handler that omits it now
returns a *short page* rather than a leak, which is a quieter failure to notice.

### F-4 — missing brand context reported as 401 (medium, fixed)

60 of the platform admin's 141 calls returned `401` with `{"responseMessage":"Unauthorized."}`. The
token is valid; `RequireBrandId()` throws `UnauthorizedAccessException` because no `X-Brand-Id` was
sent. Supplying the header turns every one into `200`.

`401` is what clients use to decide a session is dead — a standard interceptor will clear the token
and bounce the operator to the login screen for a missing request parameter. The handler's own
message is discarded, so even a developer cannot tell.

**Fix:** `BrandContextRequiredException` mapped to **400** with a `brand_context_required` code,
matching the structured shape `step_up_required` and `feature_not_in_plan` already use.
`RequireBrandId()` throws it; `ExceptionHandler` classifies it before the
`UnauthorizedAccessException` arm it used to fall through to.

**Verified:** `tests/operations.Tests/Auth/BrandContextRequiredTests.cs` — 8 tests. Both halves
(the throw, and the middleware's 400 + code + surviving message), the claim and `X-Brand-Id`
override paths still resolving unchanged, and the over-correction guard: a genuine
`UnauthorizedAccessException` must still be 401, or the fix would have traded one wrong answer for
another.

### F-5 — plan gating precedence (low, no change needed)

Brand B reading Brand A's franchise gets `402 feature_not_in_plan` rather than `404`. Tested
explicitly for disclosure: a real foreign id and a non-existent id **both** return 402, and both
return 404 on a resource type the brand does own — so the status carries no signal about existence
and nothing leaks.

**Resolution: correct as it stands.** Reading the code closes the question the live test opened.
The 402 branch of `ApiAuthorizationResultHandler` reads exactly two things — the caller's own
`ent_off` claim and the permission code the endpoint declares — and never the route, the resource
id or its owner. It is therefore not a function of *whose* resource was asked for: a brand that has
not licensed a feature gets the same 402 for its own ids as for anyone else's. The ordering the
finding named is also not invertible in any useful direction, since authorization necessarily runs
before the handler that knows who owns a row, and changing that would mean loading resources before
deciding whether the caller may ask for them.

What was missing was not a fix but a guard, so the property is now pinned:
`ApiAuthorizationResultHandlerTests.The_402_is_byte_identical_for_a_real_foreign_id_and_a_fabricated_one`
asserts the whole envelope, not just the status — a difference anywhere in the body would read as
just as good an oracle as a difference in the status line.

## 3. What passed

| Layer | Result |
|---|---|
| **Tenant isolation, API** | Cross-tenant reads and writes on orders, customers, stores, franchises, users all denied (403/404) |
| **Tenant isolation, DB** | Brand A session: 9 orders. Brand B: 0. No brand: 0. Foreign id by explicit lookup: 0 rows |
| **Tenant-context forgery** | A brand admin sending another brand's `X-Brand-Id` is not moved. Garbage header handled |
| **Sub-brand boundary** | Store admin and warehouse staff denied on a franchise they hold no membership under; brand admin allowed |
| **Privilege escalation** | 6 routes refused, including self-promotion to `platform_admin` and password reset of another user |
| **Token lanes** | Staff token on customer endpoints → 403; customer token on admin → 403; anonymous → 401; tampered signature → 401 |
| **Step-up** | `users.set_password` and `stores.create` correctly return structured `403 step_up_required` |
| **Plan vs permission** | `402 feature_not_in_plan` distinguishable from a permission denial |
| **Customer isolation** | Cross-customer order by id → 404; two customers see 6 and 0 orders respectively |
| **Rate limiting** | Auth group limited (10/60s) — hit repeatedly during the audit, working as designed |
| **UI** | Warehouse staff typing an admin URL directly gets “You don't have access to this module”; sidebar correctly reduced |

## 4. Two structural facts worth recording

**Zero of the 136 RLS policies express the franchise or store boundary.** No policy references
`app.current_franchise_id` or `app.current_store_id`. That boundary exists only in
`ICurrentUser.IsWithinScope`, at 95 hand-placed call sites — one of which was missing
(`CreateFranchise`, task A0.7) and nothing detected it. This is the single strongest argument for
moving the rule into policy rows.

**The A0.6 customer-isolation fix is real and measurable.** Ten policies read
`app.current_customer_id`; the interceptor never set it. Because the clause is
`current_customer_id() IS NULL OR customer_id = current_customer_id()`, the unset variable made the
first arm **true** and every one of those policies degraded to brand equality — a **fail-open**.

| Database session | Payments visible |
|---|---|
| Brand context only, customer variable unset *(pre-fix)* | 5 |
| Brand context + customer 1 | 0 |
| Brand context + customer 2 | 2 |

## 5. Method note

One case was initially **inconclusive** and is worth recording as method. The first sub-brand test
targeted a store that turned out to be soft-deleted, so its 404s proved deletion rather than scope —
exposed by the own-tenant control failing. The suite was re-run against two live franchises before
the boundary was credited as passing.

## 6. Readiness

- **Authorization as it stands: production-sound.** Every boundary tested held, enforced twice over
  by layers that do not depend on each other.
- **ABAC as a control: not ready, and not claimed.** The engine agrees with its SQL twin on every
  operator, and is switched off, unmigrated, and holds zero policies. It cannot be credited until a
  shadow window on real traffic produces a clean parity report (`authz.parity_disagreement`, zero
  *looser* rows).

See `docs/ABAC_MODEL.md` for the authorization model and the cutover procedure, and
`docs/ABAC_IMPLEMENTATION_PLAN.md` §7a for task-by-task status.

## 7. Migrations — applied 2026-08-31

All seven ran; `migrate.sh status` reports 0 pending. The two that mattered most to this report:
**0023** revoked the `auditor` role's deny-propagated `domains.manage` and `pricing.slab.manage`
(verified gone), and **0029** closed F-3.

`0026` failed on the first attempt — `SELECT DISTINCT` resolved an untyped `NULL` as `text` before
the `INSERT` could infer `uuid` for `brand_id` — and was fixed with an explicit cast before
re-running. Worth recording as method: the file had been reviewed twice and the fault only appeared
when it was actually executed.

**Verified live after applying**, which is the part that matters for F-3. The same boundary now
holds independently at both layers:

| Session | Users seen via API | Users seen in a direct DB session |
|---|---|---|
| Brand admin (brand A) | 7 | 7 |
| Brand admin (brand B) | 1 | 1 |
| Platform admin / bypass | 20 | 20 |
| No brand context | — | 0 |

Cross-tenant read by id still returns `404`, own-tenant `200`, and login, the store/franchise/role
list endpoints and the platform admin's full view all still work — checked against the running
hosts after the migrations, not inferred.
