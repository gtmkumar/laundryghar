/**
 * Friendly 403 panel (R3-NAV-1). Shown when a staff member reaches a POS route
 * their token doesn't grant — instead of an opaque backend bounce. This is a UX
 * guard only; the backend remains the source of truth and still enforces the
 * permission on every endpoint.
 */
import { ShieldAlert } from 'lucide-react'

interface ForbiddenStateProps {
  /** Short label for the area the user can't access, e.g. "the cash book". */
  area?: string
}

export function ForbiddenState({ area }: ForbiddenStateProps) {
  return (
    <div className="flex flex-col items-center justify-center py-20 px-4 gap-3 text-center">
      <div className="w-16 h-16 rounded-2xl bg-amber-50 flex items-center justify-center">
        <ShieldAlert className="h-8 w-8 text-amber-500" />
      </div>
      <h1 className="text-lg font-bold text-gray-900">Access not allowed</h1>
      <p className="max-w-sm text-sm text-gray-500">
        Your counter login doesn't have permission to use
        {area ? ` ${area}` : ' this screen'}. Ask a store admin to grant it, then
        sign in again.
      </p>
    </div>
  )
}
