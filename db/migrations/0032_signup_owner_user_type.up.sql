-- 0032 — make a self-signed-up brand owner's user_type agree with the role they were given.
--
-- Audit finding A-2, "two owners, two shapes". The platform creates an owner two ways and they
-- disagreed:
--
--   CompleteSignup (self-serve)  →  user_type 'staff'           + role brand_admin
--   InviteOwner    (franchise)   →  user_type 'franchise_owner'  + role franchise_owner
--
-- Same real-world concept — the person who owns this business — described two different ways,
-- reconciled only by a code comment noting that a dedicated Owner preset had not shipped.
--
-- ── Why it is not cosmetic ───────────────────────────────────────────────────────────────────
--
-- user_type and role are two independent axes, and code that reads the wrong one silently gets the
-- wrong answer. AdminSettings.Forbidden gates on `UserType == "brand_admin"` rather than on a
-- permission, so a self-signed-up owner holding the brand_admin ROLE was refused their own brand's
-- settings. Measured on this database before the fix: 12 live accounts hold a primary brand-scoped
-- brand_admin membership while typed 'staff'.
--
-- The rule both flows now follow, and which this migration applies retrospectively: user_type
-- mirrors the tier of the primary role the account holds.
--
-- ── Why the predicate is this narrow ─────────────────────────────────────────────────────────
--
-- Only accounts whose PRIMARY, live, brand-scoped membership is brand_admin are touched. Someone
-- who merely holds an additional brand_admin membership somewhere is not an owner and keeps their
-- type; so does anyone already typed correctly, anyone soft-deleted, and anyone whose brand_admin
-- membership is revoked or expired. This raises no one's authority that their own primary role did
-- not already grant — a type is not a permission, and the role was already there.

SET client_min_messages = WARNING;

UPDATE identity_access.users u
   SET user_type = 'brand_admin',
       updated_at = now(),
       version    = u.version + 1
 WHERE u.deleted_at IS NULL
   AND u.user_type <> 'brand_admin'
   AND EXISTS (
        SELECT 1
          FROM identity_access.user_scope_memberships m
          JOIN identity_access.roles r ON r.id = m.role_id
         WHERE m.user_id     = u.id
           AND m.is_primary
           AND m.revoked_at IS NULL
           AND (m.expires_at IS NULL OR m.expires_at > now())
           AND m.scope_type  = 'brand'
           AND r.code        = 'brand_admin'
           AND r.deleted_at IS NULL);
