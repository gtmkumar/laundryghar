-- 0020_fix_permission_group_modules — rollback
-- Restores all ten groups to the (incorrect) definitions seeded by 0013. All ten, not just the ones
-- 0020 changed: a partial restore left appended modules behind and made a re-apply fail its own
-- duplicate assertion.

UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['order','orders','pickup','booking','appointment','pos'] WHERE key='bookings';
UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['delivery','dispatch','rider_assignment'] WHERE key='dispatch';
UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['customer','customers','support'] WHERE key='customers';
UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['catalog','pricing','items','item','service','package','packages','fabric','fabrics']
 WHERE key='catalog';
UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['user','users','role','roles','rider','riders'] WHERE key='staff';
UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['payment','payments','refund','wallet','cashbook','expense','expenses'] WHERE key='money';
UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['garment','fulfillment','warehouse','warehouse_ops','quality'] WHERE key='processing';
UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['report','reports','analytics','audit'] WHERE key='reports';
UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['brand','brands','settings','feature_flag','cms'] WHERE key='settings';
UPDATE identity_access.permission_groups SET permission_modules =
    ARRAY['saas','subscription','royalty','platform_plans'] WHERE key='billing';
