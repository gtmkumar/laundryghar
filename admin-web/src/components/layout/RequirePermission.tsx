import { Outlet, useLocation } from 'react-router-dom'
import { usePermissions, useCanManageSettings } from '@/hooks/usePermissions'
import { requiredPermissionForPath } from '@/lib/routePermissions'
import { ForbiddenPage } from '@/pages/ForbiddenPage'

/**
 * Route-level permission gate. Mounted as a layout route around the protected
 * pages: it resolves the permission required by the current pathname (from the
 * canonical {@link requiredPermissionForPath} map, which mirrors the server's
 * modules table) and renders a 403 page when the signed-in user lacks it,
 * instead of letting the page mount and fire forbidden API calls.
 *
 * Auth-only routes (map value `null`) always render. The /settings tree is a
 * special case: its module has no granular permission code — it is gated by
 * user_type (platform_admin | brand_admin), so we use the same predicate the
 * settings panels use (useCanManageSettings) to decide visibility.
 *
 * The backend still enforces every permission on its APIs; this guard only
 * upgrades the UX from an opaque/exploding page to a clear 403.
 */
export function RequirePermission() {
  const location = useLocation()
  const { hasPermission } = usePermissions()
  const canManageSettings = useCanManageSettings()

  const required = requiredPermissionForPath(location.pathname)

  // /settings has no permission code — gate it by user_type instead.
  if (location.pathname === '/settings' || location.pathname.startsWith('/settings/')) {
    return canManageSettings ? <Outlet /> : <ForbiddenPage />
  }

  if (required && !hasPermission(required)) {
    return <ForbiddenPage />
  }

  return <Outlet />
}
