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
} as const

export function usePermissions() {
  const user = useAuthStore((s) => s.user)

  const perms = useMemo(() => new Set(user?.permissions ?? []), [user?.permissions])
  const isSuper = perms.has('*') || user?.user_type === 'platform_admin'

  function can(permission: string): boolean {
    return isSuper || perms.has(permission)
  }

  return { can, isSuper }
}
