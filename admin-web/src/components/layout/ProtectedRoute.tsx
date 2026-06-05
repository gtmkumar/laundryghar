import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuthStore } from '@/stores/authStore'

/**
 * Wraps protected routes. Redirects to /login if there is no access token,
 * preserving the original destination so the user is returned there after login.
 *
 * NOTE: Token expiry is handled by the axios interceptor (401 → refresh → retry).
 * This guard only checks whether a token exists at all.
 */
export function ProtectedRoute() {
  const { accessToken } = useAuthStore()
  const location = useLocation()

  if (!accessToken) {
    return <Navigate to="/login" state={{ from: location }} replace />
  }

  return <Outlet />
}
