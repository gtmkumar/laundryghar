import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Loader2 } from 'lucide-react'
import { cn } from '@/lib/utils'
import { TEAM_LABEL, type TeamScope } from '@/api/franchiseTeam'
import { useTeamPreview } from '@/hooks/useFranchiseTeam'
import { TeamRowItem } from './FranchiseTeamShared'

interface Props {
  icon: React.ElementType
  value: number
  scope: TeamScope
  franchiseId: string
  /** Fallback avatar glyph for rows without initials (e.g. stores). */
  rowIcon: React.ReactNode
  onOpen: (scope: TeamScope) => void
}

/**
 * A franchise stat tile (stores / staff / riders) that:
 *  - opens a lightweight hovercard PREVIEW (first 5 + count) on hover/focus, and
 *  - opens the full drawer on click.
 * The hovercard renders in a portal with collision-aware placement (flips above
 * when there's no room below, clamps to the viewport horizontally).
 */
export function FranchiseStatTile({ icon: Icon, value, scope, franchiseId, rowIcon, onOpen }: Props) {
  const [open, setOpen] = useState(false)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const openTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const closeTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const scheduleOpen = () => {
    if (value <= 0) return
    if (closeTimer.current) clearTimeout(closeTimer.current)
    if (openTimer.current) clearTimeout(openTimer.current)
    openTimer.current = setTimeout(() => setOpen(true), 250)
  }
  const scheduleClose = () => {
    if (openTimer.current) clearTimeout(openTimer.current)
    if (closeTimer.current) clearTimeout(closeTimer.current)
    closeTimer.current = setTimeout(() => setOpen(false), 120)
  }
  const cancelClose = () => {
    if (closeTimer.current) clearTimeout(closeTimer.current)
  }

  useEffect(
    () => () => {
      if (openTimer.current) clearTimeout(openTimer.current)
      if (closeTimer.current) clearTimeout(closeTimer.current)
    },
    [],
  )

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        onClick={() => value > 0 && onOpen(scope)}
        onMouseEnter={scheduleOpen}
        onMouseLeave={scheduleClose}
        onFocus={scheduleOpen}
        onBlur={scheduleClose}
        disabled={value <= 0}
        className={cn(
          'w-full rounded-xl border border-gray-100 py-2 transition-colors',
          value > 0 ? 'cursor-pointer hover:border-lg-green/40 hover:bg-lg-green/5' : 'cursor-default',
        )}
      >
        <Icon className="mx-auto mb-0.5 h-3.5 w-3.5 text-gray-400" />
        <p className="text-base font-bold leading-none text-gray-900">{value}</p>
        <p className="text-[11px] capitalize text-gray-400">{scope}</p>
      </button>

      {open && value > 0 && (
        <HoverCard triggerRef={triggerRef} onMouseEnter={cancelClose} onMouseLeave={scheduleClose}>
          <PreviewCard
            scope={scope}
            franchiseId={franchiseId}
            rowIcon={rowIcon}
            onViewAll={() => {
              setOpen(false)
              onOpen(scope)
            }}
          />
        </HoverCard>
      )}
    </>
  )
}

/**
 * Floating container: portals to <body> and positions itself in viewport
 * coordinates relative to the trigger — preferring below, flipping above when
 * it would overflow the bottom, and clamping within the left/right edges.
 * Re-measures on content resize, scroll and window resize.
 */
function HoverCard({
  triggerRef,
  onMouseEnter,
  onMouseLeave,
  children,
}: {
  triggerRef: React.RefObject<HTMLElement | null>
  onMouseEnter: () => void
  onMouseLeave: () => void
  children: React.ReactNode
}) {
  const cardRef = useRef<HTMLDivElement>(null)
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null)

  useLayoutEffect(() => {
    const reposition = () => {
      const trigger = triggerRef.current
      const card = cardRef.current
      if (!trigger || !card) return
      const t = trigger.getBoundingClientRect()
      const c = card.getBoundingClientRect()
      const gap = 8
      const margin = 8

      // Vertical: prefer below; flip above if it would overflow the viewport bottom.
      let top = t.bottom + gap
      if (top + c.height > window.innerHeight - margin) {
        const above = t.top - c.height - gap
        top = above >= margin ? above : Math.max(margin, window.innerHeight - c.height - margin)
      }

      // Horizontal: centre on the trigger, then clamp to the viewport.
      let left = t.left + t.width / 2 - c.width / 2
      left = Math.min(Math.max(margin, left), window.innerWidth - c.width - margin)

      setPos({ top, left })
    }

    reposition()
    const ro = new ResizeObserver(reposition)
    if (cardRef.current) ro.observe(cardRef.current)
    window.addEventListener('scroll', reposition, true)
    window.addEventListener('resize', reposition)
    return () => {
      ro.disconnect()
      window.removeEventListener('scroll', reposition, true)
      window.removeEventListener('resize', reposition)
    }
  }, [triggerRef])

  return createPortal(
    <div
      ref={cardRef}
      onMouseEnter={onMouseEnter}
      onMouseLeave={onMouseLeave}
      style={{
        position: 'fixed',
        top: pos?.top ?? 0,
        left: pos?.left ?? 0,
        zIndex: 60,
        visibility: pos ? 'visible' : 'hidden',
      }}
    >
      {children}
    </div>,
    document.body,
  )
}

function PreviewCard({
  scope,
  franchiseId,
  rowIcon,
  onViewAll,
}: {
  scope: TeamScope
  franchiseId: string
  rowIcon: React.ReactNode
  onViewAll: () => void
}) {
  const { data, isLoading } = useTeamPreview(scope, franchiseId, true)
  const rows = data?.list ?? []
  const total = data?.totalCount ?? rows.length

  return (
    <div className="w-64 rounded-xl border border-gray-200 bg-white p-2 text-left shadow-xl">
      <div className="flex items-center justify-between px-1.5 pb-1.5">
        <span className="text-xs font-semibold text-gray-700">{TEAM_LABEL[scope]}</span>
        <span className="text-xs text-gray-400">{total}</span>
      </div>
      {isLoading ? (
        <div className="flex items-center justify-center py-5 text-gray-400">
          <Loader2 className="h-4 w-4 animate-spin" />
        </div>
      ) : rows.length === 0 ? (
        <p className="px-1.5 py-3 text-center text-xs text-gray-400">Nothing here yet.</p>
      ) : (
        <div className="divide-y divide-gray-50 px-1.5">
          {rows.map((r) => (
            <TeamRowItem key={r.id} row={r} icon={rowIcon} />
          ))}
        </div>
      )}
      <button
        type="button"
        onClick={onViewAll}
        className="mt-1.5 w-full rounded-lg bg-gray-50 px-2 py-1.5 text-xs font-semibold text-lg-green hover:bg-gray-100"
      >
        View all {total} →
      </button>
    </div>
  )
}
