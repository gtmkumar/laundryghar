import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Search, UserPlus } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useAccessPeople, useAccessRoles, useAccessFranchises } from '@/hooks/useAccessControl'
import { usePermissions } from '@/hooks/usePermissions'
import { PeopleTab } from './PeopleTab'
import { RolesTab } from './RolesTab'
import { FranchisesTab } from './FranchisesTab'
import { InviteUserModal } from './InviteUserModal'

type TabKey = 'people' | 'roles' | 'franchises'

const TABS: { key: TabKey; label: string }[] = [
  { key: 'people', label: 'People' },
  { key: 'roles', label: 'Roles & Permissions' },
  { key: 'franchises', label: 'Franchises' },
]

export function AccessControlPage() {
  const [params, setParams] = useSearchParams()
  const tab = (params.get('tab') as TabKey) || 'people'
  const setTab = (t: TabKey) => setParams(t === 'people' ? {} : { tab: t }, { replace: true })

  const { hasPermission } = usePermissions()
  const canInvite = hasPermission('users.create')

  const [search, setSearch] = useState('')
  const [inviteOpen, setInviteOpen] = useState(false)

  const [peopleSort, setPeopleSort] = useState<string | undefined>(undefined)
  const toggleSort = (key: string) =>
    setPeopleSort((cur) => (cur === key ? `-${key}` : cur === `-${key}` ? undefined : key))

  const term = search.trim() || undefined
  const people = useAccessPeople(term, peopleSort)
  const roles = useAccessRoles()
  const franchises = useAccessFranchises(term)

  // Flatten the infinite-query pages for the count badge and the invite picker.
  const franchiseList = franchises.data?.pages.flatMap((p) => p.list) ?? []
  const franchiseTotal = franchises.data?.pages[0]?.totalCount ?? franchiseList.length

  // Roles filter client-side, so the badge reflects the same search the tab applies.
  const lc = term?.toLowerCase()
  const rolesCount = roles.data?.groups.reduce(
    (n, g) =>
      n +
      (lc
        ? g.roles.filter((r) => r.name.toLowerCase().includes(lc) || (r.description ?? '').toLowerCase().includes(lc)).length
        : g.roles.length),
    0,
  )

  const counts: Record<TabKey, number | undefined> = {
    people: people.data?.pages[0]?.counts.all,
    roles: rolesCount,
    franchises: franchises.data ? franchiseTotal : undefined,
  }

  return (
    <div className="space-y-5">
      {/* Header */}
      <div className="flex flex-wrap items-start gap-4">
        <div>
          <p className="text-xs font-medium text-gray-400">Administration · Users &amp; Roles</p>
          <h1 className="text-2xl font-bold text-gray-900">Access control</h1>
        </div>
        <div className="ml-auto flex items-center gap-2">
          <div className="relative hidden sm:block">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search people, roles, franchises…"
              className="w-72 rounded-xl border border-gray-200 bg-white py-2 pl-9 pr-3 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
            />
          </div>
          {canInvite && (
            <button
              type="button"
              onClick={() => setInviteOpen(true)}
              className="inline-flex items-center gap-1.5 rounded-xl bg-lg-amber px-3.5 py-2 text-sm font-semibold text-[#11160F] hover:bg-lg-amber-hover"
            >
              <UserPlus className="h-4 w-4" /> Invite user
            </button>
          )}
        </div>
      </div>

      {/* Tabs */}
      <div className="flex items-center gap-1 border-b border-gray-200">
        {TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setTab(t.key)}
            className={cn(
              'relative px-4 py-2.5 text-sm font-medium transition-colors',
              tab === t.key ? 'text-lg-green' : 'text-gray-500 hover:text-gray-800',
            )}
          >
            <span className="inline-flex items-center gap-2">
              {t.label}
              {counts[t.key] != null && (
                <span
                  className={cn(
                    'rounded-full px-1.5 py-0.5 text-[11px] font-semibold tabular-nums',
                    tab === t.key ? 'bg-lg-green/10 text-lg-green' : 'bg-gray-100 text-gray-500',
                  )}
                >
                  {counts[t.key]}
                </span>
              )}
            </span>
            {tab === t.key && <span className="absolute inset-x-0 -bottom-px h-0.5 bg-lg-green" />}
          </button>
        ))}
      </div>

      {/* Tab body */}
      {tab === 'people' && <PeopleTab query={people} sort={peopleSort} onSort={toggleSort} />}
      {tab === 'roles' && <RolesTab query={roles} search={term} />}
      {tab === 'franchises' && <FranchisesTab query={franchises} />}

      <InviteUserModal
        open={inviteOpen}
        onClose={() => setInviteOpen(false)}
        roles={roles.data}
        franchises={franchiseList}
      />
    </div>
  )
}
