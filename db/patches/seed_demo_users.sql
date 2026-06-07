-- ============================================================================
-- seed_demo_users.sql  (idempotent)
-- Durable demo operator logins so they survive a DB rebuild. These were created
-- via the Identity admin API; this patch re-inserts the exact rows (incl. the
-- already-hashed passwords) + their primary scope memberships.
--
-- Dev credentials (plaintext):
--   warehouse@laundryghar.local  / Warehouse@123   → warehouse_supervisor @ Mumbai Central Warehouse
--   storeadmin@laundryghar.local / Store@123        → store_admin          @ Laundry Ghar Sector 45
--
-- (Platform admin admin@laundryghar.local / Admin@123 is created by the Identity seeder.)
-- Run as a privileged (postgres) connection.
-- ============================================================================

BEGIN;

INSERT INTO identity_access.users
  (id, email, phone_e164, password_hash, user_type, locale, timezone, status,
   must_change_password, mfa_enabled, failed_attempts, version, created_at, updated_at)
VALUES
  ('0bf0de2e-e57d-4ef8-8473-99ccfa254d38', 'warehouse@laundryghar.local', '+919800000050',
   'v1$4riI4jnHyN/BKRjfoW1qUg==$LMXwzEK1bMHKqKZM/d3TfEra87IV7vSqXpojBMksXqo=',
   'warehouse_staff', 'en-IN', 'Asia/Kolkata', 'active', false, false, 0, 1, now(), now()),
  ('a6e82798-3d0d-434d-aca8-780b26d89242', 'storeadmin@laundryghar.local', '+919800000051',
   'v1$x4wS4923drGpuglHZEHP+A==$+wOZZzuwy+zw48wMVK7NKelIuzLZ69YdvGepPehkCdQ=',
   'store_admin', 'en-IN', 'Asia/Kolkata', 'active', false, false, 0, 1, now(), now())
ON CONFLICT (id) DO NOTHING;

INSERT INTO identity_access.user_scope_memberships
  (id, user_id, scope_type, scope_id, role_id, is_primary, granted_at, metadata, created_at)
VALUES
  -- warehouse@ → warehouse_supervisor @ Mumbai Central Warehouse
  ('46e49da5-a18a-4b83-87ac-8b7477f846bb', '0bf0de2e-e57d-4ef8-8473-99ccfa254d38',
   'warehouse', 'a6c735c1-51df-47a3-aee8-5972edfa3e5b',
   '0b8572c1-f674-46ca-92ba-bc0ebae1c9c4', true, now(), '{}'::jsonb, now()),
  -- storeadmin@ → store_admin @ Laundry Ghar Sector 45
  ('2060e2c0-80fd-4c3b-b8bd-2aa4812e840b', 'a6e82798-3d0d-434d-aca8-780b26d89242',
   'store', '60e5bb20-8e4e-4892-a85e-449402463cf9',
   '03357e78-a53e-4009-938e-f036dc0de3bc', true, now(), '{}'::jsonb, now())
ON CONFLICT (id) DO NOTHING;

COMMIT;

SELECT u.email, u.user_type, m.scope_type, m.is_primary
FROM identity_access.users u
LEFT JOIN identity_access.user_scope_memberships m ON m.user_id = u.id AND m.is_primary
WHERE u.email IN ('warehouse@laundryghar.local', 'storeadmin@laundryghar.local')
ORDER BY u.email;
