import { useEffect, useRef, useState } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuthStore, isTokenExpired } from '@/stores/authStore'
import { refreshAccessToken } from '@/api/client'

/** Seconds before access-token expiry at which we proactively refresh. */
const REFRESH_SKEW_SECONDS = 120

/**
 * True when the token is missing, unparseable, already expired, or within
 * REFRESH_SKEW of expiry. Delegates the `exp` decode to the shared store helper
 * so there's a single source of truth (an already-past exp is covered too — it
 * satisfies the skew comparison, so we refresh before rendering).
 */
function isExpiringSoon(token: string | null): boolean {
  return isTokenExpired(token, REFRESH_SKEW_SECONDS)
}

/**
 * Wraps protected routes.
 *
 * Two responsibilities beyond the original "is there a token" gate (POS-2):
 *  1. Bootstrap on hard reload — accessToken is restored from localStorage but the
 *     refresh token is NOT (it lives only in the HttpOnly cookie + in-memory). If the
 *     restored access token is expired/near-expiry, we proactively refresh via the
 *     cookie BEFORE rendering protected content, so the first counter API call isn't a 401.
 *  2. Proactive refresh — when the access token is within 2 min of expiry, refresh
 *     ahead of time so a long-idle tablet doesn't hit a 401 mid-action.
 *
 * On refresh failure we clean-logout and redirect to /login (no spinner hang).
 * Token *expiry mid-session* is still also covered by the 401 interceptor as a backstop.
 * Mirrors admin-web/src/components/layout/ProtectedRoute.tsx.
 */
export function ProtectedRoute() {
  const accessToken = useAuthStore((s) => s.accessToken)
  const clearAuth = useAuthStore((s) => s.clearAuth)
  const location = useLocation()

  // 'checking' only while an initial cookie-refresh is in flight for a stale token.
  const [phase, setPhase] = useState<'checking' | 'ready' | 'failed'>(() =>
    accessToken && isExpiringSoon(accessToken) ? 'checking' : 'ready',
  )
  const ranBootstrap = useRef(false)

  useEffect(() => {
    if (ranBootstrap.current) return
    ranBootstrap.current = true

    // When there's no token or it's fresh, `phase` was already initialized to
    // 'ready' (see useState initializer) — nothing to sync, just bail. (Avoids a
    // redundant synchronous setState in the effect body.)
    if (!accessToken || !isExpiringSoon(accessToken)) return

    // No `cancelled` guard here: under React StrictMode (dev) the effect's
    // cleanup runs between the double-invoked mount, while `ranBootstrap`
    // blocks the second invocation from starting a fresh refresh. A cancelled
    // flag would make the in-flight refresh's handlers no-op, leaving phase
    // stuck on 'checking' forever (hung spinner on any stale/dead session).
    refreshAccessToken()
      .then(() => setPhase('ready'))
      .catch(() => {
        clearAuth()
        setPhase('failed')
      })
  }, [accessToken, clearAuth])

  // No session at all → straight to login (preserve intended destination).
  if (!accessToken || phase === 'failed') {
    return <Navigate to="/login" state={{ from: location }} replace />
  }

  if (phase === 'checking') {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-50">
        <div className="h-6 w-6 animate-spin rounded-full border-2 border-blue-600 border-t-transparent" />
      </div>
    )
  }

  return <Outlet />
}
