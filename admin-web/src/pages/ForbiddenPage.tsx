import { Link } from 'react-router-dom'
import { ShieldOff } from 'lucide-react'

/**
 * Full-page 403 shown when a user deep-links (by URL) to a route their role does
 * not grant. Distinct from the inline <ForbiddenState/> (used when a single
 * page-level query 403s) — this is the route-guard surface rendered by
 * <RequirePermission/> when the route's required permission is absent.
 *
 * The backend already enforces the permission on every underlying API, so this
 * is a UX/info-disclosure guard, not a security boundary.
 */
export function ForbiddenPage() {
  return (
    <div className="flex flex-col items-center justify-center min-h-[60vh] gap-5 text-center px-4">
      <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-amber-50 text-amber-500">
        <ShieldOff className="h-8 w-8" />
      </div>
      <div className="max-w-md">
        <h1 className="text-xl font-bold text-gray-900">You don&apos;t have access to this module</h1>
        <p className="mt-2 text-sm text-gray-500">
          Your account doesn&apos;t have permission to view this area. Contact your
          administrator if you believe you should have access.
        </p>
      </div>
      <Link
        to="/"
        className="rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
      >
        Back to dashboard
      </Link>
    </div>
  )
}
