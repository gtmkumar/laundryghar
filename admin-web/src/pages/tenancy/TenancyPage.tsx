import { useMemo, useState } from 'react'
import { Loader2, Plus, Search, Pencil, Power, PowerOff } from 'lucide-react'
import {
  useStoresInfinite,
  useFranchisesInfinite,
  useFranchises,
  useUpdateStore,
} from '@/hooks/useTenancy'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { useBrandStore } from '@/stores/brandStore'
import { usePermissions } from '@/hooks/usePermissions'
import { PageHeader } from '@/components/shared/PageHeader'
import { AddStoreDrawer } from './AddStoreDrawer'
import { StoreEditDrawer, STORE_STATUSES } from './StoreEditDrawer'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { DataTable, type Column, type SortState } from '@/components/shared/DataTable'
import { ActionMenu, ActionMenuItem } from '@/components/ui/ActionMenu'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import type { StoreDto, FranchiseDto } from '@/types/api'
import { formatDate } from '@/lib/utils'

const STORE_TYPE_OPTIONS = [
  { value: 'walkin', label: 'Walk-in' },
  { value: 'pickup_only', label: 'Pickup only' },
  { value: 'express', label: 'Express' },
  { value: 'hub', label: 'Hub' },
  { value: 'collection_point', label: 'Collection point' },
]

// ── Sub-tabs ─────────────────────────────────────────────────────────────────

type Tab = 'stores' | 'franchises'

const franchiseColumns: Column<FranchiseDto>[] = [
  { header: 'Code', accessor: 'code', className: 'font-mono text-xs w-24' },
  { header: 'Legal Name', accessor: 'legalName' },
  {
    header: 'Onboarding',
    accessor: (r) => (
      <Badge variant={r.onboardingStatus === 'completed' ? 'success' : 'warning'}>
        {r.onboardingStatus}
      </Badge>
    ),
  },
  {
    header: 'Status',
    accessor: (r) => <StatusBadge status={r.status} />,
  },
  { header: 'Created', accessor: (r) => formatDate(r.createdAt) },
]

function StatusBadge({ status }: { status: string }) {
  const variant =
    status === 'active' || status === 'completed'
      ? 'success'
      : status === 'closed' || status === 'inactive'
        ? 'secondary'
        : 'warning'
  return (
    <Badge variant={variant} className="capitalize">
      {status.replace(/_/g, ' ')}
    </Badge>
  )
}

// ── Stores tab ────────────────────────────────────────────────────────────────

type StoreSortKey = 'code' | 'name' | 'storeType' | 'franchise' | 'city' | 'status' | 'createdAt'

function StoresTab() {
  const { activeBrandId } = useBrandStore()
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('stores.update')

  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useStoresInfinite(activeBrandId ?? undefined)
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  // Franchises drive both the "which franchise" column and the franchise filter.
  const franchisesQ = useFranchises({ brandId: activeBrandId ?? undefined, pageSize: 100 })
  const franchiseName = useMemo(() => {
    const m = new Map<string, string>()
    for (const f of franchisesQ.data?.list ?? []) m.set(f.id, f.legalName)
    return m
  }, [franchisesQ.data])

  const updateStore = useUpdateStore()
  const [editing, setEditing] = useState<StoreDto | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  // Toolbar state.
  const [search, setSearch] = useState('')
  const [franchiseFilter, setFranchiseFilter] = useState('')
  const [statusFilter, setStatusFilter] = useState('')
  const [typeFilter, setTypeFilter] = useState('')
  const [sort, setSort] = useState<SortState>({ key: 'createdAt', dir: 'desc' })

  const allStores = useMemo(() => data?.pages.flatMap((p) => p.list) ?? [], [data])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    const rows = allStores.filter((s) => {
      if (franchiseFilter && s.franchiseId !== franchiseFilter) return false
      if (statusFilter && s.status !== statusFilter) return false
      if (typeFilter && s.storeType !== typeFilter) return false
      if (q) {
        const hay = `${s.code} ${s.name} ${s.city} ${franchiseName.get(s.franchiseId) ?? ''}`.toLowerCase()
        if (!hay.includes(q)) return false
      }
      return true
    })
    const dir = sort.dir === 'asc' ? 1 : -1
    const val = (s: StoreDto): string => {
      switch (sort.key as StoreSortKey) {
        case 'franchise':
          return franchiseName.get(s.franchiseId) ?? ''
        case 'createdAt':
          return s.createdAt
        default:
          return String(s[sort.key as keyof StoreDto] ?? '')
      }
    }
    return [...rows].sort((a, b) => val(a).localeCompare(val(b), undefined, { numeric: true }) * dir)
  }, [allStores, search, franchiseFilter, statusFilter, typeFilter, sort, franchiseName])

  const toggleSort = (key: string) =>
    setSort((s) => (s.key === key ? { key, dir: s.dir === 'asc' ? 'desc' : 'asc' } : { key, dir: 'asc' }))

  const setStatus = async (store: StoreDto, status: 'active' | 'paused') => {
    setBusyId(store.id)
    try {
      await updateStore.mutateAsync({ id: store.id, payload: { status } })
    } finally {
      setBusyId(null)
    }
  }

  const columns: Column<StoreDto>[] = [
    { header: 'Code', accessor: 'code', className: 'font-mono text-xs w-24', sortKey: 'code' },
    { header: 'Name', accessor: 'name', sortKey: 'name' },
    {
      header: 'Franchise',
      accessor: (r) => (
        <span className={franchiseName.has(r.franchiseId) ? '' : 'text-gray-400'}>
          {franchiseName.get(r.franchiseId) ?? '—'}
        </span>
      ),
      sortKey: 'franchise',
    },
    {
      header: 'Type',
      accessor: (r) => <span className="capitalize">{r.storeType.replace(/_/g, ' ')}</span>,
      sortKey: 'storeType',
    },
    { header: 'City', accessor: 'city', sortKey: 'city' },
    { header: 'Status', accessor: (r) => <StatusBadge status={r.status} />, sortKey: 'status' },
    { header: 'Created', accessor: (r) => formatDate(r.createdAt), sortKey: 'createdAt' },
    ...(canManage
      ? [
          {
            header: '',
            className: 'w-12 text-right',
            accessor: (r: StoreDto) => (
              <div onClick={(e) => e.stopPropagation()}>
                <ActionMenu busy={busyId === r.id} label="Store actions">
                  {(close) => (
                    <>
                      <ActionMenuItem icon={Pencil} onClick={() => { close(); setEditing(r) }}>
                        Edit
                      </ActionMenuItem>
                      {r.status === 'active' ? (
                        <ActionMenuItem
                          icon={PowerOff}
                          danger
                          onClick={() => { close(); void setStatus(r, 'paused') }}
                        >
                          Deactivate
                        </ActionMenuItem>
                      ) : (
                        <ActionMenuItem
                          icon={Power}
                          onClick={() => { close(); void setStatus(r, 'active') }}
                        >
                          Activate
                        </ActionMenuItem>
                      )}
                    </>
                  )}
                </ActionMenu>
              </div>
            ),
          } as Column<StoreDto>,
        ]
      : []),
  ]

  if (isLoading) return <LoadingState message="Loading stores..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  const total = data?.pages[0]?.totalCount

  return (
    <div>
      {/* Toolbar — search + filters */}
      <div className="flex flex-wrap items-center gap-2 px-4 pt-4">
        <div className="relative min-w-[200px] flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-gray-400" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search code, name, city, franchise…"
            className="w-full rounded-lg border border-gray-200 bg-white py-2 pl-9 pr-3 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
          />
        </div>
        <select value={franchiseFilter} onChange={(e) => setFranchiseFilter(e.target.value)} className={filterCls}>
          <option value="">All franchises</option>
          {(franchisesQ.data?.list ?? []).map((f) => (
            <option key={f.id} value={f.id}>{f.legalName}</option>
          ))}
        </select>
        <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} className={filterCls}>
          <option value="">All statuses</option>
          {STORE_STATUSES.map((s) => (
            <option key={s.value} value={s.value}>{s.label}</option>
          ))}
        </select>
        <select value={typeFilter} onChange={(e) => setTypeFilter(e.target.value)} className={filterCls}>
          <option value="">All types</option>
          {STORE_TYPE_OPTIONS.map((t) => (
            <option key={t.value} value={t.value}>{t.label}</option>
          ))}
        </select>
      </div>

      <p className="px-4 py-3 text-sm text-gray-500">
        {filtered.length}
        {typeof total === 'number' && filtered.length !== total ? ` of ${total}` : ''} store
        {filtered.length === 1 ? '' : 's'}
      </p>

      <DataTable
        columns={columns}
        data={filtered}
        keyFn={(r) => r.id}
        sort={sort}
        onSort={toggleSort}
        emptyMessage={
          allStores.length === 0
            ? 'No stores found. Select a brand or add stores.'
            : 'No stores match your filters.'
        }
      />
      <div ref={sentinelRef} className="h-1" />
      {isFetchingNextPage && (
        <div className="flex items-center justify-center py-4 text-gray-400">
          <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more…
        </div>
      )}

      <StoreEditDrawer store={editing} onClose={() => setEditing(null)} />
    </div>
  )
}

const filterCls =
  'rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm text-gray-700 outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'

// ── Franchises tab ────────────────────────────────────────────────────────────

function FranchisesTab() {
  const { activeBrandId } = useBrandStore()

  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useFranchisesInfinite(activeBrandId ?? undefined)
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  if (isLoading) return <LoadingState message="Loading franchises..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  const franchises = data?.pages.flatMap((p) => p.list) ?? []
  const total = data?.pages[0]?.totalCount

  return (
    <div>
      {total !== undefined && (
        <p className="text-sm text-gray-500 px-4 pt-3">{total} franchise{total === 1 ? '' : 's'}</p>
      )}
      <DataTable
        columns={franchiseColumns}
        data={franchises}
        keyFn={(r) => r.id}
        emptyMessage="No franchises found."
      />
      <div ref={sentinelRef} className="h-1" />
      {isFetchingNextPage && (
        <div className="flex items-center justify-center py-4 text-gray-400">
          <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more…
        </div>
      )}
    </div>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function TenancyPage() {
  const [activeTab, setActiveTab] = useState<Tab>('stores')
  const [addStoreOpen, setAddStoreOpen] = useState(false)
  const { hasPermission } = usePermissions()
  const canCreateStore = hasPermission('stores.create')

  const tabs: { id: Tab; label: string }[] = [
    { id: 'stores', label: 'Stores' },
    { id: 'franchises', label: 'Franchises' },
  ]

  return (
    <div>
      <PageHeader
        title="Tenancy"
        description="Manage stores, franchises, and warehouse locations in the org hierarchy."
        action={
          activeTab === 'stores' && canCreateStore ? (
            <button
              type="button"
              onClick={() => setAddStoreOpen(true)}
              className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
            >
              <Plus className="h-4 w-4" /> Add store
            </button>
          ) : undefined
        }
      />

      {/* Tab bar */}
      <div className="flex gap-1 border-b border-gray-200 mb-6">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={[
              'px-4 py-2 text-sm font-medium border-b-2 transition-colors',
              activeTab === tab.id
                ? 'border-blue-600 text-blue-600'
                : 'border-transparent text-gray-500 hover:text-gray-700',
            ].join(' ')}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <Card className="overflow-hidden">
        {activeTab === 'stores' ? <StoresTab /> : <FranchisesTab />}
      </Card>

      <AddStoreDrawer open={addStoreOpen} onClose={() => setAddStoreOpen(false)} />
    </div>
  )
}
