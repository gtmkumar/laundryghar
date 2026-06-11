import { useEffect, useRef, useState } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuthStore } from '@/stores/authStore'
import { refreshAccessToken } from '@/api/client'

/** Seconds before access-token expiry at which we proactively refresh. */
const REFRESH_SKEW_SECONDS = 120

/** Reads the `exp` (epoch seconds) claim from a JWT without verifying it. */
function readExp(token: string | null): number | null {
  if (!token) return null
  try {
    const payload = token.split('.')[1]
    const claims = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/'))) as { exp?: number }
    return typeof claims.exp === 'number' ? claims.exp : null
  } catch {
    return null
  }
}

/** True when the token is missing, unparseable, or within REFRESH_SKEW of expiry. */
function isExpiringSoon(token: string | null): boolean {
  const exp = readExp(token)
  if (exp === null) return true
  return exp - Date.now() / 1000 < REFRESH_SKEW_SECONDS
}

/**
 * Wraps protected routes.
 *
 * Two responsibilities beyond the original "is there a token" gate:
 *  1. Bootstrap on hard reload — accessToken is restored from localStorage but the
 *     refresh token is NOT (it lives only in the HttpOnly cookie + in-memory). If the
 *     restored access token is expired/near-expiry, we proactively refresh via the
 *     cookie BEFORE rendering protected content, so the first API call isn't a 401.
 *  2. Proactive refresh — when the access token is within 2 min of expiry, refresh
 *     ahead of time so long-idle tabs don't hit a 401 mid-action.
 *
 * On refresh failure we clean-logout and redirect to /login (no spinner hang).
 * Token *expiry mid-session* is still also covered by the 401 interceptor as a backstop.
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

    if (!accessToken || !isExpiringSoon(accessToken)) {
      setPhase('ready')
      return
    }

    // No `cancelled` guard here: under React StrictMode (dev) the effect's
    // cleanup runs between the double-invoked mount, while `ranBootstrap`
    // blocks the second invocation from starting a fresh refresh. A cancelled
    // flag would make the in-flight refresh's handlers no-op, leaving phase
    // stuck on 'checking' forever (hung spinner on any stale/dead session).
    // Setting state after a genuine unmount is a harmless no-op in React 18.
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
      <div className="min-h-screen flex items-center justify-center bg-lg-cream">
        <div className="h-6 w-6 animate-spin rounded-full border-2 border-lg-green border-t-transparent" />
      </div>
    )
  }

  return <Outlet />
}
