# Fix report — remediation of `docs/AUDIT_REPORT.md`

**Date:** 2026-09-05 · **Tracker:** `docs/FIX_TASKS.md` (per-task evidence) · **Report:**
`docs/AUDIT_REPORT.md`

All seven audit findings are closed. Every fix was driven end to end against the running system —
real database, real API, real browser, and, where the finding had a mobile surface, a real Android
emulator.

**Tests: 781 → 847 backend** (+66, all new), plus 91 rider-mobile and 170 customer-mobile. Zero
failures anywhere. Backend warnings unchanged at 66 — none introduced. Four migrations added, each
with a verified rollback.

---

## Issue → fix → evidence

| # | Finding | Fix | Proof it is closed |
|---|---|---|---|
| **A-1** | Guard asymmetry: `SetRoleCells` lacked the system-role, brand-isolation and rank guards `AssignPermission` had | One shared `RoleEditGuard` both handlers call; migration `0030` so the rank guard can actually resolve | The real exploit ran: rank-28 principal edited a rank-25 role — **pre-fix HTTP 200**, grants 31→34 including `royalty.override`; **post-fix HTTP 403**, 31→31. 18 tests |
| **A-2** | Self-signup owner typed `staff` while holding `brand_admin`; franchise owner typed to match | Both flows derive the type from the role via `UserType.ForPrimaryRole`; migration `0032` corrects 12 existing owners | `GET /admin/settings/` as that owner: **403 → 200**. Fresh signup produces `brand_admin \| brand_admin`. 15 tests |
| **A-3** | `UserType.franchise_owner` vs `NotificationRecipientType.franchisee` | Aligned onto `franchise_owner`; migration `0033` moves the CHECK constraint | Live insert: `franchisee` → **CHECK violation**, `franchise_owner` → **INSERT 0 1**. 3 tests |
| **A-4** | Person drawer could grant/revoke memberships and overrides but never list them | Two new read endpoints + both panels rewritten to render server state | A membership granted in **July** now appears and is revocable; overrides list a global deny and a scoped, time-boxed allow. Both disclaimers deleted. 12 tests |
| **A-5** | Permission catalogue drift | 7 codes folded into `PermissionDefs`; a drift test reads every repo SQL file | Deleted `dispatch.mode.manage` (171→170), restarted, seeder **recreated it identically** (→171). DB codes **171** = `PermissionDefs` **171**. 3 tests |
| **A-6** | Franchise/store GUCs published, read by zero RLS policies | `kernel.within_scope_cols` + a **RESTRICTIVE** policy on **39 tables** (migration `0031`) | A foreign store/franchise drops from **9 orders to 0**; every legitimate scope level unchanged at both DB and API. 15 tests |
| **A-7** | rider-mobile header claimed password login "not OTP"; the function was unreachable | Header corrected; `passwordLogin`, its type and two further stale comments removed | Rider OTP login re-run on a real Android emulator: bundled, signed in, live data. 0 `passwordLogin` references remain |

---

## Where the audit was wrong, and how

Three findings did not survive contact with the running system unchanged. Recording this because the
corrections are load-bearing.

**A-1 overstated one half and understated the other.** The report frames it as a system-role hole. It
was not live-exploitable: `identity_access.roles` carried an RLS policy that hid system roles from
brand sessions, so the attempt returned 404 before reaching the handler. The half that *was*
exploitable — rank escalation *within* a brand — no layer covered, and that is the one I reproduced
end to end. Both are now closed, at both layers.

**A-5 undercounted by six.** The report says one drifted code and "165 live". The live catalogue
holds **171**, and **seven** codes were missing from the seeder, not one.

**A-6 understated the gap.** The report says the boundary "is enforced only in application code
(roughly 95 call sites)". Measured before changing anything, four principals at four scope levels
asked the orders API for the same brand and all four received the same six orders — because those 95
call sites are *mutating* handlers that a read never reaches. The boundary was absent from the
database **and** from the read path; migration 0031 is the first layer of any kind to express it on
reads.

---

## Decisions made autonomously

**Shared rules instead of corrected copies.** A-1, A-2 and A-3 were all one rule written out twice
and drifting. Each is now a single implementation both callers use — `RoleEditGuard`,
`UserType.ForPrimaryRole`, one recipient constant — because fixing the copy that happens to be wrong
leaves the mechanism that made it wrong.

**Migration 0030 was written although the audit never named it.** Adding A-1's rank guard broke the
*negative* control: brand admins could no longer edit their own junior roles. The generic `rls_brand`
policy assumes a NOT NULL `brand_id`, but on `roles` a NULL means "global", so all 17 system roles
were invisible to every brand session and the guard could not resolve the caller's own rank.
`GetAccessRoles` already stated the opposite rule in code — handler and policy disagreed and the
policy silently won. Without 0030 the A-1 fix cannot function. Visible side effect: the roles list
went from 4 entries to 21, and a brand admin can offer a rider or store role for the first time.

**0031 relaxes one arm of `IsWithinScope`, deliberately.** A row whose column for the caller's scope
level is NULL does not constrain that caller. The strict rule would have hidden 977/977 `audit_logs`,
16/16 `system_settings` and 4/4 `price_lists` from every franchise- and store-scoped user, and every
order from warehouse staff. The arm protects nothing that was ever protected — a row naming no store
is not another store's row — while still refusing a row that names a *different* one. It is a strict
tightening of current behaviour and never a loosening, which is the property a backstop must have.

**0031 uses RESTRICTIVE policies and splits 0030 per command.** Permissive policies are OR'd, so a
new permissive policy could only widen access. And a single `FOR ALL` policy shares one USING clause
between reads and deletes while WITH CHECK cannot cover DELETE — measured: a `FOR ALL` form of 0030
let a brand session delete the global `brand_admin` row.

**A-7's dead code was removed rather than wired up.** The app is deliberately OTP-only. Building a
password screen would invent a feature nobody asked for; the backend endpoint still exists for other
clients, so the flow can be added back deliberately.

**A test I wrote and removed.** In A-5 I first asserted `Code == module + "." + action`. It failed on
nine rows and the rows were right: the `partner_*` family deliberately splits differently, and
`PermissionMatrix` parses the code string rather than those columns. Asserting a false invariant is
worse than asserting none.

**A bug I introduced and caught.** My first A-4 implementation reintroduced the very defect it was
fixing: granting a platform-scoped role succeeded but stayed invisible, because the brand filter
could not match a platform membership — and only a platform admin can create one, so the sole caller
who could make it was the sole caller who could not see it. Found by testing the feature rather than
the code, fixed, and locked with a test.

---

## Still outstanding

**The warehouse boundary cannot bind on orders.** 0031 implements and tests the warehouse arm, but
the application never populates `order_lifecycle.orders.warehouse_id` (0 of 9 rows) and
`tenancy_org.store_warehouse_mappings` is empty, so no data-model path from a warehouse membership to
an order exists. Enforcing it strictly today would show warehouse staff nothing. Closing it properly
means either populating `warehouse_id` at routing time or resolving warehouse→store through the
mapping table — a data-model change beyond a finding about RLS coverage.

## Observed but not changed

Each is real, none is an audit finding, and none was in scope.

- **`AdminSettings.Forbidden` gates on `user_type`, not a permission.** Its own comment calls it
  defence-in-depth behind the permission policy, but it reads the identity axis rather than the
  authority one — which is *how* A-2 denied a legitimate owner. My fix makes it correct for owners;
  the check is still the wrong shape and should become a permission gate.
- **`scripts/smoke.sh` is stale.** It probes 5050/5002/5005/8080 and fails all 16 checks with
  connection-refused on any current machine; the hosts moved to 5056/5015/5242 in `docs/PORTS.md` on
  2026-08-25. The equivalent probes were run by hand instead (all green).
- **The dev seeder and migration 0023 disagree.** `IdentitySeeder` runs on every `core.WebApi` start
  in Development and re-adds an `auditor` / `pricing.slab.manage` **deny** row that 0023 removed. The
  effective grant is identical either way, so it is drift rather than a defect — but the two
  disagree about whether the row should exist.
- **The revoke path uses `window.confirm`.** A native modal blocks browser automation entirely, which
  is why A-4's revoke was verified through the API and a reload rather than the button.

---

## Verification performed

- **Database:** all migrations applied, `migrate.sh status` → 0 pending; 0030/0031/0033 rolled back
  and re-applied to prove `down` works. RLS behaviour driven directly as the non-owner `app_user`
  role, because RLS does not apply to a table's owner.
- **API:** every endpoint exercised with real tokens through the real auth chain, including OTP
  step-up where the permission is high/critical. Cross-service reads confirmed 200 on every schema
  0031 touches.
- **Chrome:** access-control (People, Roles & Permissions, Policies, Franchises, Licensing), orders,
  warehouse board, riders, settings, CMS outbox — as platform admin, brand admin and store admin.
  Zero application console errors throughout; the only console entries in any session came from a
  Chrome extension's own content script.
- **Mobile:** rider-mobile run twice on a real Android emulator through Expo Go, signing in with the
  real 6-digit phone OTP and rendering live franchise-scoped data. The one ERROR in its logs is Expo
  Go's documented removal of push notifications in SDK 53, which requires a development build.
- **Test-data hygiene:** every account, membership, override, role grant and tenant created during
  verification was removed afterwards, and the three test accounts whose password hashes were
  temporarily changed were restored to their exact original values.

## Repository state

Changes are staged, not committed — no `git commit` or `git push` was run.
