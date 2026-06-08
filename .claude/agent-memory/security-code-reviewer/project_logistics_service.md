---
name: project-logistics-service
description: Security posture of laundryghar.Logistics (BC-5, riders) — connection role, RLS coverage, rider onboarding cross-service trust model, key finding on users-table IDOR
metadata:
  type: project
---

BC-5 laundryghar.Logistics manages riders (logistics.riders profile) + assignments + capacity + rider-self endpoints.

**Connection role — DIFFERS from Catalog:** Logistics Program.cs uses ConnectionStrings:Default = `app_user` (NON-superuser), so RLS IS enforced at runtime. (Catalog used `postgres` superuser, bypassing RLS — different posture.) There is also a `Admin`/`postgres` conn string in appsettings but DbContext binds `Default`.

**RLS coverage (verified in db/patches/rls_enable_logistics.sql + database_scripts/01_bc1):**
- logistics.riders, rider_assignments, rider_capacity_config, rider_location_pings → RLS `rls_brand` policy: `USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())` FOR app_user. ENABLED (not FORCE — superuser escape hatch intended).
- franchises, stores → tenant RLS policies (franchises_tenant, stores_tenant) in 01_bc1_tenancy_org.sql. Protected.
- **users + user_profiles (Identity schema 02_bc2) have NO RLS and NO brand_id column.** They are GLOBAL tables. User↔brand binding lives in `user_scope_memberships` (user × scope × role). This is the dominant cross-tenant subtlety for anything that joins users by id.

**Brand isolation model:** Handlers explicitly do `_user.RequireBrandId()` + `.Where(r => r.BrandId == brandId)` on rider rows AND RLS double-enforces. DbContext has NO global brand query filter (only soft-delete filters) — brand isolation is RLS + explicit predicates, never a global EF filter.

**Rider DTO enrichment (RiderQueries.GetRiders / CreateRiderHandler.LoadEnrichedAsync):** Batch-fetches users/user_profiles/franchises/stores by ids harvested from already-brand-scoped rider rows. List-path enrichment is SAFE: the userIds/franchiseIds/storeIds all come from riders rows that passed brand filter + RLS, so no cross-brand id can enter the lookup. franchises/stores lookups are additionally RLS-protected; users/user_profiles are global but only reachable via a rider you already own.

**KEY FINDING — CreateRiderHandler userId IDOR (Medium):** CreateRiderHandler validates only `u.UserType == UserType.Rider && u.DeletedAt == null` against the global `users` table — it does NOT verify the user has a `user_scope_membership` in the actor's brand. An operator with rider.manage can attach ANY brand's rider user to their own brand's rider profile (rider row lands in attacker brand, so it does not directly leak the other brand's rider list, but it binds a foreign identity + that user's global email/phone/name then surface in the attacker's RiderDto enrichment). Fix: join user_scope_memberships to confirm brand membership before creating the profile.

**FranchiseId / PrimaryStoreId at create:** Validator only checks NotEmpty; no brand-ownership check in C#. RLS WITH CHECK on riders enforces the rider's brand_id = current brand (set server-side from RequireBrandId), but franchise_id/store_id columns on riders are NOT validated to belong to the brand at insert — RLS on riders only checks riders.brand_id. A crafted franchiseId/storeId from another brand could be stored on the rider row; enrichment then can't resolve its name (RLS on franchises/stores hides it → shows null/—), so low practical leak but data-integrity gap.

**AuthZ:** rider endpoints correctly gated — GET rider.read, POST/PUT/deactivate rider.manage; group has .RequireAuthorization(). rider-self under RiderOnly policy. No new unauthenticated endpoint.
