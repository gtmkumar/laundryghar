/**
 * Route-level permission gate (R3-NAV-1). Renders the child route only when the
 * `allowed` predicate passes; otherwise shows the friendly 403 panel instead of
 * letting the user mount a screen whose every action the backend would reject.
 *
 * Pairs with the nav, which hides entries the user can't use — this wrapper is
 * the defense for someone who reaches the URL directly (typed/bookmarked).
 */
import { Outlet } from 'react-router-dom'
import { ForbiddenState } from '@/components/shared/ForbiddenState'

interface RequirePermissionProps {
  allowed: boolean
  /** Area label surfaced in the 403 copy, e.g. "the cash book". */
  area?: string
}

export function RequirePermission({ allowed, area }: RequirePermissionProps) {
  if (!allowed) return <ForbiddenState area={area} />
  return <Outlet />
}
