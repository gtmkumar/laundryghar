# The Laundry Ghar authorization model

*Companion to `docs/ABAC_IMPLEMENTATION_PLAN.md`. This document explains how a request gets
allowed or denied, in the order the system actually decides it.*

---

## 1. The one-line version

> **Who you are** → **what you hold** → **what you're touching** → **which rules apply** → **yes or no**

Spelled out:

```
User
 └─ has Attributes ......... user_type, brand, franchise, store, step-up freshness, channel
 └─ holds Memberships ...... (scope node, role) pairs — "brand_admin AT brand:acme"
      └─ Role
           └─ grants Permissions ..... orders.create, payment.refund, …  (allow / deny)
                                        ↓
                                 baked into the JWT at login
                                        ↓
Request  ──▶  RBAC gate ......... do you hold the permission code?     ── no ──▶ 403
                  │ yes
                  ▼
              ABAC gate ......... do the policy rows permit it?        ── no ──▶ 403 + reason
                  │ yes
                  ▼
              Handler ........... loads the row
                  │
                  ▼
              RLS ............... does the database let you see it?    ── no ──▶ 0 rows
                  │ yes
                  ▼
              Response
```

Four gates. A request must clear **all four**. Any one of them saying no is a denial — and that
redundancy is the point: the UI hiding a button is a convenience, not a control.

---

## 2. The pieces, defined

| Thing | What it is | Where it lives |
|---|---|---|
| **User** | A person or machine principal | `identity_access.users` |
| **Attribute** | A fact about the user, the resource, or the moment | `authz.attribute` (catalogue), resolved per request |
| **Scope node** | A place in the tenant tree: `platform`, `brand:<id>`, `franchise:<id>`, `store:<id>`, `warehouse:<id>` | `scope_nodes` JWT claim |
| **Membership** | "This user holds this role at this node" | `identity_access.user_scope_memberships` |
| **Role** | A named bundle of permissions | `identity_access.roles` |
| **Permission** | A coarse capability code, e.g. `payment.refund` | `identity_access.permissions` |
| **Policy** | A rule: *effect* + *resource* + *action* + *condition* | `authz.policy` + `authz.policy_condition` |
| **Decision** | permit / deny / not-applicable, with a reason | `authz.decision_log` |

### Roles vs. attributes — the distinction that matters

A **role** answers *"what job does this person do?"* — it is coarse, enumerable, and it is what an
admin console can render as a list.

An **attribute** answers *"what is true right now?"* — the order's value, the store it belongs to,
whether the caller re-verified in the last five minutes, whether it's the customer's own record.

RBAC alone cannot say *"a store manager may refund, but only up to ₹5,000, only for their own
store, and only within 24 hours."* Everything after the first comma is an attribute. That sentence
is why ABAC exists here.

**This system is a hybrid, on purpose.** RBAC stays as the coarse gate because "what can this person
do?" must remain answerable by a query for the admin console and for audit. ABAC refines it. Pure
ABAC would make that question require simulating every possible request.

---

## 3. Where each attribute comes from

| Attribute | Source | If it can't be resolved |
|---|---|---|
| `subject.user_id`, `subject.customer_id` | JWT `sub`, split by `token_use` | known-null; comparisons work |
| `subject.user_type` | JWT `user_type` | known-null |
| `subject.brand_id` / `franchise_id` / `store_id` | JWT claims | known-null |
| `subject.permissions` | JWT `permissions` | resolved-empty |
| `subject.roles` | JWT `roles` | **absent → deny** |
| `subject.scope_nodes` | JWT `scope_nodes` | **absent → deny** |
| `subject.stepup_at` | JWT `stepup_at` | known-null (= never stepped up) |
| `subject.entitlements` | DB, cached 15s | **absent → deny** |
| `resource.*` | The row itself, via `authz.resource_type.attribute_map` | **absent → deny** |
| `env.now`, `env.ip`, `env.channel` | Request | always resolved |

### The three-valued rule

Every comparison returns **true**, **false**, or **indeterminate**. Indeterminate means *"an attribute
this rule needs could not be resolved."*

- An **indeterminate DENY still denies.** A deny whose condition can't be evaluated must not
  silently vanish.
- An **indeterminate PERMIT does not permit.**

So the system fails **closed**. There is one deliberate exception, documented in
`AbacAuthorizationHandler`: if the ABAC engine itself *throws*, the request falls back to the RBAC
gate rather than failing closed — a bug in the new layer must not take down endpoints the old layer
was protecting correctly.

**Absent ≠ null.** A key that is *missing* is unresolved (→ indeterminate → deny). A key that is
*present with a null value* is a known fact and compares normally. Collapsing those two is how
authorization systems fail open; every layer here keeps them apart.

---

## 4. How a decision is combined

Given every policy targeting this `(resource_type, action)`:

1. **Deny-overrides.** Any deny that matches → **DENY**. Any deny that *can't be evaluated* →
   **DENY**.
2. **Highest-priority permit that actually holds** → **PERMIT**.
3. **Otherwise → DENY** (default-deny).

Priority orders policies *within* a pass. It never lets a permit outrank a deny.

This mirrors what the RBAC layer already does: `ScopeResolver` computes
`effective = (roleAllowed − roleDenied ∪ userAllow) − userDeny`, so a deny anywhere kills a
permission everywhere.

---

## 5. Worked scenarios

> Read these as: *who, holding what, touching what, trying what, and why the answer is what it is.*

### Scenario 1 — the ordinary allow

| | |
|---|---|
| **User** | Alice |
| **Role** | Store manager |
| **Attributes** | `scope_nodes = store:S1`, `brand = Acme` |
| **Resource** | Order #100, which belongs to store S1 |
| **Action** | `update` |
| **Policy** | *Staff may update orders inside their assigned scope* |
| **Decision** | ✅ **Allowed** |

**Why:** Alice holds `orders.update`, and `within_scope` compares her membership node `store:S1`
against the order's `store_id` — they match.

- **UI:** the Edit button is shown.
- **API:** `PATCH /orders/100` returns 200.
- **Database:** the RLS brand predicate passes; the row is visible.

### Scenario 2 — the same person, a different store

| | |
|---|---|
| **User** | Alice (same role, same everything) |
| **Resource** | Order #200, belonging to store **S2** |
| **Action** | `update` |
| **Decision** | ❌ **Denied** |

**Why:** `within_scope` finds no membership node matching `store:S2`.

- **UI:** order #200 never appears in her list.
- **API:** `PATCH /orders/200` returns 403 — **even though she holds `orders.update`**. The
  permission says *what* she may do; the scope says *where*.
- **Database:** same brand, so RLS alone would have let this through. This is exactly the
  franchise/store boundary that no hand-written RLS policy expresses, and why the application check
  cannot be skipped.

### Scenario 3 — another brand entirely

| | |
|---|---|
| **User** | Alice, at brand Acme |
| **Resource** | Order #300, belonging to brand **Globex** |
| **Decision** | ❌ **Denied — twice** |

- **API:** 403 from the scope check.
- **Database:** even if the API check were removed, RLS returns **zero rows**: the policy is
  `brand_id = current_brand_id()` and the GUC says Acme. Two independent layers.

### Scenario 4 — a refund above the ceiling *(the rule RBAC cannot express)*

| | |
|---|---|
| **User** | Bob |
| **Role** | Store manager, holds `payment.refund` |
| **Resource** | Payment of **₹8,000** |
| **Policy** | *deny* `payment/refund` when `resource.amount > 5000` and `subject.roles contains store_manager` |
| **Decision** | ❌ **Denied** |

**Why:** the deny matches on the amount. Bob genuinely holds the permission — RBAC would allow
this. The ceiling lives in a policy row, not in code, so raising it to ₹10,000 is a data change.

**The same payment at ₹3,000:** ✅ allowed, same person, same role, same permission.

### Scenario 5 — a customer reaching for someone else's data

| | |
|---|---|
| **User** | Carol, a customer |
| **Attributes** | `token_use = customer`, `sub = C1` |
| **Resource** | Payment belonging to customer **C2**, same brand |
| **Decision** | ❌ **Denied** |

**Why:** `app.current_customer_id` is set to C1, and the RLS policy reads
`customer_id = current_customer_id()`.

> ⚠️ **This is the A0.6 defect.** Before the fix, the interceptor never set that GUC. Because the
> clause is `current_customer_id() IS NULL OR customer_id = current_customer_id()`, an unset GUC made
> the first arm **true** and every one of those ten policies degraded to plain brand equality — a
> **fail-open**. Customer isolation rested entirely on per-handler ownership checks. Setting the GUC
> is what closes it.

### Scenario 6 — the attribute nobody could resolve

| | |
|---|---|
| **User** | Dan, on a token minted before `scope_nodes` existed |
| **Policy** | *Staff may update orders inside their assigned scope* |
| **Decision** | ❌ **Denied** |

**Why:** `subject.scope_nodes` is absent → indeterminate → deny.

> ⚠️ **This is the A7.4 defect.** `IsWithinScope` used to `return true` on an absent claim, as
> rollout safety for pre-feature tokens. The rule it actually stated was *"a token missing its
> authorization data passes every scope check"* — the broadest fail-open in the model.

---

## 6. The four layers, and what each one is for

| Layer | Enforces | Can it be skipped? |
|---|---|---|
| **UI** | Hides what you can't use | **Yes — trivially.** Never a control. |
| **API / RBAC** | Do you hold the permission code? | No — server-side, on the signed token |
| **API / ABAC** | Do the policy rows permit it, given the attributes? | No |
| **Database / RLS** | Does the tenant predicate admit the row? | No — even a SQL-injected query is filtered |

**Why four.** Each catches what the one above cannot. RLS gives brand isolation that survives an
application bug, but it cannot express the franchise/store boundary — no hand-written policy
references `app.current_franchise_id`. ABAC expresses that boundary in one place instead of 95
hand-placed `IsWithinScope` calls, one of which was *missing* (`CreateFranchise`, A0.7) and nothing
detected it.

---

## 7. Operating the engine

| Setting | Default | Meaning |
|---|---|---|
| `Abac:Enabled` | `false` | Master switch. Off = the PDP is never consulted. |
| `Abac:Mode` | `shadow` | `shadow` = evaluate and log, never block. `enforce` = block on deny. |
| `Abac:EnforcedResourceTypes` | `[]` | Which modules are cut over. Empty = all (only when `Mode=enforce`). |
| `Abac:LogPermits` | `true` | Log permits as well as denies. Disagreements are always logged. |

### The cutover procedure

1. `Abac:Enabled = true`, `Mode = shadow`. Nothing changes for anyone; decisions accumulate.
2. Watch `authz.parity_summary` and `authz.parity_disagreement`.
3. **`abac_looser` must be zero.** ABAC permitting what RBAC denies is a defect in the projection,
   not a policy decision. One row here is worse than a thousand of the other kind.
4. Review every `abac_stricter` row. Each is either the point of the policy, or an unresolved
   attribute failing closed on a path nobody considered.
5. Add the module to `EnforcedResourceTypes`, set `Mode = enforce`. One module at a time.
6. Watch `authz.decision_log` where `mode = 'enforce'` and `decision = 'deny'`.

**Rollback is a config change**, not a deploy: drop the module from `EnforcedResourceTypes`.

### Reading a denial

Every ABAC denial returns a structured 403 naming the matched policy key and the reason. The
`Policies → Why was this denied?` tab in admin-web reads `authz.decision_log` directly, including
the attributes that were compared. This is strictly more than the rest of the system offers:
`AuditSaveChangesInterceptor` fires only on writes, so a denied **read** leaves no trace anywhere
else.

---

## 8. Known gaps

Honest list, current as of this document:

- **Policy conditions are authored in SQL, not in the UI.** The console lists policies, shows their
  conditions and lets you activate/deactivate and reprioritise them. Building a condition *tree*
  from the UI is not built (A8.1 is partial).
- **No RLS policy has been switched to the generated form.** `authz.apply_generated_rls()` exists
  and is tested; the go/no-go benchmark (A9.1) has not been run, and the plan expects the largest
  partitioned tables to keep hand-written policies.
- **The 95 `IsWithinScope` call sites are still there** (A4.4). They are correct; they are just
  duplicated by the `within_scope` operator now. Retiring them is gated on a parity window.
- **37 hardcoded role literals across 19 files** (A7.2). Gated on the same parity window.
- **`env.channel` cannot distinguish admin from POS** — both present as `token_use=user`.
  Separating them needs a signed claim; a header would be caller-controlled and therefore
  worthless for a policy decision.
