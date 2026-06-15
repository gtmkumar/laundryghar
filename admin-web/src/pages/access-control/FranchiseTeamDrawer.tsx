import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Store, Users, Bike, Loader2, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/utils'
import { FormDrawer } from '@/components/shared/FormDrawer'
import { TEAM_LABEL, type TeamScope, type TeamRow } from '@/api/franchiseTeam'
import { useTeamInfinite } from '@/hooks/useFranchiseTeam'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { TeamRowItem } from './FranchiseTeamShared'
import { PersonDetailDrawer, type PersonSummary } from './PersonDetailDrawer'

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
  const [person, setPerson] = useState<PersonSummary | null>(null)
  const navigate = useNavigate()
  // Re-seed the scope tab from the prop while open (mirrors the prior effect's
  // [open, initialScope] deps): on open, or when the requested scope changes.
  const [seed, setSeed] = useState(`${open}|${initialScope}`)
  const nextSeed = `${open}|${initialScope}`
  if (seed !== nextSeed) {
    setSeed(nextSeed)
    if (open) setScope(initialScope)
  }

  // Row click → the right detail surface. Staff opens the person drawer in place;
  // riders deep-link to their full detail; stores land on the Stores list.
  const openRow = (r: TeamRow) => {
    if (scope === 'staff') {
      setPerson({ id: r.id, name: r.title, roleName: r.subtitle, status: r.status, initials: r.initials ?? undefined })
      return
    }
    onClose()
    if (scope === 'riders') navigate(`/riders?rider=${r.id}`)
    else navigate('/tenancy')
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
    <>
      <FormDrawer
        open={open}
        onClose={onClose}
        width="sm"
        eyebrow={franchiseName}
        title="Team"
        bodyClassName="flex-1 overflow-y-auto px-4 py-3"
        footer={null}
        headerExtra={
          <div className="flex items-center gap-1">
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
        }
      >
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
                  onClick={() => openRow(r)}
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
      </FormDrawer>

      {/* Staff row → person detail/edit drawer, layered above this drawer */}
      <PersonDetailDrawer person={person} open={person !== null} onClose={() => setPerson(null)} />
    </>
  )
}
