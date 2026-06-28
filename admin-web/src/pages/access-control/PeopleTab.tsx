import { useState } from 'react'
import { Loader2, ChevronUp, ChevronDown, ChevronsUpDown } from 'lucide-react'
import { cn } from '@/lib/utils'
import { userTypeLabel } from '@/types/userType'
import type { AccessPeoplePage, AccessPerson, AccessPeopleCounts } from '@/types/api'
import { PersonRowActions } from './PersonRowActions'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { PersonDetailDrawer, type PersonSummary } from './PersonDetailDrawer'

interface Props {
  query: {
    data?: { pages: AccessPeoplePage[] }
    isLoading: boolean
    isError: boolean
    error?: unknown
    hasNextPage: boolean
    isFetchingNextPage: boolean
    fetchNextPage: () => void
  }
  sort?: string
  onSort?: (key: string) => void
}

const EMPTY_COUNTS: AccessPeopleCounts = { all: 0, hqEmployees: 0, franchiseOwners: 0, franchiseStaff: 0 }

type ChipKey = 'all' | 'hq' | 'owners' | 'staff'

// Soft badge palette keyed by role code (falls back to slate).
const ROLE_BADGE: Record<string, string> = {
  platform_admin: 'bg-violet-100 text-violet-700',
  brand_admin: 'bg-violet-100 text-violet-700',
  operations_manager: 'bg-emerald-100 text-emerald-700',
  regional_manager: 'bg-emerald-100 text-emerald-700',
  finance_manager: 'bg-sky-100 text-sky-700',
  catalogue_manager: 'bg-amber-100 text-amber-700',
  support_lead: 'bg-rose-100 text-rose-700',
  auditor: 'bg-slate-100 text-slate-600',
  franchise_owner: 'bg-lg-green/12 text-lg-green',
  store_admin: 'bg-teal-100 text-teal-700',
  store_staff: 'bg-teal-50 text-teal-600',
  warehouse_supervisor: 'bg-indigo-100 text-indigo-700',
  warehouse_staff: 'bg-indigo-50 text-indigo-600',
  // Vertical-neutral on-site processing staff (salon/logistics) — same family as warehouse_staff.
  ops_staff: 'bg-indigo-100 text-indigo-700',
  rider: 'bg-orange-100 text-orange-700',
}

const AVATAR_BG = [
  'bg-violet-500', 'bg-emerald-500', 'bg-sky-500', 'bg-amber-500',
  'bg-rose-500', 'bg-teal-500', 'bg-indigo-500', 'bg-orange-500',
]

function timeAgo(iso: string | null): string {
  if (!iso) return '—'
  const mins = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000))
  if (mins < 1) return 'now'
  if (mins < 60) return `${mins}m ago`
  const hrs = Math.floor(mins / 60)
  if (hrs < 24) return `${hrs}h ago`
  const days = Math.floor(hrs / 24)
  return days < 30 ? `${days}d ago` : new Date(iso).toLocaleDateString()
}

function avatarColor(name: string) {
  let h = 0
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0
  return AVATAR_BG[h % AVATAR_BG.length]
}

function matchesChip(p: AccessPerson, chip: ChipKey): boolean {
  switch (chip) {
    case 'hq': return p.tier === 'enterprise'
    case 'owners': return p.roleCode === 'franchise_owner'
    case 'staff': return p.tier === 'franchise' && p.roleCode !== 'franchise_owner'
    default: return true
  }
}

export function PeopleTab({ query, sort, onSort }: Props) {
  const [chip, setChip] = useState<ChipKey>('all')
  const [selected, setSelected] = useState<PersonSummary | null>(null)
  const { data, isLoading, isError, error, hasNextPage, isFetchingNextPage, fetchNextPage } = query

  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-24 text-gray-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading people…
      </div>
    )
  }
  if (isError || !data) {
    if (isForbiddenError(error)) return <ForbiddenState message="You don’t have access to the people directory." />
    return <div className="py-24 text-center text-sm text-red-600">Couldn’t load people.</div>
  }

  const counts = data.pages[0]?.counts ?? EMPTY_COUNTS
  const people = data.pages.flatMap((p) => p.people.list)

  const chips: { key: ChipKey; label: string; count: number }[] = [
    { key: 'all', label: 'All', count: counts.all },
    { key: 'hq', label: 'HQ employees', count: counts.hqEmployees },
    { key: 'owners', label: 'Franchise owners', count: counts.franchiseOwners },
    { key: 'staff', label: 'Franchise staff', count: counts.franchiseStaff },
  ]
  const rows = people.filter((p) => matchesChip(p, chip))

  return (
    <div className="space-y-4">
      {/* Filter chips */}
      <div className="flex flex-wrap items-center gap-2">
        {chips.map((c) => (
          <button
            key={c.key}
            type="button"
            onClick={() => setChip(c.key)}
            className={cn(
              'inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-sm font-medium transition-colors',
              chip === c.key
                ? 'bg-lg-green text-white'
                : 'bg-white border border-gray-200 text-gray-600 hover:bg-gray-50',
            )}
          >
            {c.label}
            <span className={cn('text-xs', chip === c.key ? 'text-white/80' : 'text-gray-400')}>{c.count}</span>
          </button>
        ))}
      </div>

      {/* Table */}
      <div className="overflow-hidden rounded-2xl border border-gray-200 bg-white">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-100 text-left text-[11px] font-semibold uppercase tracking-wide text-gray-400">
              <SortTh label="Name" sortKey="name" sort={sort} onSort={onSort} />
              <SortTh label="Role" sortKey="role" sort={sort} onSort={onSort} />
              <th className="px-5 py-3">Scope</th>
              <th className="px-5 py-3">Type</th>
              <th className="px-5 py-3">Status</th>
              <SortTh label="Last active" sortKey="active" sort={sort} onSort={onSort} align="right" />
              <th className="w-12 px-5 py-3"><span className="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            {rows.map((p) => (
              <tr
                key={p.id}
                onClick={() => setSelected(toSummary(p))}
                className="cursor-pointer border-b border-gray-50 last:border-0 hover:bg-gray-50/60"
              >
                <td className="px-5 py-3">
                  <div className="flex items-center gap-3">
                    <span className={cn('flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-xs font-semibold text-white', avatarColor(p.name))}>
                      {p.initials}
                    </span>
                    <div className="min-w-0">
                      <p className="truncate font-medium text-gray-900">{p.name}</p>
                      <p className="truncate text-xs text-gray-400">{p.email}</p>
                    </div>
                  </div>
                </td>
                <td className="px-5 py-3">
                  <span className={cn('inline-block rounded-full px-2.5 py-1 text-xs font-medium', ROLE_BADGE[p.roleCode] ?? 'bg-slate-100 text-slate-600')}>
                    {p.roleName}
                  </span>
                </td>
                <td className="px-5 py-3 text-gray-600">{p.scopeLabel}</td>
                <td className="px-5 py-3 text-gray-500">{userTypeLabel(p.userType)}</td>
                <td className="px-5 py-3">
                  <span className="inline-flex items-center gap-1.5 text-xs font-medium capitalize">
                    <span className={cn('h-1.5 w-1.5 rounded-full', p.status === 'active' ? 'bg-emerald-500' : p.status === 'invited' ? 'bg-amber-500' : 'bg-gray-300')} />
                    <span className={p.status === 'active' ? 'text-emerald-700' : p.status === 'invited' ? 'text-amber-700' : 'text-gray-500'}>{p.status}</span>
                  </span>
                </td>
                <td className="px-5 py-3 text-right text-gray-400">{timeAgo(p.lastActiveAt)}</td>
                <td className="px-5 py-3 text-right" onClick={(e) => e.stopPropagation()}><PersonRowActions person={p} /></td>
              </tr>
            ))}
            {rows.length === 0 && (
              <tr><td colSpan={7} className="px-5 py-12 text-center text-sm text-gray-400">No people match this filter.</td></tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Infinite-scroll sentinel + loading-more indicator */}
      <div ref={sentinelRef} className="h-1" />
      {isFetchingNextPage && (
        <div className="flex items-center justify-center py-4 text-gray-400">
          <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more…
        </div>
      )}

      <PersonDetailDrawer person={selected} open={selected !== null} onClose={() => setSelected(null)} />
    </div>
  )
}

function toSummary(p: AccessPerson): PersonSummary {
  return { id: p.id, name: p.name, roleName: p.roleName, scopeLabel: p.scopeLabel, status: p.status, initials: p.initials }
}

/** Sortable column header. Clicking toggles asc → desc (key ↔ -key) via onSort. */
function SortTh({
  label,
  sortKey,
  sort,
  onSort,
  align = 'left',
}: {
  label: string
  sortKey: string
  sort?: string
  onSort?: (key: string) => void
  align?: 'left' | 'right'
}) {
  if (!onSort) return <th className={cn('px-5 py-3', align === 'right' && 'text-right')}>{label}</th>
  const active = sort === sortKey || sort === `-${sortKey}`
  const desc = sort === `-${sortKey}`
  return (
    <th className={cn('px-5 py-3', align === 'right' && 'text-right')}>
      <button
        type="button"
        onClick={() => onSort(sortKey)}
        className={cn(
          'inline-flex items-center gap-1 font-semibold uppercase tracking-wide transition-colors hover:text-gray-600',
          align === 'right' && 'flex-row-reverse',
          active ? 'text-gray-700' : 'text-gray-400',
        )}
      >
        {label}
        {active ? (
          desc ? <ChevronDown className="h-3 w-3" /> : <ChevronUp className="h-3 w-3" />
        ) : (
          <ChevronsUpDown className="h-3 w-3 opacity-50" />
        )}
      </button>
    </th>
  )
}
