-- 0033 — one spelling for the franchise owner.
--
-- Audit finding A-3, "naming drift": `UserType` spells the role `franchise_owner`;
-- `NotificationRecipientType` spelled the same real person `franchisee`. Two names for one concept,
-- and the raw value reaches users — admin-web's Notification Outbox and Logs tabs render
-- `recipientType` directly in their Recipient column when no phone or email is present, so an
-- operator could see "franchisee" on one screen and "franchise_owner" on every other.
--
-- ── Why franchise_owner is the spelling that survives ────────────────────────────────────────
--
-- It is the one that is load-bearing everywhere else: identity_access.users.user_type, the seeded
-- roles.code, the JWT roles claim, and the CHECK constraints behind them. `franchisee` appears in
-- exactly one enum, is referenced by no code at all, and matches no row in either notifications
-- table. Changing the widely-used name to match the unused one would be the expensive direction and
-- the wrong one.
--
-- ── Deliberately NOT renamed ─────────────────────────────────────────────────────────────────
--
-- tenancy_org.franchise_agreements.franchisee_legal_name / _pan / _gstin / _phone / _email keep
-- their names. Those are fields of a legal instrument, where "franchisee" is the correct term for
-- the contracting party — the same word doing a different job. The finding is about two enums
-- naming one person two ways, not about the contract's vocabulary.
--
-- Verified before writing this: zero rows in engagement_cms.notifications_outbox or
-- notifications_log carry recipient_type = 'franchisee' (both hold only 'customer'), and
-- notifications_log has no CHECK on the column at all. The UPDATEs below are therefore no-ops on
-- this database and exist so the migration is correct on one where they are not.

SET client_min_messages = WARNING;

UPDATE engagement_cms.notifications_log
   SET recipient_type = 'franchise_owner'
 WHERE recipient_type = 'franchisee';

-- The constraint has to admit the new value before any row can carry it.
ALTER TABLE engagement_cms.notifications_outbox
    DROP CONSTRAINT IF EXISTS notifications_outbox_recipient_type_check;

ALTER TABLE engagement_cms.notifications_outbox
    ADD CONSTRAINT notifications_outbox_recipient_type_check
    CHECK (recipient_type IN ('customer', 'user', 'rider', 'franchise_owner', 'manual'));

UPDATE engagement_cms.notifications_outbox
   SET recipient_type = 'franchise_owner'
 WHERE recipient_type = 'franchisee';
