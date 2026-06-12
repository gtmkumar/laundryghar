/**
 * Reads the `permissions` claim out of the active JWT and exposes a `can()`
 * predicate for client-side gating.
 *
 * WEB-3 (POS side): privileged counter actions — creating a customer and
 * recording a payment — must be hidden/disabled when the staff member lacks the
 * backing permission, so they don't submit and bounce off an opaque 403. This is
 * a UX guard only; the backend remains the source of truth and still enforces
 * the permission. A platform/super admin (or any user whose token carries `*`)
 * is treated as holding every permission.
 *
 * The permissions array already lives on the parsed JWT in authStore (normalized
 * from the backend's space-separated claim there), so this is a thin selector —
 * no extra network call.
 *
 * Superuser handling: a token carrying `*`, OR a platform_admin (whose token
 * does NOT carry `*` but bypasses authorization server-side via user_type), is
 * treated as holding every permission — otherwise the client would gate them out
 * of controls the backend would actually allow.
 */
import { useMemo } from 'react'
import { useAuthStore } from '@/stores/authStore'

export const PERMISSIONS = {
  customerCreate: 'customer.create',
  paymentRecord: 'payment.record',
  // R3-NAV-1: gating order-create and cash-book management at the counter.
  // TODO(R3-SEC-2): backend is adding a dedicated `pos.order.create` / `pos.order.read`
  // family so POS is lockable independently of the admin Orders module. Until it
  // lands and is seeded, accept EITHER the legacy `orders.create` OR the new
  // `pos.order.create` (see canCreateOrder below) so this gate works against
  // whichever code the live token carries.
  ordersCreate: 'orders.create',
  posOrderCreate: 'pos.order.create',
  cashbookManage: 'cashbook.manage',
} as const

export function usePermissions() {
  const user = useAuthStore((s) => s.user)

  const perms = useMemo(() => new Set(user?.permissions ?? []), [user?.permissions])
  const isSuper = perms.has('*') || user?.user_type === 'platform_admin'

  function can(permission: string): boolean {
    return isSuper || perms.has(permission)
  }

  function canAny(...permissions: string[]): boolean {
    return isSuper || permissions.some((p) => perms.has(p))
  }

  // R3-NAV-1 derived gates. Order creation accepts either the legacy Orders code
  // or the incoming POS-specific code so the screen keeps working through the
  // backend cut-over.
  const canCreateOrder = canAny(PERMISSIONS.ordersCreate, PERMISSIONS.posOrderCreate)
  // Viewing the orders list / detail (reprint a past receipt) follows the same
  // family — read is implied by create at the counter.
  const canViewOrders = canAny(
    PERMISSIONS.ordersCreate,
    PERMISSIONS.posOrderCreate,
    'orders.read',
    'pos.order.read',
  )
  const canManageCashbook = can(PERMISSIONS.cashbookManage)

  return { can, canAny, isSuper, canCreateOrder, canViewOrders, canManageCashbook }
}
