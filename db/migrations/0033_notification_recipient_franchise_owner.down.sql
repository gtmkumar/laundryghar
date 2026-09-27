-- Down for 0033. Restores 'franchisee' as the notification recipient spelling — the drift audit
-- finding A-3 describes.

SET client_min_messages = WARNING;

UPDATE engagement_cms.notifications_log
   SET recipient_type = 'franchisee'
 WHERE recipient_type = 'franchise_owner';

ALTER TABLE engagement_cms.notifications_outbox
    DROP CONSTRAINT IF EXISTS notifications_outbox_recipient_type_check;

ALTER TABLE engagement_cms.notifications_outbox
    ADD CONSTRAINT notifications_outbox_recipient_type_check
    CHECK (recipient_type IN ('customer', 'user', 'rider', 'franchisee', 'manual'));

UPDATE engagement_cms.notifications_outbox
   SET recipient_type = 'franchisee'
 WHERE recipient_type = 'franchise_owner';
