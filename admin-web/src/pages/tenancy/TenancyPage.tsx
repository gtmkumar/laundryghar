import { useState } from 'react'
import { useStores, useFranchises } from '@/hooks/useTenancy'
import { useBrandStore } from '@/stores/brandStore'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { Pagination } from '@/components/shared/Pagination'
import { DataTable, type Column } from '@/components/shared/DataTable'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import type { StoreDto, FranchiseDto } from '@/types/api'
import { formatDate } from '@/lib/utils'

// ── Sub-tabs ─────────────────────────────────────────────────────────────────

type Tab = 'stores' | 'franchises'

const storeColumns: Column<StoreDto>[] = [
  { header: 'Code', accessor: 'code', className: 'font-mono text-xs w-24' },
  { header: 'Name', accessor: 'name' },
  { header: 'Type', accessor: (r) => <span className="capitalize">{r.storeType}</span> },
  { header: 'City', accessor: 'city' },
  {
    header: 'Status',
    accessor: (r) => (
      <StatusBadge status={r.status} />
    ),
  },
  { header: 'Created', accessor: (r) => formatDate(r.createdAt) },
]

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
    status === 'active' ? 'success' : status === 'inactive' ? 'secondary' : 'warning'
  return (
    <Badge variant={variant} className="capitalize">
      {status}
    </Badge>
  )
}

// ── Stores tab ────────────────────────────────────────────────────────────────

function StoresTab() {
  const { activeBrandId } = useBrandStore()
  const [page, setPage] = useState(1)

  const { data, isLoading, isError, error, refetch } = useStores({
    brandId: activeBrandId ?? undefined,
    page,
    pageSize: 20,
  })

  if (isLoading) return <LoadingState message="Loading stores..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  const stores = data?.list ?? []

  return (
    <div>
      <DataTable
        columns={storeColumns}
        data={stores}
        keyFn={(r) => r.id}
        emptyMessage="No stores found. Select a brand or add stores."
      />
      <Pagination
        page={page}
        hasPrevious={data?.hasPreviousPage ?? false}
        hasNext={data?.hasNextPage ?? false}
        onPrevious={() => setPage((p) => Math.max(1, p - 1))}
        onNext={() => setPage((p) => p + 1)}
      />
    </div>
  )
}

// ── Franchises tab ────────────────────────────────────────────────────────────

function FranchisesTab() {
  const { activeBrandId } = useBrandStore()
  const [page, setPage] = useState(1)

  const { data, isLoading, isError, error, refetch } = useFranchises({
    brandId: activeBrandId ?? undefined,
    page,
    pageSize: 20,
  })

  if (isLoading) return <LoadingState message="Loading franchises..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  const franchises = data?.list ?? []

  return (
    <div>
      <DataTable
        columns={franchiseColumns}
        data={franchises}
        keyFn={(r) => r.id}
        emptyMessage="No franchises found."
      />
      <Pagination
        page={page}
        hasPrevious={data?.hasPreviousPage ?? false}
        hasNext={data?.hasNextPage ?? false}
        onPrevious={() => setPage((p) => Math.max(1, p - 1))}
        onNext={() => setPage((p) => p + 1)}
      />
    </div>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function TenancyPage() {
  const [activeTab, setActiveTab] = useState<Tab>('stores')

  const tabs: { id: Tab; label: string }[] = [
    { id: 'stores', label: 'Stores' },
    { id: 'franchises', label: 'Franchises' },
  ]

  return (
    <div>
      <PageHeader
        title="Tenancy"
        description="Manage stores, franchises, and warehouse locations in the org hierarchy."
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
    </div>
  )
}
