-- ============================================================================
-- rls_enable_finance_royalty.sql  — BC-7 finance_royalty RLS activation
-- ----------------------------------------------------------------------------
-- The kernel-helper rls_brand policies were already defined on all 8
-- finance_royalty tables by rls_proposal.sql (verified 2026-06-05), but
-- ROW LEVEL SECURITY was not ENABLED on any of them.
--
-- This patch activates RLS on all 8 tables.  No legacy raw-cast policies
-- exist in this schema (pg_policies query confirmed zero *_tenant rows).
--
-- Policy bucket: all 8 tables are B1 (brand-only).
-- Policy on every table: rls_brand  FOR ALL  TO app_user
--   USING      (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
--   WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
--
-- Idempotent:
--   • ALTER TABLE … ENABLE ROW LEVEL SECURITY is a no-op when already on.
--   • The safety guard (no-policy check) raises an exception if a policy is
--     ever missing — prevents accidentally locking out app_user.
-- ============================================================================
SET client_min_messages = WARNING;

DO $$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'cash_books',
        'cash_book_entries',
        'expense_categories',
        'expenses',
        'expense_attachments',
        'shift_handovers',
        'royalty_invoices',
        'royalty_calculations'
    ]
    LOOP
        -- Safety: never enable RLS on a table that has no policy (would lock
        -- out app_user entirely — deny-by-default with no permissive policy).
        IF EXISTS (
            SELECT 1 FROM pg_policies
            WHERE schemaname = 'finance_royalty'
              AND tablename  = t
        ) THEN
            EXECUTE format(
                'ALTER TABLE finance_royalty.%I ENABLE ROW LEVEL SECURITY', t);
            RAISE NOTICE 'RLS enabled on finance_royalty.%', t;
        ELSE
            RAISE EXCEPTION
                'finance_royalty.% has no RLS policy — refusing to ENABLE '
                '(would deny all app_user access)', t;
        END IF;
    END LOOP;
END $$;

-- Post-condition report: rowsecurity flag + policy count per table.
SELECT
    c.relname                                  AS table_name,
    c.relrowsecurity                           AS rls_on,
    count(p.policyname)                        AS policy_count,
    string_agg(p.policyname, ', ' ORDER BY p.policyname) AS policies
FROM   pg_class c
JOIN   pg_namespace n ON n.oid = c.relnamespace
LEFT   JOIN pg_policies p
         ON p.schemaname = n.nspname
        AND p.tablename  = c.relname
WHERE  n.nspname  = 'finance_royalty'
  AND  c.relkind  = 'r'
GROUP  BY c.relname, c.relrowsecurity
ORDER  BY c.relname;
