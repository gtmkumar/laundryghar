-- Fix the Rider role to least privilege (2026-06-11).
--
-- Before: rider held orders.list + orders.update — broad admin-API grants the
-- rider app never uses (the rider lane is /api/v1/rider/*, gated by RiderOnly).
-- Worse, orders.update would let a rider JWT hit admin endpoints that check it
-- (e.g. invoice generation).
--
-- After: rider holds exactly rider.tasks.read + rider.tasks.update — the new
-- self-scope codes enforced on the rider lane (read = task/assignment/earnings
-- views; update = status, OTP, photos, inspection). Session endpoints
-- (me/duty/location/push-token) stay RiderOnly so revoking task permissions
-- can't brick login or live tracking.
--
-- Mirrors the IdentitySeeder change in the same commit; this patch aligns the
-- live DB (the seeder is add-only and never removes grants).

begin;

-- 1. New self-scope permissions (idempotent).
insert into identity_access.permissions (code, module, action, name, risk_level, is_system, requires_scope, status)
values
  ('rider.tasks.read',   'rider', 'tasks.read',   'View own assigned pickup/delivery tasks',                        'low',    true, true, 'active'),
  ('rider.tasks.update', 'rider', 'tasks.update', 'Progress own assigned tasks (status, OTP, photos, inspection)', 'normal', true, true, 'active')
on conflict (code) do nothing;

-- 2. Grant them to the seeded rider role (idempotent).
insert into identity_access.role_permissions (id, role_id, permission_id, granted_at, created_at)
select gen_random_uuid(), r.id, p.id, now(), now()
from identity_access.roles r
join identity_access.permissions p on p.code in ('rider.tasks.read', 'rider.tasks.update')
where r.code = 'rider' and r.deleted_at is null
  and not exists (
    select 1 from identity_access.role_permissions rp
    where rp.role_id = r.id and rp.permission_id = p.id);

-- 3. Drop the over-broad order grants from rider.
delete from identity_access.role_permissions rp
using identity_access.roles r, identity_access.permissions p
where rp.role_id = r.id and rp.permission_id = p.id
  and r.code = 'rider'
  and p.code in ('orders.list', 'orders.update');

commit;
