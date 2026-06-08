import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { X, Store, Users, Bike, Loader2, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/utils'
import { TEAM_LABEL, type TeamScope } from '@/api/franchiseTeam'
import { useTeamInfinite } from '@/hooks/useFranchiseTeam'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { TeamRowItem } from './FranchiseTeamShared'

const SCOPE_ICON: Record<TeamScope, React.ElementType> = { stores: Store, staff: Users, riders: Bike }
const SCOPES: TeamScope[] = ['stores', 'staff', 'riders']

interface Props {
  open: boolean
  franchiseId: string | null
  franchiseName: string
  scope: TeamScope
  onClose: () => void
}

/** Right-side drawer showing a franchise's stores / staff / riders with infinite scroll. */
export function FranchiseTeamDrawer({ open, franchiseId, franchiseName, scope: initialScope, onClose }: Props) {
  const [scope, setScope] = useState<TeamScope>(initialScope)
  const navigate = useNavigate()
  useEffect(() => {
    if (open) setScope(initialScope)
  }, [open, initialScope])

  // Row → its detail/management screen. Only riders have a full detail+edit
  // drawer today (deep-linked); stores & staff land on their list pages.
  const goToDetail = (id: string) => {
    onClose()
    if (scope === 'riders') navigate(`/riders?rider=${id}`)
    else if (scope === 'stores') navigate('/tenancy')
    else navigate('/access-control?tab=people')
  }

  const q = useTeamInfinite(scope, franchiseId ?? '', open && !!franchiseId)
  const sentinelRef = useInfiniteScroll({
    hasNextPage: q.hasNextPage,
    isFetchingNextPage: q.isFetchingNextPage,
    fetchNextPage: q.fetchNextPage,
  })

  if (!open || !franchiseId) return null

  const rows = q.data?.pages.flatMap((p) => p.list) ?? []
  const total = q.data?.pages[0]?.totalCount ?? rows.length
  const Icon = SCOPE_ICON[scope]
  const label = TEAM_LABEL[scope].toLowerCase()

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/30" onClick={onClose}>
      <div className="flex h-full w-full max-w-md flex-col bg-white shadow-2xl" onClick={(e) => e.stopPropagation()}>
        {/* Header */}
        <div className="border-b border-gray-100 px-6 py-5">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <p className="truncate text-xs font-medium text-gray-400">{franchiseName}</p>
              <h2 className="text-xl font-bold text-gray-900">Team</h2>
            </div>
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-700"
            >
              <X className="h-5 w-5" />
            </button>
          </div>
          {/* Scope tabs */}
          <div className="mt-4 flex items-center gap-1">
            {SCOPES.map((s) => {
              const Si = SCOPE_ICON[s]
              return (
                <button
                  key={s}
                  type="button"
                  onClick={() => setScope(s)}
                  className={cn(
                    'inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
                    scope === s ? 'bg-lg-green text-white' : 'text-gray-500 hover:bg-gray-100',
                  )}
                >
                  <Si className="h-3.5 w-3.5" /> {TEAM_LABEL[s]}
                </button>
              )
            })}
          </div>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-4 py-3">
          {q.isLoading ? (
            <div className="flex items-center justify-center py-24 text-gray-400">
              <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading {label}…
            </div>
          ) : q.isError ? (
            <div className="py-24 text-center text-sm text-red-600">Couldn’t load {label}.</div>
          ) : rows.length === 0 ? (
            <div className="py-24 text-center text-sm text-gray-400">No {label} yet.</div>
          ) : (
            <>
              <p className="px-2 pb-1 text-xs text-gray-400">{total} {label}</p>
              <div className="divide-y divide-gray-50">
                {rows.map((r) => (
                  <button
                    key={r.id}
                    type="button"
                    onClick={() => goToDetail(r.id)}
                    className="group flex w-full items-center gap-1 rounded-lg px-2 text-left transition-colors hover:bg-gray-50"
                    title={`Open ${r.title}`}
                  >
                    <div className="min-w-0 flex-1">
                      <TeamRowItem row={r} icon={<Icon className="h-3.5 w-3.5" />} />
                    </div>
                    <ChevronRight className="h-4 w-4 shrink-0 text-gray-300 transition-colors group-hover:text-gray-500" />
                  </button>
                ))}
              </div>
              <div ref={sentinelRef} className="h-1" />
              {q.isFetchingNextPage && (
                <div className="flex items-center justify-center py-3 text-gray-400">
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more…
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  )
}
