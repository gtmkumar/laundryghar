/**
 * Canonical route → required-permission map for client-side route gating.
 *
 * This mirrors the server's `identity_access.modules` table (route +
 * required_permission columns), seeded by `db/patches/seed_navigator_modules.sql`,
 * `seed_subscriptions_modules.sql`, `seed_royalty_module.sql`, and
 * `enable_settings_nav.sql`. The backend ALREADY enforces these permissions on
 * the underlying APIs (a forbidden user gets 403s); this map is purely a UX +
 * info-disclosure guard so an unauthorized user who deep-links to a route by URL
 * sees a proper 403 page instead of a blank/exploding screen.
 *
 * Why a static map and not the navigator payload: the GetNavigator DTO
 * (NavItemDto) intentionally omits `requiredPermission` — it only carries the
 * already-filtered list of visible items. So the client cannot derive the
 * route→permission mapping from the navigator response. If the seed table
 * changes, update this map to match (it is the one place that must stay in sync).
 *
 * Entries with `null` are auth-only (no granular permission gate):
 *  - '/'         dashboard — required_permission NULL in the seed
 *  - '/cms'      required_permission NULL in the seed
 *  - '/settings' required_permission NULL — gated by user_type via
 *                useCanManageSettings (platform_admin | brand_admin), NOT a code
 */
export const ROUTE_PERMISSIONS: Record<string, string | null> = {
  '/': null,
  '/tenancy': 'stores.list',
  '/orders': 'orders.list',
  '/warehouse/board': 'garment.read',
  '/customers': 'customer.read',
  '/support': 'support.read',
  '/riders': 'rider.read',
  '/riders/verification': 'rider.read',
  '/riders/payouts': 'rider.read',
  '/riders/incentives': 'rider.read',
  '/catalog': 'pricing.read',
  '/packages': 'packages.manage',
  '/coupons': 'coupons.manage',
  '/promotions': 'promotions.manage',
  '/cms': null,
  '/subscriptions': 'subscription.read',
  '/cashbook': 'cashbook.read',
  '/expenses': 'expense.read',
  '/analytics': 'analytics.read',
  '/access-control': 'users.list',
  '/settings': null,
  '/royalty': 'royalty.read',
  '/platform-plans': 'saas.read',
}

/**
 * Resolve the permission required to view a given pathname. Returns:
 *  - a permission code string when the route is gated,
 *  - null when the route is auth-only (or unmapped — fail-open to auth-only,
 *    since the backend still enforces its own checks and an unmapped route
 *    should never hard-block a legitimately-authed user).
 */
export function requiredPermissionForPath(pathname: string): string | null {
  if (pathname in ROUTE_PERMISSIONS) return ROUTE_PERMISSIONS[pathname]
  // Longest-prefix match for nested routes (e.g. /warehouse/board/...).
  const match = Object.keys(ROUTE_PERMISSIONS)
    .filter((r) => r !== '/' && pathname.startsWith(r + '/'))
    .sort((a, b) => b.length - a.length)[0]
  return match ? ROUTE_PERMISSIONS[match] : null
}
