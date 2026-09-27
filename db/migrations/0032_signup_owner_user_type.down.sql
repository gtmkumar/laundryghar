-- Down for 0032. Returns brand owners whose primary role is brand_admin to user_type 'staff' —
-- the shape CompleteSignup used to create and which audit finding A-2 describes.
--
-- Note this is not a per-row restore: an account legitimately typed 'brand_admin' before 0032 ran
-- is indistinguishable, by predicate, from one 0032 corrected. The forward migration is a
-- convergence onto the rule "user_type mirrors the primary role", so rolling it back re-introduces
-- the divergence for every account the rule covers rather than only those it changed.

SET client_min_messages = WARNING;

UPDATE identity_access.users u
   SET user_type = 'staff',
       updated_at = now(),
       version    = u.version + 1
 WHERE u.deleted_at IS NULL
   AND u.user_type = 'brand_admin'
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
