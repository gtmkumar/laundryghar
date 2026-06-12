import { useMemo, useState } from 'react'
import { Loader2, Plus, Pencil, Power, PowerOff, Eye } from 'lucide-react'
import {
  useStoresInfinite,
  useFranchisesInfinite,
  useFranchises,
  useUpdateStore,
  useWarehouses,
  useUpdateWarehouse,
} from '@/hooks/useTenancy'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { useBrandStore } from '@/stores/brandStore'
import { usePermissions } from '@/hooks/usePermissions'
import { PageHeader } from '@/components/shared/PageHeader'
import { AddStoreDrawer } from './AddStoreDrawer'
import { StoreEditDrawer, STORE_STATUSES } from './StoreEditDrawer'
import {
  AddWarehouseDrawer,
  WarehouseDetailDrawer,
  WarehouseEditDrawer,
  WAREHOUSE_STATUSES,
} from './WarehouseDrawers'
import { DeliverySlotsTab } from './DeliverySlotsTab'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable, type FilterDef } from '@/components/shared/FilterableTable'
import { ActionMenu, ActionMenuItem } from '@/components/ui/ActionMenu'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import type { StoreDto, FranchiseDto, WarehouseDto } from '@/types/api'
import { formatDate } from '@/lib/utils'

const STORE_TYPE_OPTIONS = [
  { value: 'walkin', label: 'Walk-in' },
  { value: 'pickup_only', label: 'Pickup only' },
  { value: 'express', label: 'Express' },
  { value: 'hub', label: 'Hub' },
  { value: 'collection_point', label: 'Collection point' },
]

type Tab = 'stores' | 'franchises' | 'warehouses' | 'slots'

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

/** Build distinct dropdown options from a column of values present in the data. */
function distinctOptions<T>(rows: T[], read: (row: T) => string) {
  const seen = new Set<string>()
  for (const r of rows) {
    const v = read(r)
    if (v) seen.add(v)
  }
  return [...seen].sort().map((v) => ({ value: v, label: v.replace(/_/g, ' ') }))
}

/** Shared infinite-scroll footer (sentinel + spinner). */
function ScrollFooter({
  sentinelRef,
  loading,
}: {
  sentinelRef: React.Ref<HTMLDivElement>
  loading: boolean
}) {
  return (
    <>
      <div ref={sentinelRef} className="h-1" />
      {loading && (
        <div className="flex items-center justify-center py-4 text-gray-400">
          <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more…
        </div>
      )}
    </>
  )
}

// ── Stores tab ────────────────────────────────────────────────────────────────

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

  const stores = useMemo(() => data?.pages.flatMap((p) => p.list) ?? [], [data])
  const total = data?.pages[0]?.totalCount

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
      sortAccessor: (r) => franchiseName.get(r.franchiseId) ?? '',
    },
    {
      header: 'Type',
      accessor: (r) => <span className="capitalize">{r.storeType.replace(/_/g, ' ')}</span>,
      sortKey: 'storeType',
      sortAccessor: (r) => r.storeType,
    },
    { header: 'City', accessor: 'city', sortKey: 'city' },
    {
      header: 'Status',
      accessor: (r) => <StatusBadge status={r.status} />,
      sortKey: 'status',
      sortAccessor: (r) => r.status,
    },
    {
      header: 'Created',
      accessor: (r) => formatDate(r.createdAt),
      sortKey: 'createdAt',
      sortAccessor: (r) => r.createdAt,
    },
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

  const filters: FilterDef<StoreDto>[] = [
    {
      key: 'franchise',
      allLabel: 'All franchises',
      value: (s) => s.franchiseId,
      options: (franchisesQ.data?.list ?? []).map((f) => ({ value: f.id, label: f.legalName })),
    },
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (s) => s.status,
      options: STORE_STATUSES.map((s) => ({ value: s.value, label: s.label })),
    },
    {
      key: 'type',
      allLabel: 'All types',
      value: (s) => s.storeType,
      options: STORE_TYPE_OPTIONS,
    },
  ]

  if (isLoading) return <LoadingState message="Loading stores..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  return (
    <>
      <FilterableTable
        columns={columns}
        data={stores}
        keyFn={(r) => r.id}
        unit="store"
        totalCount={total}
        searchPlaceholder="Search code, name, city, franchise…"
        searchAccessor={(s) => `${s.code} ${s.name} ${s.city} ${franchiseName.get(s.franchiseId) ?? ''}`}
        filters={filters}
        initialSort={{ key: 'createdAt', dir: 'desc' }}
        emptyMessage="No stores found. Select a brand or add stores."
        noMatchMessage="No stores match your filters."
        csvExport={{
          filename: `stores-${new Date().toISOString().slice(0, 10)}`,
          columns: [
            { header: 'Code', value: (s) => s.code },
            { header: 'Name', value: (s) => s.name },
            { header: 'Franchise', value: (s) => franchiseName.get(s.franchiseId) ?? '' },
            { header: 'Type', value: (s) => s.storeType },
            { header: 'City', value: (s) => s.city },
            { header: 'Status', value: (s) => s.status },
            { header: 'Created', value: (s) => s.createdAt },
          ],
        }}
        footer={<ScrollFooter sentinelRef={sentinelRef} loading={isFetchingNextPage} />}
      />
      <StoreEditDrawer store={editing} onClose={() => setEditing(null)} />
    </>
  )
}

// ── Franchises tab ────────────────────────────────────────────────────────────

const franchiseColumns: Column<FranchiseDto>[] = [
  { header: 'Code', accessor: 'code', className: 'font-mono text-xs w-24', sortKey: 'code' },
  { header: 'Legal Name', accessor: 'legalName', sortKey: 'legalName' },
  {
    header: 'Onboarding',
    accessor: (r) => (
      <Badge variant={r.onboardingStatus === 'active' ? 'success' : 'warning'} className="capitalize">
        {r.onboardingStatus.replace(/_/g, ' ')}
      </Badge>
    ),
    sortKey: 'onboardingStatus',
    sortAccessor: (r) => r.onboardingStatus,
  },
  {
    header: 'Status',
    accessor: (r) => <StatusBadge status={r.status} />,
    sortKey: 'status',
    sortAccessor: (r) => r.status,
  },
  {
    header: 'Created',
    accessor: (r) => formatDate(r.createdAt),
    sortKey: 'createdAt',
    sortAccessor: (r) => r.createdAt,
  },
]

function FranchisesTab() {
  const { activeBrandId } = useBrandStore()

  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useFranchisesInfinite(activeBrandId ?? undefined)
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  const franchises = useMemo(() => data?.pages.flatMap((p) => p.list) ?? [], [data])
  const total = data?.pages[0]?.totalCount

  const filters: FilterDef<FranchiseDto>[] = [
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (f) => f.status,
      options: distinctOptions(franchises, (f) => f.status),
    },
    {
      key: 'onboarding',
      allLabel: 'All onboarding',
      value: (f) => f.onboardingStatus,
      options: distinctOptions(franchises, (f) => f.onboardingStatus),
    },
  ]

  if (isLoading) return <LoadingState message="Loading franchises..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  return (
    <FilterableTable
      columns={franchiseColumns}
      data={franchises}
      keyFn={(r) => r.id}
      unit="franchise"
      totalCount={total}
      searchPlaceholder="Search code or legal name…"
      searchAccessor={(f) => `${f.code} ${f.legalName}`}
      filters={filters}
      initialSort={{ key: 'createdAt', dir: 'desc' }}
      emptyMessage="No franchises found."
      noMatchMessage="No franchises match your filters."
      footer={<ScrollFooter sentinelRef={sentinelRef} loading={isFetchingNextPage} />}
    />
  )
}

// ── Warehouses tab ──────────────────────────────────────────────────────────────

function WarehousesTab({ addOpen, onAddClose }: { addOpen: boolean; onAddClose: () => void }) {
  const { activeBrandId } = useBrandStore()
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('warehouses.update')

  const warehousesQ = useWarehouses({ brandId: activeBrandId ?? undefined, pageSize: 100 })
  const franchisesQ = useFranchises({ brandId: activeBrandId ?? undefined, pageSize: 100 })

  const franchiseName = useMemo(() => {
    const m = new Map<string, string>()
    for (const f of franchisesQ.data?.list ?? []) m.set(f.id, f.legalName)
    return m
  }, [franchisesQ.data])

  const warehouses = useMemo(() => warehousesQ.data?.list ?? [], [warehousesQ.data])
  const total = warehousesQ.data?.totalCount

  const updateWarehouse = useUpdateWarehouse()
  const [viewing, setViewing] = useState<WarehouseDto | null>(null)
  const [editing, setEditing] = useState<WarehouseDto | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  const setStatus = async (w: WarehouseDto, status: 'active' | 'paused') => {
    setBusyId(w.id)
    try {
      await updateWarehouse.mutateAsync({ id: w.id, payload: { status } })
    } finally {
      setBusyId(null)
    }
  }

  const columns: Column<WarehouseDto>[] = [
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
      sortAccessor: (r) => franchiseName.get(r.franchiseId) ?? '',
    },
    { header: 'City', accessor: 'city', sortKey: 'city' },
    {
      header: 'Status',
      accessor: (r) => <StatusBadge status={r.status} />,
      sortKey: 'status',
      sortAccessor: (r) => r.status,
    },
    {
      header: 'Created',
      accessor: (r) => formatDate(r.createdAt),
      sortKey: 'createdAt',
      sortAccessor: (r) => r.createdAt,
    },
    {
      header: '',
      className: 'w-12 text-right',
      accessor: (r) => (
        <div onClick={(e) => e.stopPropagation()}>
          <ActionMenu busy={busyId === r.id} label="Warehouse actions">
            {(close) => (
              <>
                <ActionMenuItem icon={Eye} onClick={() => { close(); setViewing(r) }}>
                  View
                </ActionMenuItem>
                {canManage && (
                  <>
                    <ActionMenuItem icon={Pencil} onClick={() => { close(); setEditing(r) }}>
                      Edit
                    </ActionMenuItem>
                    {r.status === 'active' ? (
                      <ActionMenuItem icon={PowerOff} danger onClick={() => { close(); void setStatus(r, 'paused') }}>
                        Deactivate
                      </ActionMenuItem>
                    ) : (
                      <ActionMenuItem icon={Power} onClick={() => { close(); void setStatus(r, 'active') }}>
                        Activate
                      </ActionMenuItem>
                    )}
                  </>
                )}
              </>
            )}
          </ActionMenu>
        </div>
      ),
    },
  ]

  const filters: FilterDef<WarehouseDto>[] = [
    {
      key: 'franchise',
      allLabel: 'All franchises',
      value: (w) => w.franchiseId,
      options: (franchisesQ.data?.list ?? []).map((f) => ({ value: f.id, label: f.legalName })),
    },
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (w) => w.status,
      options: WAREHOUSE_STATUSES.map((s) => ({ value: s.value, label: s.label })),
    },
  ]

  if (warehousesQ.isLoading) return <LoadingState message="Loading warehouses..." />
  if (warehousesQ.isError)
    return <ErrorState error={warehousesQ.error as Error} onRetry={() => void warehousesQ.refetch()} />

  return (
    <>
      <FilterableTable
        columns={columns}
        data={warehouses}
        keyFn={(r) => r.id}
        onRowClick={(w) => setViewing(w)}
        unit="warehouse"
        totalCount={total}
        searchPlaceholder="Search code, name, city…"
        searchAccessor={(w) => `${w.code} ${w.name} ${w.city} ${franchiseName.get(w.franchiseId) ?? ''}`}
        filters={filters}
        initialSort={{ key: 'createdAt', dir: 'desc' }}
        emptyMessage="No warehouses found."
        noMatchMessage="No warehouses match your filters."
      />

      <AddWarehouseDrawer open={addOpen} onClose={onAddClose} />
      <WarehouseDetailDrawer
        warehouse={viewing}
        franchiseName={viewing ? franchiseName.get(viewing.franchiseId) : undefined}
        onClose={() => setViewing(null)}
        canManage={canManage}
        onEdit={(w) => { setViewing(null); setEditing(w) }}
      />
      <WarehouseEditDrawer warehouse={editing} onClose={() => setEditing(null)} />
    </>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function TenancyPage() {
  const [activeTab, setActiveTab] = useState<Tab>('stores')
  const [addStoreOpen, setAddStoreOpen] = useState(false)
  const [addWarehouseOpen, setAddWarehouseOpen] = useState(false)
  const [addSlotOpen, setAddSlotOpen] = useState(false)
  const { hasPermission } = usePermissions()
  const canCreateStore = hasPermission('stores.create')
  const canCreateWarehouse = hasPermission('warehouses.create')
  const canManageSlots = hasPermission('delivery.slot.manage')

  const tabs: { id: Tab; label: string }[] = [
    { id: 'stores', label: 'Stores' },
    { id: 'franchises', label: 'Franchises' },
    { id: 'warehouses', label: 'Warehouses' },
    { id: 'slots', label: 'Delivery slots' },
  ]

  const headerAction =
    activeTab === 'stores' && canCreateStore ? (
      <button
        type="button"
        onClick={() => setAddStoreOpen(true)}
        className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
      >
        <Plus className="h-4 w-4" /> Add store
      </button>
    ) : activeTab === 'warehouses' && canCreateWarehouse ? (
      <button
        type="button"
        onClick={() => setAddWarehouseOpen(true)}
        className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
      >
        <Plus className="h-4 w-4" /> Add warehouse
      </button>
    ) : activeTab === 'slots' && canManageSlots ? (
      <button
        type="button"
        onClick={() => setAddSlotOpen(true)}
        className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
      >
        <Plus className="h-4 w-4" /> Add slot
      </button>
    ) : undefined

  return (
    <div>
      <PageHeader
        title="Tenancy"
        description="Manage stores, franchises, and warehouse locations in the org hierarchy."
        action={headerAction}
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
        {activeTab === 'stores' ? (
          <StoresTab />
        ) : activeTab === 'franchises' ? (
          <FranchisesTab />
        ) : activeTab === 'warehouses' ? (
          <WarehousesTab addOpen={addWarehouseOpen} onAddClose={() => setAddWarehouseOpen(false)} />
        ) : (
          <DeliverySlotsTab addOpen={addSlotOpen} onAddClose={() => setAddSlotOpen(false)} />
        )}
      </Card>

      <AddStoreDrawer open={addStoreOpen} onClose={() => setAddStoreOpen(false)} />
    </div>
  )
}
