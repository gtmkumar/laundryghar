-- ============================================================================
-- rls_enable_commerce.sql  — BC-6 commerce RLS activation
-- ----------------------------------------------------------------------------
-- The kernel-helper RLS policies (rls_brand / rls_brand_or_customer) were
-- already defined on every commerce table by rls_proposal.sql, but ROW LEVEL
-- SECURITY was only ENABLED on `packages`. This activates RLS on the other 12,
-- and drops the one legacy raw-cast policy (packages_tenant, DEF-002 class)
-- after confirming a kernel-helper policy still protects that table.
-- Idempotent: ENABLE is a no-op when already on; DROP POLICY IF EXISTS is safe.
-- ============================================================================
SET client_min_messages = WARNING;

DO $$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'packages','customer_packages','package_usage_ledger',
        'loyalty_programs','loyalty_points_ledger',
        'coupons','coupon_redemptions','promotions',
        'payment_methods','payments','payment_refunds',
        'wallet_accounts','wallet_transactions'
    ]
    LOOP
        -- Safety: never enable RLS on a table that has no policy (would lock out app_user)
        IF EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='commerce' AND tablename=t) THEN
            EXECUTE format('ALTER TABLE commerce.%I ENABLE ROW LEVEL SECURITY', t);
        ELSE
            RAISE EXCEPTION 'commerce.% has no RLS policy — refusing to ENABLE (would deny all app_user access)', t;
        END IF;
    END LOOP;
END $$;

-- Drop the legacy raw-cast policy on packages, but only if the kernel-helper
-- rls_brand policy is present (so isolation is never removed).
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_policies
               WHERE schemaname='commerce' AND tablename='packages' AND policyname='rls_brand') THEN
        DROP POLICY IF EXISTS packages_tenant ON commerce.packages;
    ELSE
        RAISE EXCEPTION 'commerce.packages missing kernel-helper rls_brand — refusing to drop legacy packages_tenant';
    END IF;
END $$;
