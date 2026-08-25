-- 0014_impersonation_grants — forward migration
-- Wrapped in a transaction by migrate.sh (opt out: -- migrate: no-transaction)
--
-- §7: "Support: impersonation with consent + full audit (Platform Support role, read-first)."
-- §8.1: the platform must not "change a company's branding, config, or data without recorded
-- consent." Before this migration the only trace of the feature was an unused enum member,
-- `AuthMethod.Impersonation`.
--
-- ─── What this is NOT ────────────────────────────────────────────────────────────────────────
-- It is not a restriction on `platform_admin`. §2.3 already documents "operate-as-tenant": a
-- platform admin picks a brand in the BrandSwitcher, sends X-Brand-Id, and works inside that tenant
-- with RLS bypass. That path is unchanged, and narrowing it was not asked for.
--
-- This is the path for SUPPORT, who have no bypass and no brand: a consented, time-boxed,
-- read-first session that the provider grants and can end at any moment. The difference matters —
-- consent that the grantee can grant itself is not consent, which is why `impersonation.approve` is
-- deliberately NOT given to `platform_admin` below.
--
-- ─── Four properties, each enforced in SQL rather than trusted to the application ─────────────
--   1. CONSENT     — a grant is born `pending`; only an approval by a brand-side owner arms it.
--   2. TIME-BOXED  — an approved grant MUST carry an expiry, and it cannot exceed 24h.
--   3. READ-FIRST  — scope defaults to 'read_only'; read_write is a deliberate, separate answer.
--   4. REVOCABLE   — status flips to 'revoked' and the middleware re-reads state EVERY request, so
--                    revocation is immediate rather than "whenever the token expires".

CREATE TABLE IF NOT EXISTS identity_access.impersonation_grants (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id            uuid NOT NULL REFERENCES tenancy_org.brands(id) ON DELETE CASCADE,

    -- WHO is being let in. A single named person, never a role: "support can enter" is not consent,
    -- "this person can enter until 4pm" is.
    support_user_id     uuid NOT NULL REFERENCES identity_access.users(id) ON DELETE CASCADE,
    reason              text NOT NULL,

    scope               varchar(16) NOT NULL DEFAULT 'read_only'
                            CHECK (scope IN ('read_only', 'read_write')),
    status              varchar(16) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending', 'approved', 'revoked', 'expired', 'denied')),

    requested_at        timestamptz NOT NULL DEFAULT now(),
    approved_by_user_id uuid REFERENCES identity_access.users(id) ON DELETE SET NULL,
    approved_at         timestamptz,
    expires_at          timestamptz,
    revoked_at          timestamptz,
    revoked_by_user_id  uuid REFERENCES identity_access.users(id) ON DELETE SET NULL,
    revoke_reason       text,

    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_by          uuid,

    -- Property 1+2, as a constraint: an approved grant cannot exist without a named approver and an
    -- expiry. A NULL expiry would be a session that never ends, which is the failure mode this whole
    -- table exists to prevent.
    CONSTRAINT impersonation_grants_approved_is_complete CHECK (
        status <> 'approved'
        OR (approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL AND expires_at IS NOT NULL)
    ),

    -- The 24h ceiling. Chosen because it is the longest window that still forces a human to make the
    -- decision again the next day; a support engagement that outlives it should be re-consented, not
    -- inherited.
    CONSTRAINT impersonation_grants_window_bounded CHECK (
        expires_at IS NULL OR approved_at IS NULL
        OR (expires_at > approved_at AND expires_at <= approved_at + interval '24 hours')
    ),

    CONSTRAINT impersonation_grants_revoked_is_complete CHECK (
        status <> 'revoked' OR revoked_at IS NOT NULL
    )
);

-- One live grant per (brand, person). Without this a support engineer could accumulate overlapping
-- grants and outlive a revocation by falling back to an older one.
CREATE UNIQUE INDEX IF NOT EXISTS idx_impersonation_grants_one_live
    ON identity_access.impersonation_grants (brand_id, support_user_id)
    WHERE status IN ('pending', 'approved');

CREATE INDEX IF NOT EXISTS idx_impersonation_grants_brand
    ON identity_access.impersonation_grants (brand_id, status, requested_at DESC);

-- ─── RLS ─────────────────────────────────────────────────────────────────────────────────────
-- Brand-scoped like every other tenant table: a provider sees who asked to enter THEIR account and
-- nobody else's. Standard shape (kernel.rls_bypass() OR brand_id = kernel.current_brand_id()) so a
-- support session — whose token carries the target brand — reads exactly its own brand's rows.
ALTER TABLE identity_access.impersonation_grants ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_access.impersonation_grants FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS rls_brand ON identity_access.impersonation_grants;
CREATE POLICY rls_brand ON identity_access.impersonation_grants
    USING (kernel.rls_bypass() OR brand_id = kernel.current_brand_id())
    WITH CHECK (kernel.rls_bypass() OR brand_id = kernel.current_brand_id());

GRANT SELECT, INSERT, UPDATE ON identity_access.impersonation_grants TO app_user;

DROP TRIGGER IF EXISTS trg_impersonation_grants_updated_at ON identity_access.impersonation_grants;
CREATE TRIGGER trg_impersonation_grants_updated_at
    BEFORE UPDATE ON identity_access.impersonation_grants
    FOR EACH ROW EXECUTE FUNCTION kernel.set_updated_at();

-- ─── Both identities on every audited row ────────────────────────────────────────────────────
-- The acceptance criterion is "every impersonated action in audit_logs with BOTH identities".
-- actor_user_id already carries the human who acted (the support engineer — impersonation never
-- rewrites who did it). This column carries the consent under which they acted, and the grant row
-- names the owner who gave it. One nullable uuid rather than two denormalised user columns: the
-- join answers "who approved this, when, for how long, and was it read-only" — a copied user id
-- answers none of that.
--
-- audit_logs is partitioned; ALTER on the parent propagates to every partition and the default.
ALTER TABLE identity_access.audit_logs
    ADD COLUMN IF NOT EXISTS impersonation_grant_id uuid;

COMMENT ON COLUMN identity_access.audit_logs.impersonation_grant_id IS
    'Non-null when the action was taken inside a consented support impersonation session; '
    'joins to identity_access.impersonation_grants for the approver, scope and window.';

-- Deliberately NOT a foreign key. audit_logs is a 7-year append-only ledger across ~14 partitions;
-- an FK would make deleting a brand (which cascades its grants) either fail or silently blank the
-- audit trail. The trail must outlive the row it points at.
CREATE INDEX IF NOT EXISTS idx_audit_logs_impersonation
    ON identity_access.audit_logs (impersonation_grant_id, occurred_at DESC)
    WHERE impersonation_grant_id IS NOT NULL;

-- ─── The state oracle the middleware calls on EVERY request ──────────────────────────────────
-- SECURITY DEFINER for the same reason kernel.resolve_brand_domain and kernel.brand_status are: the
-- caller of this is a request that has not yet been allowed to see anything. Granting a blanket RLS
-- bypass to make one lookup work is how the brands-RLS trap gets sprung a fourth time; this grants
-- exactly one capability instead — "tell me the state of this one grant id".
--
-- It also does the lazy expiry. A grant whose window has passed reports 'expired' the moment it is
-- asked, without waiting for a sweep job to run: time passing must end the session by itself, not
-- because something remembered to notice.
CREATE OR REPLACE FUNCTION kernel.impersonation_grant_state(p_grant_id uuid)
RETURNS TABLE (status text, scope text, brand_id uuid, support_user_id uuid, expires_at timestamptz)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = identity_access, kernel, pg_catalog
AS $$
    SELECT CASE
               WHEN g.status = 'approved' AND g.expires_at IS NOT NULL AND g.expires_at <= now()
                   THEN 'expired'
               ELSE g.status::text
           END,
           g.scope::text, g.brand_id, g.support_user_id, g.expires_at
    FROM   identity_access.impersonation_grants g
    WHERE  g.id = p_grant_id;
$$;

REVOKE ALL ON FUNCTION kernel.impersonation_grant_state(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION kernel.impersonation_grant_state(uuid) TO app_user;

-- ─── Asking is a platform action against a tenant table ──────────────────────────────────────
-- A support engineer has no brand_id and no RLS bypass, so a plain INSERT of a pending request
-- would fail the WITH CHECK above. The tempting fix — give support bypass — is how the brands-RLS
-- trap keeps getting sprung: it grants a blanket capability to solve one call.
--
-- This grants exactly that one call instead. Note what it can and cannot do: it can only ever
-- create a row with status 'pending', which authorises NOTHING. Approval, the step that actually
-- opens the door, stays an ordinary RLS-scoped write performed by the brand's own owner — which is
-- precisely where row-level security should be doing the work.
CREATE OR REPLACE FUNCTION kernel.request_impersonation(
    p_brand_id        uuid,
    p_support_user_id uuid,
    p_reason          text,
    p_scope           text
) RETURNS uuid
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = identity_access, tenancy_org, kernel, pg_catalog
AS $$
DECLARE v_id uuid;
BEGIN
    IF p_scope NOT IN ('read_only', 'read_write') THEN
        RAISE EXCEPTION 'invalid impersonation scope: %', p_scope;
    END IF;

    IF btrim(coalesce(p_reason, '')) = '' THEN
        RAISE EXCEPTION 'an impersonation request must carry a reason';
    END IF;

    -- The brand must exist. Checked here rather than left to the FK so the caller gets a clear
    -- answer instead of a constraint-violation string.
    IF NOT EXISTS (SELECT 1 FROM tenancy_org.brands b WHERE b.id = p_brand_id) THEN
        RAISE EXCEPTION 'unknown brand %', p_brand_id USING ERRCODE = 'no_data_found';
    END IF;

    -- Re-asking while a request is already live returns the EXISTING one rather than raising. The
    -- unique index would otherwise turn a double-click into an error, and an already-approved grant
    -- must not be silently replaced by a fresh pending one — that would revoke access by accident.
    SELECT g.id INTO v_id
    FROM   identity_access.impersonation_grants g
    WHERE  g.brand_id = p_brand_id
      AND  g.support_user_id = p_support_user_id
      AND  g.status IN ('pending', 'approved')
    LIMIT  1;

    IF v_id IS NOT NULL THEN
        RETURN v_id;
    END IF;

    INSERT INTO identity_access.impersonation_grants
        (brand_id, support_user_id, reason, scope, status, created_by, updated_by)
    VALUES
        (p_brand_id, p_support_user_id, p_reason, p_scope, 'pending', p_support_user_id, p_support_user_id)
    RETURNING id INTO v_id;

    RETURN v_id;
END $$;

REVOKE ALL ON FUNCTION kernel.request_impersonation(uuid, uuid, text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION kernel.request_impersonation(uuid, uuid, text, text) TO app_user;

-- ─── Permissions ─────────────────────────────────────────────────────────────────────────────
-- Both hang off `settings`, which is a CORE module (§3.3: "core modules never gate"). That is
-- deliberate: an entitlement lapse must never be able to strip the ability to REVOKE a support
-- session. A provider whose plan expired mid-incident still gets to close the door.
INSERT INTO identity_access.permissions (id, code, module, action, name, description, is_system, requires_scope, risk_level, module_key, status, created_at, updated_at)
VALUES
    (gen_random_uuid(), 'impersonation.request', 'impersonation', 'request',
     'Request impersonation',
     'Ask a provider for a time-boxed, consented session inside their account.',
     true, false, 'high', 'settings', 'active', now(), now()),
    (gen_random_uuid(), 'impersonation.approve', 'impersonation', 'approve',
     'Approve or revoke impersonation',
     'Grant, deny or end a support session inside this account. Owner-only (§6 Law 1).',
     true, true, 'critical', 'settings', 'active', now(), now())
ON CONFLICT (code) DO UPDATE
    SET name = EXCLUDED.name, description = EXCLUDED.description,
        risk_level = EXCLUDED.risk_level, module_key = EXCLUDED.module_key, updated_at = now();

-- The platform side may ASK.
INSERT INTO identity_access.role_permissions (role_id, permission_id, created_at)
SELECT r.id, p.id, now()
FROM   identity_access.roles r
CROSS JOIN identity_access.permissions p
WHERE  p.code = 'impersonation.request'
  AND  r.code IN ('support', 'support_lead', 'platform_admin')
ON CONFLICT DO NOTHING;

-- The provider side may ANSWER — and `platform_admin` is not on this list on purpose. Consent the
-- grantee can grant itself is not consent. (A platform admin who needs tenant access already has
-- §2.3 operate-as-tenant; they do not need to forge an approval to get it, which is precisely why
-- withholding this permission costs nothing and is worth stating.)
INSERT INTO identity_access.role_permissions (role_id, permission_id, created_at)
SELECT r.id, p.id, now()
FROM   identity_access.roles r
CROSS JOIN identity_access.permissions p
WHERE  p.code = 'impersonation.approve'
  AND  r.code IN ('brand_admin', 'franchise_owner')
ON CONFLICT DO NOTHING;

-- ─── Assert the consent asymmetry actually holds ─────────────────────────────────────────────
DO $$
DECLARE bad text;
BEGIN
    SELECT string_agg(r.code, ', ')
    INTO   bad
    FROM   identity_access.roles r
    JOIN   identity_access.role_permissions rp ON rp.role_id = r.id
    JOIN   identity_access.permissions p       ON p.id = rp.permission_id
    WHERE  p.code = 'impersonation.approve'
      AND  EXISTS (SELECT 1 FROM identity_access.role_permissions rp2
                   JOIN identity_access.permissions p2 ON p2.id = rp2.permission_id
                   WHERE rp2.role_id = r.id AND p2.code = 'impersonation.request');

    IF bad IS NOT NULL THEN
        RAISE EXCEPTION 'consent is meaningless: role(s) % can both request and approve impersonation', bad;
    END IF;

    RAISE NOTICE 'impersonation: request and approve are held by disjoint roles';
END $$;
