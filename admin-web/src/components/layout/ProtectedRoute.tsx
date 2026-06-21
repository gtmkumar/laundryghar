import { useEffect, useRef, useState } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuthStore, isTokenExpired } from '@/stores/authStore'
import { refreshAccessToken } from '@/api/client'

/** Seconds before access-token expiry at which we proactively refresh. */
const REFRESH_SKEW_SECONDS = 120

/**
 * True when the token is missing, unparseable, already expired, or within
 * REFRESH_SKEW of expiry. Delegates the `exp` decode to the shared store helper
 * so there's a single source of truth (an already-past exp is covered too —
 * it satisfies the skew comparison, so we refresh before rendering).
 */
function isExpiringSoon(token: string | null): boolean {
  return isTokenExpired(token, REFRESH_SKEW_SECONDS)
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

  // (Re)establish a session before rendering when the access token is missing OR near
  // expiry. "Missing" matters: a privacy browser (or cleared localStorage) can drop the
  // access token while the HttpOnly refresh cookie survives — a cookie-backed refresh then
  // silently recovers the session instead of bouncing the user to /login.
  const needsRecovery = !accessToken || isExpiringSoon(accessToken)

  // 'checking' while an initial/recovery refresh is in flight.
  const [phase, setPhase] = useState<'checking' | 'ready' | 'failed'>(() =>
    needsRecovery ? 'checking' : 'ready',
  )
  const ranBootstrap = useRef(false)

  useEffect(() => {
    if (ranBootstrap.current) return
    ranBootstrap.current = true

    // Fresh, present token → nothing to do (phase already 'ready').
    if (!needsRecovery) return

    // No `cancelled` guard here: under React StrictMode (dev) the effect's cleanup runs
    // between the double-invoked mount, while `ranBootstrap` blocks the second invocation
    // from starting a fresh refresh. A cancelled flag would strand phase on 'checking'
    // forever. setState after a genuine unmount is a harmless no-op in React 18.
    refreshAccessToken()
      .then(() => setPhase('ready'))
      .catch(() => {
        clearAuth()
        setPhase('failed')
      })
  }, [needsRecovery, clearAuth])

  // Give up only after a recovery attempt has actually failed — not merely because the
  // access token is absent (the cookie refresh above may still rescue the session).
  if (phase === 'failed') {
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
