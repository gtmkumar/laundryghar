# ABAC Implementation Plan — Laundry Ghar

**Verdict first: ABAC is not implemented.** Not partially, not behind a flag, not in a branch.
The platform runs RBAC + scope + entitlement + step-up, and every attribute-shaped rule it does
enforce is hardcoded in a C# handler. This document establishes that with evidence, then plans the
layer that fixes it.

*State as of 2026-08-31. Every count below was executed against the live `laundry_ghar_db` or the
current `main` (`0aa074a`). Where a doc and the code disagree, the code wins.*

---

## 0. The finding

Searching the entire repository for ABAC returns **exactly one line**, and it is a recommendation
for future work, not an implementation:

> `RBAC_Navigation_PaaS_PostgreSQL.md:507` — under **"9. Extension points"**:
> "**ABAC overlay** — keep RBAC as the base; layer attribute rules (time, IP, record attributes)
> for the few cases that need them. Don't model everything as attributes."

The database agrees. There is no `authz` schema. Across all 14 schemas, **no table in
`identity_access` has a column capable of holding a condition** — no `condition`, `rule`,
`expression`, `predicate`, or `policy` column, and no JSONB anywhere in the access-control tables.
`identity_access.role_permissions` is `(role_id, permission_id, effect)` and nothing more: a role
either holds a code or it does not.

```
$ psql -d laundry_ghar_db -At -c "select table_schema||'.'||table_name||'.'||column_name
    from information_schema.columns
    where column_name ~ '(condition|predicate|expression|rule|policy|attribute|abac)'"
```
Every hit is a business-domain column — `commerce.promotions.rules`, `customer_catalog.items.attributes`,
`logistics.incentive_rules.rule_type`. **Zero hits in `identity_access`.**

### What exists instead

Authority is four gates, all of them set-membership or equality, all resolved without ever looking
at the row being acted on:

| Gate | Where | Decides on | Attribute-shaped? |
|---|---|---|---|
| Identity | `token_use` claim | which lane you are in | no |
| Authority | `PermissionHandler.cs:15` | `permissions` claim contains the code | no — string set membership |
| Freshness | `StepUp.cs` | seconds since `stepup_at`, 5-min window | **yes — environment**, hardcoded |
| Entitlement | `ScopeResolver.cs:177-231` | brand's licensed features | **yes — tenant attribute**, hardcoded |
| Visibility | 136 RLS policies | `brand_id = kernel.current_brand_id()` | no — tenant equality |

Two of those five *are* attribute conditions. Neither is expressed as data; both are C# and both are
unique implementations that no other rule can reuse.

---

## 1. The distinction that makes this tractable

A handler naming **what it is** is metadata. A handler naming **who may call it, and under which
conditions** is a rule. Only the second must move to data.

| Stays in code (metadata) | Moves to data (rule) |
|---|---|
| `RequireAuthorization("permission:payment.refund")` — this endpoint issues refunds | Who may refund, up to what amount, on which payment statuses, within what window |
| Attribute keys registered in the catalogue | Attribute comparisons and their values |
| Table and column names in the resource registry | The brand / franchise / store / status predicates over them |

Everything in the right-hand column today lives in **95 `IsWithinScope` call sites**, **87
`UnauthorizedAccessException` throws**, and **136 hand-written RLS policies**. That is what this
plan moves.

---

## 2. Measured starting point

All figures from the live database and `main @ 0aa074a`, not from documentation.

| Dimension | Count | Note |
|---|---|---|
| `RequireAuthorization("permission:…")` sites | **335** across **94** endpoint files | the PEP surface to migrate |
| Total `RequireAuthorization(…)` sites | 453 | includes 28 actor-type policies (`CustomerOnly`, `RiderOnly`, `PartnerOnly`, `PartnerAdmin`) |
| Distinct permission codes referenced by an endpoint | **161** of **171** in the catalogue | 10 codes gate nothing |
| Permissions / roles / grants | 171 / 23 / 771 | 98 of the grants are `effect='deny'` |
| Live memberships | **20** | 17 of 23 roles have zero members |
| `user_permission_override` rows | **0** | the table is written by one endpoint and read by no query |
| Step-up permissions | **62** (13 critical + 49 high) | the only environment condition in the system |
| `IsWithinScope(...)` call sites | **95** | each hand-resolves the resource's ancestry |
| `IsPlatformAdmin` branches | 32 | a master key: `ScopeType == "platform"` also satisfies it |
| Hardcoded role literals in C# (excl. seeders/tests) | **37** across 19 files | |
| `UnauthorizedAccessException` throws in Application layers | **87** | the shadow policy layer |
| `AuthorizationHandler<>` implementations | **8** | none is resource-based; none takes a `TResource` |
| RLS policies | **136** | |
| …referencing `current_brand_id` | **118** | |
| …referencing `current_franchise_id` or `current_store_id` | **0** | both GUCs are *set* on every connection and read by nothing |
| …referencing any status / amount / time column | **0** | RLS is pure tenant equality |
| ABAC policy/attribute/condition tables | **0** | |

### The shadow policy layer, counted

The rules the RBAC engine does not express are implemented inline, roughly **250 times**:

| Category | Attribute class | Distinct sites | Extracted? |
|---|---|---|---|
| Scope guards via `IsWithinScope` | relationship | **74 guards across 56 files** | one shared method, **100% manual invocation** |
| Customer self-filter (`CustomerId == cmd.CustomerId`) | relationship | **49** | no |
| Rider self-resolve (`(UserId, BrandId)` → `rider.Id`) | relationship | **19** across 15 files | no |
| Status / state-conditional authority | resource | **107 files** carry status conditionals; ~64 throw on them | only for orders (`IFulfillmentStrategy`) |
| Time-window gating | environment | 7 OTP files + erasure grace + 3 coupon-validity copies | threshold math only |
| Threshold / amount | resource | 3 coupon-cap copies, 1 payout balance, 1 dead expense threshold | no |
| In-handler `IsPlatformAdmin` branching | subject | **24** | no |
| Hierarchy / priority ladders | subject vs resource | **2 independent ladders** | no |

Two things *are* extracted into policy objects — `IFulfillmentStrategy` and `IsWithinScope` — which
proves the pattern is achievable here. Both are still hand-invoked, and the gaps below are exactly
the handlers that forgot to call them.

> ⚠️ **Do not plan against `docs/SCHEMA_FULL.sql`.** It is a stale 2026-06-05 snapshot: its
> `role_permissions` (line 589) has **no `effect` column** and its `permissions` (line 571) lacks
> `module_key` and `status`. The live database is ahead — `effect` arrived via
> `db/patches/permission_overrides.sql:16`, and `scope_type`/`scope_id`/`expires_at` via
> `db/patches/permission_override_scope_expiry.sql:23`.

### Three facts worth stating on their own

**1 · Six session variables are set; two are read.** `RlsConnectionInterceptor.cs:62-67` sets
`app.current_brand_id`, `current_franchise_id`, `current_store_id`, `current_user_id`,
`current_partner_id`, `bypass_rls` on every connection. Only `current_brand_id` and `bypass_rls`
appear in any policy. **The entire franchise/store/warehouse layer has zero database enforcement** —
it exists only in the 95 `IsWithinScope` calls, and a handler that forgets one has no backstop.

**2 · `app.current_customer_id` is read by 10 policies, set by nobody — and it fails OPEN.** The
clause is not a plain equality; it is guarded:

```sql
rls_bypass() OR (brand_id = current_brand_id()
                 AND (current_customer_id() IS NULL OR customer_id = current_customer_id()))
```

`RlsConnectionInterceptor` issues six `set_config` calls and customer is not among them, so
`kernel.current_customer_id()` returns NULL on every application connection, the `IS NULL` arm
short-circuits to **true**, and all ten policies **degrade to plain brand-id equality**. They are on
`commerce.payments`, `payment_refunds`, `wallet_accounts`, `wallet_transactions`, `customer_packages`,
`package_usage_ledger`, `coupon_redemptions`, `loyalty_points_ledger`,
`engagement_cms.notification_preferences` and `whatsapp_message_log`.

At the database layer **one customer's wallet and payment rows are visible to any connection
carrying the same brand**. Customer isolation on financial data exists only in application code.
This is a live defect, not a latent one, and any ABAC layer that assumed DB-enforced customer
ownership would inherit a false premise — which is why A0.6 sits before A1.

**3 · No rule in the system depends on an amount.** `IssueRefundHandler` is the canonical case:

```csharp
// commerce.Application/Commerce/Admin/Payments/AdminPaymentHandlers.cs
:92   if (!_user.IsWithinScope(brandId: payment.BrandId, franchiseId: …, storeId: …))
          throw new ForbiddenException("This payment is outside your assigned scope.");   // 403 — authz
:95   if (payment.Status != "captured" && payment.Status != "completed")
          throw new BusinessRuleException(…);                                             // 400 — validation
:115  if (alreadyRefunded + req.Amount > payment.Amount)
          throw new BusinessRuleException(…);                                             // 400 — validation
```

Three conditions over resource attributes, three different exception types, three different HTTP
statuses, and **no per-role refund ceiling anywhere** — a `store_admin` and a `finance_manager` can
refund the same unbounded amount. "Refunds above ₹5,000 need a finance manager" is not expressible
today without editing C#.

---

## 3. Design principles

1. **Rules are rows.** No authorization predicate in C#, none in an RLS policy body.
2. **Date-versioned.** `effective_from` / `effective_to` on every policy. A franchise agreement
   changes; the decision made last quarter must stay reproducible.
3. **One rule, one place.** The application and PostgreSQL evaluate the same rows. Drift must be
   uncommittable, not merely discouraged.
4. **Fail closed.** Unresolved attribute, unreachable policy store, empty result — all deny.
5. **Every decision explainable.** Which policy matched, on which attributes. A denial nobody can
   explain becomes a permanent support ticket.
6. **Tenant-authored policies are first class.** `authz.policy.brand_id IS NULL` is a platform rule;
   non-null is a brand's own. This is a multi-vertical PaaS — a salon brand and a laundry brand will
   not want the same refund ceiling.
7. **No role names inside rules.** A role is just one subject attribute among many.

---

## 4. Target architecture

### Schema — `authz`

```
authz.attribute        -- catalogue: key, category (subject|resource|action|environment),
                       -- data_type, resolver_key. Code cannot use an unregistered attribute.
authz.resource_type    -- registry: key, schema_name, table_name, attribute_map jsonb
                       -- (attribute key -> column). This is what lets RLS be GENERATED.
authz.action           -- read, create, update, delete, refund, approve, assign, cancel, …
authz.policy           -- key, brand_id, effect (permit|deny), resource_type, action,
                       -- permission_code (the coarse RBAC gate it refines), priority,
                       -- effective_from, effective_to, is_active, version
authz.policy_condition -- AND/OR/NOT/compare tree: node_type, left_attribute, operator,
                       -- right_kind (literal|attribute|function), right_value jsonb
authz.decision_log     -- partitioned, append-only, with a correct WITH CHECK
```

Combining algorithm: **deny-overrides, then priority, then default-deny.**

### Application components

One home: `laundryghar.Utilities/Authorization/Abac/`. It is already referenced by all three hosts
(`core`, `operations`, `commerce`) and already hosts `PermissionHandler`, `StepUp` and
`PermissionPolicyProvider` — so no new project, and no chance of the three-copy drift that
`CurrentUser*` variants produce elsewhere.

| Component | Role | Replaces |
|---|---|---|
| `IAttributeResolver` + keyed DI registry | **PIP** — supplies attribute values | ad-hoc `ICurrentUser` reads, hand-loaded entities |
| `IPolicyDecisionPoint` | **PDP** — permit/deny + reason, never throws | the 87 `UnauthorizedAccessException` sites |
| `IPolicyFilterBuilder` | compiles the same rows to an EF `Expression` | the hand-written list fences |
| `AbacAuthorizationHandler` | **PEP** — an ASP.NET requirement alongside `PermissionRequirement` | extends, does not replace, `PermissionHandler` |
| `authz.permits(...)` | the same evaluator, in SQL, for RLS | 136 hand-written policy bodies |

The PEP composes with the existing gate rather than replacing it: `permission:payment.refund` still
runs first as the coarse check, then the PDP evaluates conditions against the loaded resource. That
keeps "what can this person do?" cheaply enumerable for the admin console — the thing pure ABAC
gives up.

### Anti-drift core

```sql
-- One evaluator, reading the same authz.policy rows the application reads.
CREATE FUNCTION authz.permits(p_resource_type text, p_action text, p_row jsonb)
RETURNS boolean SECURITY DEFINER STABLE PARALLEL SAFE;

-- Every generated RLS policy becomes uniform:
USING (authz.permits('commerce.payment', 'read', to_jsonb(payments.*)))
```

Two mechanisms keep it honest:
- a **generator** regenerates all policies from `authz.resource_type` — nobody hand-writes a policy
  body again;
- a **CI test** asserts `pg_policies` equals the generator's output. A hand-edited policy fails the
  build.

---

## 5. Performance — the real risk

A per-row function call is the obvious objection and a fair one. Mitigations, in order of preference:

1. Resolve the subject's derived sets **once per request** into GUCs, so the per-row test is set
   membership, not a graph walk.
2. `STABLE PARALLEL SAFE` so PostgreSQL caches within a scan.
3. For hot tables (`order_lifecycle.orders`, `logistics.rider_location_pings`, both partitioned) the
   generator emits a **specialised predicate** — still generated from the same rows, so still no drift.

**A9.1 is a go/no-go gate.** If generated RLS cannot meet the latency budget on the largest tables,
we keep hand-written RLS there and keep the CI equivalence test. *The test is the load-bearing part,
not the generation.*

---

## 6. Phase map

| Phase | Theme | Tasks | Gate |
|---|---|---|---|
| **A0** | Close the standing authority defects first | 10 | they would be inherited as seed data |
| **A1** | Attribute and policy schema | 5 | schema review |
| **A2** | PIP — attribute resolvers | 3 | every registered attribute resolves |
| **A3** | PDP — evaluation engine | 4 | unit-test matrix green |
| **A4** | PEP — pipeline and query filters | 4 | |
| **A5** | Generated RLS from the same rows | 4 | CI drift test passes |
| **A6** | Shadow mode and parity harness | 3 | zero unexplained disagreements |
| **A7** | Cutover and de-hardcoding | 4 | per module, behind a flag |
| **A8** | Policy authoring and explain UI | 3 | |
| **A9** | Performance, governance, docs | 3 | go/no-go on generated RLS |

**Sequencing rule: A0 lands before A1.** Shipping ABAC over a role catalogue where `auditor` — the
read-only role — holds `domains.manage`, and where the access-control screen renders deny rows as
grants, would encode both defects into the new model's seed data.

---

## 7. Non-goals

- **No new policy-engine dependency** (OPA, Cedar, Casbin). The evaluator is small, and a second
  runtime on the request path is a failure mode whose fail-open direction is catastrophic.
- **No change to the tenant model.** This plan expresses the existing brand → franchise → store
  hierarchy as data; it does not redesign it.
- **Not a rewrite of the three services.** Handlers gain two metadata attributes and lose their
  hand-written fences.
- **RBAC is not deleted.** Permission codes remain the coarse gate and the enumerable surface for
  the admin console. ABAC adds the conditions RBAC cannot express.

---

## 7a. Implementation status — 2026-08-31

**Solution builds clean. 781 tests green** — 116 core + 426 operations + 239 integration. That
includes 32 integration tests that apply migration 0025 to a real PostgreSQL and prove the SQL and
C# evaluators agree on every operator, 6 that lock shut the cross-tenant user disclosure the live
audit found, and 14 that apply migration 0029 the same way to prove the user directory's new
database-level tenant boundary (see `docs/ABAC_AUDIT_2026-08-31.md`).

### Done and test-verified

| Ref | What landed |
|---|---|
| A0.2 | `GetAccessRoles.cs` filters `rp.Effect != "deny"` |
| A0.3 | `SetRoleCells.cs` flips a deny to allow on enable; disable no longer deletes denies |
| A0.4 | `AssignPermission.cs` rewritten: existence, system-role guard, brand isolation, role-priority anti-escalation, effect-awareness, `perm_version` bump |
| A0.5 | `SetUserType.cs` bumps `PermVersion` in the same `SaveChanges` |
| A0.6 | `ICurrentTenant.CustomerId`; `HttpContextCurrentTenant` splits `sub` by `token_use`; interceptor publishes `app.current_customer_id` — closes a **fail-open** on ten policies |
| A0.7 | `CreateFranchise` guards `IsWithinScope(brandId:)`, matching its four siblings |
| A0.8 | `OrderCancellationRefund` extracted; all **three** cancellation paths now refund. Two of them previously cancelled a paid order and kept the money |
| A2.1 | `IAbacAttributeProvider` + per-request memoised bag; resource attributes cannot leak between decisions |
| A2.2 | `SubjectAttributeResolver` — absent-vs-known-null enforced per attribute |
| A2.3 | `EnvironmentAttributeResolver` (channel from the **signed token**, never a header) + `NpgsqlResourceAttributeResolver` reading `resource_type.attribute_map` |
| A3.1 | `ConditionEvaluator` — three-valued, 15 operators |
| A3.2 | `PolicyDecisionPoint` — deny-overrides → priority → default-deny |
| A3.3 | `PolicyCache` — 15s window matching the `perm_version` bound; serves last-known-good on a failed refresh rather than an empty set |
| A3.4 | Structured 403 `policy_denied` carrying the matched policy key and reason |
| A4.1 | `[AbacResource]` / `[AbacAction]` / `[AbacResourceId]` + `RequireAbac()` |
| A4.2 | `AbacAuthorizationHandler` composes with `PermissionRequirement`; registered on all three hosts |
| A4.3 | `PolicyFilterBuilder` — the same policy rows compiled to an EF `Expression`; untranslatable deny hides everything, untranslatable permit widens nothing |
| A5.1 | `authz.permits()` + `authz.eval_node` + `authz.compare` + `authz.within_scope`, `SECURITY DEFINER STABLE PARALLEL SAFE`, pinned `search_path` |
| A5.2 | Five subject GUCs; `?` sentinel keeps *unresolved* distinct from *empty* across a pooled connection |
| A5.3 | `authz.generated_policy_body` / `generated_policy_sql` / `apply_generated_rls` |
| A5.4 | `authz.rls_drift` view — non-empty means a generated policy was hand-edited |
| A6.1 | Shadow mode; `Abac:Enabled=false`, `Mode=shadow` by default |
| A6.2 | Both verdicts on one row (`rbac_allowed`) + `authz.parity_disagreement` / `parity_summary` |
| A7.3 | `IsPlatformAdmin` no longer satisfied by `ScopeType == "platform"` — verified against live data first |
| A7.4 | `IsWithinScope` **denies** on an absent `scope_nodes` claim |
| A8.1/8.2 | Policy list + "why was this denied" in admin-web; `/api/v1/admin/policies` |
| A8.3 | A brand may tighten a platform policy, never loosen it — enforced in the UPDATE predicate |
| A9.3 | `docs/ABAC_MODEL.md` |

Also shipped: a `roles` claim on the staff JWT. Without it `subject.roles` is unresolvable, and
since an unevaluable **deny** fails closed, every backfilled deny policy would have fired — denying
everything the moment a module was enforced. Roles are already loaded at mint time, so this costs
no extra query.

### Applied — 2026-08-31

All seven ran against `laundry_ghar_db`; `migrate.sh status` reports **0 pending**.

`0026` failed on the first attempt and was fixed before re-running: `SELECT DISTINCT` has to
resolve every output column's type in order to dedupe, so it settled an untyped `NULL` as `text`
before the `INSERT` could infer `uuid` from the target `brand_id` column. The permit projection
immediately above it is the same shape *without* `DISTINCT`, which is why only the deny projection
failed — and why a migration that had been read twice still had never shown the fault until it was
actually run.

Verified after applying: `authz` holds **306 policy rows** (171 permits + 135 denies), the
`auditor` role no longer holds `domains.manage` or `pricing.slab.manage`, RLS is on for the nine
previously unprotected `brand_id` tables, and `identity_access.users` now enforces its tenant
boundary at both layers — a brand admin's session returns **7** users through the API and **7**
through a direct database session, a second brand **1**, bypass **20**, no brand context **0**.

| Migration | Task | What it does |
|---|---|---|
| 0023 | A0.1 | Revokes `auditor`'s deny-propagated `domains.manage` + `pricing.slab.manage` |
| 0024 | A1.1–A1.5 | Schema `authz`: attributes, resource types, actions, policies, conditions, partitioned decision log |
| 0025 | A5.1–A5.4 | `authz.permits()` and the RLS generator — **changes no existing policy** |
| 0026 | A6.3 | Projects the 171 permissions + 98 role denies onto policy rows. Behaviourally identical to today by construction |
| 0027 | A0.9, A0.10 | RLS on the 9 unprotected `brand_id` tables; `requires_scope` enforced as policy |
| 0028 | A6.2 | `rbac_allowed` + the parity views |
| 0029 | F-3 | RLS on `identity_access.users`, resolved through live memberships rather than a stored `brand_id` |

ABAC now exists in the database, but the engine stays inert by configuration: `Abac:Enabled`
defaults to `false` and `Mode` to `shadow`. Nothing is enforced until A7.1 cuts a module over, and
that gates on a clean parity window.

### Not started

| Ref | Why |
|---|---|
| A4.4 | Retiring the 95 `IsWithinScope` call sites — gated on A7.1's parity window |
| A7.1 | Per-module cutover — needs a shadow window on real traffic |
| A7.2 | Deleting the 37 hardcoded role literals — gated on A7.1 |
| A9.1 | Generated-RLS benchmark — needs the migrations applied and representative data |
| A9.2 | Policy-change approval workflow |

**A0.9 note.** Four of the nine tables (`kernel.*`) already carry a correct-looking `rls_brand`
policy that was **never enabled**, so it has been inert since it was written. Two others
(`kernel.system_settings`, `finance_royalty.platform_plans`) hold platform rows with `brand_id IS
NULL`; a plain `brand_id = current_brand_id()` would have hidden them from every tenant, so 0027
gives those the `brand_id IS NULL OR …` shape.

---

## 8. Task breakdown — 43 tasks

`Gap` is what the audit found: **Missing** = does not exist; **Incorrect** = exists and is wrong.

### A0 · Close the standing authority defects (10) — *blocks A1*

| Ref | Task | Gap | Depends on |
|---|---|---|---|
| A0.1 | Add `AND rp.effect = 'allow'` to the two grant-propagation scripts (`0019:52-61`, `value_slab_pricing.sql`) and revoke `auditor`'s `domains.manage` + `pricing.slab.manage`. Both scripts read a DENY row as evidence of holding the permission, so the read-only role can rewrite a brand's custom domain. | Incorrect | — |
| A0.2 | Filter `Effect` in `GetAccessRoles.cs:44`. The access-control matrix selects permission codes with no effect filter, so `auditor`'s 97 deny rows render as ticks — the read-only role looks like the most powerful in the platform. | Incorrect | — |
| A0.3 | Make `SetRoleCells.cs:69` effect-aware. Ticking a cell that has a deny row inserts nothing, leaves the deny, and returns success. | Incorrect | A0.2 |
| A0.4 | Guard `AssignPermissionCommandHandler` (`AssignPermission.cs:16-27`) — 11 lines with no guards behind a `critical` permission, reachable from the one "Users · Approve" checkbox that maps to `permissions.assign` + `memberships.grant`. | Incorrect | — |
| A0.5 | Bump `perm_version` in `SetUserType.cs:61-69`. It bumps `users.version` but not `perm_version`, so a `user_type` change — the thing that grants the total bypass — is unrevocable until natural refresh. | Incorrect | — |
| A0.6 | Set `app.current_customer_id` in `RlsConnectionInterceptor.cs:62-67`. Ten `rls_brand_or_customer` policies read a GUC the interceptor never sets; because the clause is `current_customer_id() IS NULL OR customer_id = …`, the unset GUC makes them **fail open** and degrade to brand-only equality. | Missing | — |
| A0.7 | Add the missing scope guard to `CreateFranchise`. It is the only handler in its family that does not inject `ICurrentUser`, taking `BrandId` off the request body; its four siblings each carry `IsWithinScope`. RLS's `WITH CHECK` blocks cross-brand, but sub-brand escalation — a franchise-scoped holder of `franchises.create` minting a sibling franchise — was open. | Missing | — |
| A0.8 | Reconcile the three order-cancellation paths. `CancelOrderCommand` issues a refund; `CancelOrderByCustomerCommand` and `UpdateOrderStatusCommand`(→cancelled) do not. Three rule sets for one conceptual transition. | Incorrect | — |
| A0.9 | Enable RLS on the 9 tables that carry `brand_id` but have it switched off: `commerce.subscription_billing_attempts`, `commerce.subscription_usage_ledger`, `finance_royalty.franchise_subscription_events`, `finance_royalty.platform_plans`, `identity_access.oauth_authorization_codes`, `kernel.feature_flags`, `kernel.file_attachments`, `kernel.outbox_events`, `kernel.system_settings`. | Missing | — |
| A0.10 | Decide whether `permissions.requires_scope` is enforced or dropped. All 171 rows carry it; a grep of `backend/`, `admin-web/`, `pos-web/` finds only the EF property (`Permission.cs:17`), its column mapping, the seeder and test fixtures. No branch reads it. | Incorrect | — |

### A1 · Attribute and policy schema (5)

| Ref | Task | Gap | Depends on |
|---|---|---|---|
| A1.1 | Create schema `authz` and `authz.attribute` — the attribute catalogue (key, category, data_type, resolver_key). Code cannot reference an unregistered attribute. | Missing | A0.* |
| A1.2 | `authz.resource_type` — registry mapping a logical resource to schema/table plus an `attribute_map` jsonb (attribute key → column). This is what lets RLS be generated. | Missing | A1.1 |
| A1.3 | `authz.action` — the action vocabulary, and the resource × action matrix seeded from the 161 permission codes that gate an endpoint. | Missing | A1.2 |
| A1.4 | `authz.policy` + `authz.policy_condition` — header with `brand_id`, effect, priority, `effective_from`/`effective_to`, version; conditions as an AND/OR/NOT/compare tree. | Missing | A1.2, A1.3 |
| A1.5 | `authz.decision_log` — partitioned by month, append-only, with a correct `WITH CHECK`. Reads must be logged too; today `AuditSaveChangesInterceptor.cs:85` fires only on writes. | Missing | A1.1 |

### A2 · PIP — attribute resolvers (3)

| Ref | Task | Gap | Depends on |
|---|---|---|---|
| A2.1 | `IAttributeResolver` contract + keyed DI registry, in `laundryghar.Utilities/Authorization/Abac/`. Resolution is per-request memoised and fails closed. | Missing | A1.1 |
| A2.2 | Subject attribute resolver — user_type, roles, scope_nodes, brand/franchise/store, `stepup_at`, entitled features. Replaces scattered `ICurrentUser` reads. | Missing | A2.1 |
| A2.3 | Resource + environment resolvers — resource attributes read via `resource_type.attribute_map`; environment = now, IP, channel, `token_use`. | Missing | A2.1, A1.2 |

### A3 · PDP — evaluation engine (4)

| Ref | Task | Gap | Depends on |
|---|---|---|---|
| A3.1 | Condition evaluator + operator set (`eq, neq, lt, lte, gt, gte, in, not_in, contains, starts_with, is_null, within_scope, older_than, newer_than`), typed against `attribute.data_type`. | Missing | A1.4, A2.1 |
| A3.2 | Combining algorithm — deny-overrides, then priority, then default-deny. Must reproduce today's cross-role deny semantics (`ScopeResolver.cs:119-124`). | Missing | A3.1 |
| A3.3 | Policy cache with version-based invalidation, mirroring the existing `perm_version` propagation (~15s bound) — including the franchise/store members `PermVersionBumper.BumpBrandMembersAsync:38-44` currently skips. | Missing | A3.2 |
| A3.4 | Explain — every decision returns the matched policy key and the attributes it compared; surfaced as a structured 403 by `ApiAuthorizationResultHandler`. | Missing | A3.2 |

### A4 · PEP — pipeline and query filters (4)

| Ref | Task | Gap | Depends on |
|---|---|---|---|
| A4.1 | `[Resource("commerce.payment")]` / `[Action("refund")]` metadata on endpoints — the handler names *what it is*, never *who may call it*. | Missing | A1.3 |
| A4.2 | `AbacAuthorizationHandler` composing with the existing `PermissionRequirement` — coarse code check first, then conditions against the loaded resource. | Missing | A3.2, A4.1 |
| A4.3 | `IPolicyFilterBuilder` — compile the same policy rows into an EF `Expression` so list endpoints filter instead of loading-then-denying. | Missing | A3.1 |
| A4.4 | Retire the 95 `IsWithinScope` call sites module by module, replaced by a `within_scope` operator evaluated from policy rows. | Incorrect | A4.2, A4.3 |

### A5 · Generated RLS from the same rows (4)

| Ref | Task | Gap | Depends on |
|---|---|---|---|
| A5.1 | `authz.permits(resource_type, action, row jsonb)` — `SECURITY DEFINER STABLE PARALLEL SAFE`, reading the same `authz.policy` rows the application reads. | Missing | A1.4 |
| A5.2 | Subject attributes into session GUCs once per request, so the per-row test is set membership rather than a graph walk. Extends the existing 6-GUC `set_config` block. | Missing | A2.2, A5.1 |
| A5.3 | RLS policy generator from `authz.resource_type` — regenerates all 136 policies uniformly. Nobody hand-writes a policy body again. | Missing | A5.1, A1.2 |
| A5.4 | CI drift test — `pg_policies` must equal the generator's output. A hand-edited policy fails the build. **This is the load-bearing task of A5.** | Missing | A5.3 |

### A6 · Shadow mode and parity (3)

| Ref | Task | Gap | Depends on |
|---|---|---|---|
| A6.1 | Shadow mode — the PDP evaluates and writes to `authz.decision_log` but does not enforce. Flag `Abac:Mode = shadow\|enforce`, default shadow. | Missing | A4.2, A3.4 |
| A6.2 | Parity harness — replay real traffic, diff ABAC decision vs current decision, report every disagreement with its explain string. Exit gate: zero *unexplained* disagreements. | Missing | A6.1 |
| A6.3 | Seed policies backfilled from the 771 `role_permissions` rows so ABAC starts behaviourally identical to today — a projection, not a rewrite. | Missing | A1.4, A6.1 |

### A7 · Cutover and de-hardcoding (4)

| Ref | Task | Gap | Depends on |
|---|---|---|---|
| A7.1 | Per-module cutover behind the flag: commerce → operations → core, one module at a time, each with its own parity window. | Missing | A6.2, A6.3 |
| A7.2 | Delete the 37 hardcoded role literals across 19 non-seed files; roles become an ordinary subject attribute. | Incorrect | A7.1 |
| A7.3 | Narrow the `IsPlatformAdmin` master key (`HttpContextCurrentUser.cs:46-48`) — `ScopeType == "platform"` currently satisfies it, which is why a platform-scoped `auditor` passes `IsWithinScope` for every target. | Incorrect | A7.1 |
| A7.4 | Fix `IsWithinScope`'s fail-open on an absent `scope_nodes` claim (`HttpContextCurrentUser.cs:84`) — a missing claim allows everything. Fails closed once no pre-feature tokens remain. | Incorrect | A7.1 |

### A8 · Policy authoring and explain UI (3)

| Ref | Task | Gap | Depends on |
|---|---|---|---|
| A8.1 | Policy authoring UI in `admin-web` — build on the existing `FilterableTable` + `FormDrawer` pattern. Replaces the checkbox matrix whose "Settings · View" cell grants 38 permissions. | Missing | A7.1 |
| A8.2 | Explain view — "why was this denied", reading `authz.decision_log`. Also fixes the fact that `user_permission_override` is write-only with no query to read it back. | Missing | A3.4, A1.5 |
| A8.3 | Brand-scoped policy authoring with guardrails — a brand may tighten a platform policy, never loosen it. | Missing | A8.1 |

### A9 · Performance, governance, docs (3)

| Ref | Task | Gap | Depends on |
|---|---|---|---|
| A9.1 | **Go/no-go gate.** Benchmark generated RLS on the largest tables (`order_lifecycle.orders`, `logistics.rider_location_pings`, `identity_access.audit_logs` — all partitioned). If the budget is missed, keep hand-written RLS there and keep A5.4. | Missing | A5.3 |
| A9.2 | Policy change governance — approval workflow, `effective_from` scheduling, and an append-only change record. A policy edit is a production change. | Missing | A8.1 |
| A9.3 | Documentation and runbook; reconcile `docs/rbac.md` (lists 13 roles; the DB has 23) and fold `docs/AUTHORITY_MODEL.md` findings into the new model. | Missing | A7.1 |
