import { ShieldOff } from 'lucide-react'

interface ForbiddenStateProps {
  /** Optional override for the body copy (e.g. name the specific area). */
  message?: string
}

/**
 * Inline "you don't have permission" panel for pages whose primary query 403s.
 * Distinct from <ErrorState/> (a failed load you can retry) — a 403 is not
 * retryable, so this intentionally offers no "Try again" button. Use it where a
 * whole page/tab depends on a permission the signed-in user lacks.
 *
 * For action-level 403s (a button press whose request is forbidden), prefer the
 * toast surfaced automatically by the axios interceptor over rendering this.
 */
export function ForbiddenState({ message }: ForbiddenStateProps) {
  return (
    <div className="flex flex-col items-center justify-center py-16 text-gray-500" role="alert">
      <ShieldOff className="h-8 w-8 text-amber-400 mb-3" />
      <p className="text-sm font-medium text-gray-700 mb-1">You don&apos;t have permission</p>
      <p className="text-xs text-gray-400 max-w-sm text-center">
        {message ?? 'Your account does not have access to this area. Contact an administrator if you believe this is a mistake.'}
      </p>
    </div>
  )
}

/**
 * True when an error from a query/axios call is an HTTP 403. Pages use this to
 * decide between <ForbiddenState/> (403) and <ErrorState/> (anything else).
 */
// Small co-located 403 helper used alongside this component; not worth its own module.
// eslint-disable-next-line react-refresh/only-export-components
export function isForbiddenError(error: unknown): boolean {
  return (
    typeof error === 'object' &&
    error !== null &&
    'response' in error &&
    (error as { response?: { status?: number } }).response?.status === 403
  )
}
