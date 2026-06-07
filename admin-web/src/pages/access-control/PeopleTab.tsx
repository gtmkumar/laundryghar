import { useState } from 'react'
import { Loader2 } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { AccessPeople, AccessPerson } from '@/types/api'

interface Props {
  query: { data?: AccessPeople; isLoading: boolean; isError: boolean }
}

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

export function PeopleTab({ query }: Props) {
  const [chip, setChip] = useState<ChipKey>('all')
  const { data, isLoading, isError } = query

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-24 text-gray-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading people…
      </div>
    )
  }
  if (isError || !data) {
    return <div className="py-24 text-center text-sm text-red-600">Couldn’t load people.</div>
  }

  const chips: { key: ChipKey; label: string; count: number }[] = [
    { key: 'all', label: 'All', count: data.counts.all },
    { key: 'hq', label: 'HQ employees', count: data.counts.hqEmployees },
    { key: 'owners', label: 'Franchise owners', count: data.counts.franchiseOwners },
    { key: 'staff', label: 'Franchise staff', count: data.counts.franchiseStaff },
  ]
  const rows = data.people.filter((p) => matchesChip(p, chip))

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
              <th className="px-5 py-3">Name</th>
              <th className="px-5 py-3">Role</th>
              <th className="px-5 py-3">Scope</th>
              <th className="px-5 py-3">Type</th>
              <th className="px-5 py-3">Status</th>
              <th className="px-5 py-3 text-right">Last active</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((p) => (
              <tr key={p.id} className="border-b border-gray-50 last:border-0 hover:bg-gray-50/60">
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
                <td className="px-5 py-3 text-gray-500 capitalize">{p.tier}</td>
                <td className="px-5 py-3">
                  <span className="inline-flex items-center gap-1.5 text-xs font-medium capitalize">
                    <span className={cn('h-1.5 w-1.5 rounded-full', p.status === 'active' ? 'bg-emerald-500' : p.status === 'invited' ? 'bg-amber-500' : 'bg-gray-300')} />
                    <span className={p.status === 'active' ? 'text-emerald-700' : p.status === 'invited' ? 'text-amber-700' : 'text-gray-500'}>{p.status}</span>
                  </span>
                </td>
                <td className="px-5 py-3 text-right text-gray-400">{timeAgo(p.lastActiveAt)}</td>
              </tr>
            ))}
            {rows.length === 0 && (
              <tr><td colSpan={6} className="px-5 py-12 text-center text-sm text-gray-400">No people match this filter.</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
