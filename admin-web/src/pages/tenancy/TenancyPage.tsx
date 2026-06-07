import { useState } from 'react'
import { Loader2 } from 'lucide-react'
import { useStoresInfinite, useFranchisesInfinite } from '@/hooks/useTenancy'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { useBrandStore } from '@/stores/brandStore'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
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

  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useStoresInfinite(activeBrandId ?? undefined)
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  if (isLoading) return <LoadingState message="Loading stores..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  const stores = data?.pages.flatMap((p) => p.list) ?? []
  const total = data?.pages[0]?.totalCount

  return (
    <div>
      {total !== undefined && (
        <p className="text-sm text-gray-500 px-4 pt-3">{total} store{total === 1 ? '' : 's'}</p>
      )}
      <DataTable
        columns={storeColumns}
        data={stores}
        keyFn={(r) => r.id}
        emptyMessage="No stores found. Select a brand or add stores."
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
