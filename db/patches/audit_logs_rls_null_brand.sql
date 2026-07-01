-- =============================================================================
-- db/patches/audit_logs_rls_null_brand.sql
--
-- PURPOSE (audit-trail HIGH fix): let audit_logs rows for NON-BRAND actors
-- (system / RaaS partner / background worker) pass the WITH CHECK so they can
-- never abort the business write that produced them.
--
-- THE BUG
--   AuditSaveChangesInterceptor AddRange()s the audit row into the SAME physical
--   context as the business change, so the audit INSERT runs INSIDE the business
--   transaction. Under the hardened non-superuser app_user, a session with NO
--   brand context (a RaaS partner: token_use=partner, no brand claim, bypass=false)
--   stamps brand_id = NULL on the audit row (AuditContext.Fill copies
--   ICurrentTenant.BrandId, which is NULL for that actor). The old audit_logs
--   rls_brand WITH CHECK was:
--
--       kernel.rls_bypass() OR brand_id = kernel.current_brand_id()
--       -> false            OR NULL      = NULL                       -> NULL -> REJECT
--
--   PostgreSQL treats the NULL predicate as "not satisfied" and raises 42501, which
--   rolls back the WHOLE transaction — e.g. a partner booking never persists purely
--   because its audit row could not be written.
--
-- THE FIX
--   Relax ONLY the WITH CHECK (write path) to also permit brand_id IS NULL, so a
--   system/partner/worker audit row is accepted. The USING (read path) clause is
--   left STRICT and unchanged: partners/tenants still cannot READ another brand's
--   audit rows, and NULL-brand system rows remain visible ONLY to bypass
--   (platform-admin) sessions — never leaked cross-tenant.
--
-- FOLLOW-UP (deliberately out of scope here)
--   Full out-of-band audit isolation — writing audit rows on a SEPARATE connection
--   / transaction so that NO audit failure of any kind can ever roll back a business
--   write — is a planned follow-up. This patch removes the one identified
--   RLS-rejection failure mode (NULL brand_id) but does not change where the audit
--   INSERT runs.
--
-- Idempotent / re-runnable (DROP POLICY IF EXISTS before CREATE). Independent of
-- whether RLS is currently ENABLED on the table — a policy definition stands on its
-- own; enablement is unchanged by this file.
--
-- RUN (manual, as postgres):
--   PGPASSWORD=postgres /opt/homebrew/opt/postgresql@16/bin/psql \
--     "postgresql://postgres:postgres@localhost:5432/laundry_ghar_db" \
--     -f db/patches/audit_logs_rls_null_brand.sql
-- =============================================================================

SET client_min_messages = WARNING;

DROP POLICY IF EXISTS rls_brand ON identity_access.audit_logs;
CREATE POLICY rls_brand ON identity_access.audit_logs FOR ALL TO app_user
    -- READ: strict — a session sees only its own brand's audit rows (NULL-brand
    -- system rows are readable exclusively via rls_bypass / platform admin).
    USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
    -- WRITE: brand match OR bypass OR NULL brand_id (system/partner/worker actor),
    -- so a non-brand actor's audit INSERT can never abort its business write.
    WITH CHECK (kernel.rls_bypass()
                OR brand_id = kernel.current_brand_id()
                OR brand_id IS NULL);

-- Sanity check.
SELECT tablename, cmd, qual AS using_clause, with_check
FROM   pg_policies
WHERE  schemaname = 'identity_access'
  AND  tablename  = 'audit_logs'
  AND  policyname = 'rls_brand';

SELECT 'audit_logs_rls_null_brand.sql applied successfully.' AS result;
