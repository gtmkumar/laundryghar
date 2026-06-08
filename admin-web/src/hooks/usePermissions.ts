import { useMemo } from 'react'
import { useAuthStore } from '@/stores/authStore'

/**
 * Decoded view of the signed-in user's authorization context, sourced from the
 * access-token JWT claims (the same token the axios interceptor decodes for
 * brand_id in api/client.ts).
 *
 * Backend contract (laundryghar.Identity ScopeResolver / PermissionHandler):
 *  - `permissions` is a single space-separated string of BARE permission codes
 *    (e.g. "rider.read rider.manage rider.verify"). It is NOT prefixed with
 *    "permission:" — that prefix only names the server-side authorization policy.
 *    We tolerate a string[] too, in case a future token serializes it as an array.
 *  - `user_type === 'platform_admin'` bypasses every permission check, matching
 *    the server's PermissionHandler.
 *  - franchise-scoped users carry a `franchise_id` claim; platform/brand admins
 *    do not (they operate across franchises).
 */
export interface PermissionContext {
  /** True when the named bare permission code is granted (or the user is a platform admin). */
  hasPermission: (code: string) => boolean
  /** The user's franchise id from the JWT, or null for platform/brand admins. */
  franchiseId: string | null
  /** True for platform_admin — bypasses permission checks entirely. */
  isPlatformAdmin: boolean
  /**
   * True when the user is locked to a single franchise (franchise-scoped) and is
   * therefore NOT a platform admin. Brand admins carry brand_id but no
   * franchise_id, so they are treated as cross-franchise (not scoped).
   */
  isFranchiseScoped: boolean
}

interface DecodedClaims {
  user_type?: string
  franchise_id?: string
  permissions?: string | string[]
}

function decodeClaims(token: string | null): DecodedClaims {
  if (!token) return {}
  try {
    const payload = token.split('.')[1]
    return JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/'))) as DecodedClaims
  } catch {
    return {}
  }
}

export function usePermissions(): PermissionContext {
  const accessToken = useAuthStore((s) => s.accessToken)

  return useMemo(() => {
    const claims = decodeClaims(accessToken)
    const isPlatformAdmin = claims.user_type === 'platform_admin'
    const franchiseId = claims.franchise_id ?? null

    const granted = new Set(
      (Array.isArray(claims.permissions)
        ? claims.permissions
        : (claims.permissions ?? '').split(' ')
      )
        .map((p) => p.trim())
        .filter(Boolean),
    )

    return {
      isPlatformAdmin,
      franchiseId,
      isFranchiseScoped: !isPlatformAdmin && franchiseId !== null,
      hasPermission: (code: string) => isPlatformAdmin || granted.has(code),
    }
  }, [accessToken])
}
