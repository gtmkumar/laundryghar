import type { ReactNode } from 'react'
import { Inbox } from 'lucide-react'

interface EmptyStateProps {
  /** Optional icon override; defaults to an inbox. */
  icon?: ReactNode
  title: string
  hint?: string
  /** Compact variant for inline lists vs. full-screen panels. */
  compact?: boolean
}

/**
 * Neutral "nothing here yet" placeholder. POS-7: catalog levels (categories /
 * services / items) and the no-store case need a clear empty state instead of a
 * blank gap, so staff can tell "loaded, but empty" apart from "still loading".
 */
export function EmptyState({ icon, title, hint, compact = false }: EmptyStateProps) {
  return (
    <div
      className={`flex flex-col items-center justify-center text-center gap-2 ${
        compact ? 'py-6' : 'py-12'
      }`}
    >
      <div className="text-gray-300">{icon ?? <Inbox className="h-8 w-8" />}</div>
      <p className="text-sm font-medium text-gray-600">{title}</p>
      {hint && <p className="text-xs text-gray-400 max-w-xs">{hint}</p>}
    </div>
  )
}
