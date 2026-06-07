import { useState } from 'react'
import { Loader2 } from 'lucide-react'
import {
  useServiceCategoriesInfinite,
  useServicesInfinite,
  usePriceListsInfinite,
} from '@/hooks/useCatalog'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { DataTable, type Column } from '@/components/shared/DataTable'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import type { ServiceCategoryDto, ServiceDto, PriceListDto } from '@/types/api'
import { formatDate } from '@/lib/utils'

type Tab = 'categories' | 'services' | 'priceLists'

function StatusBadge({ status }: { status: string }) {
  return (
    <Badge variant={status === 'active' ? 'success' : 'secondary'} className="capitalize">
      {status}
    </Badge>
  )
}

// ── Service Categories tab ────────────────────────────────────────────────────

const categoryColumns: Column<ServiceCategoryDto>[] = [
  { header: 'Code', accessor: 'code', className: 'font-mono text-xs w-28' },
  { header: 'Name', accessor: 'name' },
  { header: 'Localized Name', accessor: 'nameLocalized', className: 'text-gray-400' },
  {
    header: 'Mobile',
    accessor: (r) => (
      <span className={r.isVisibleMobile ? 'text-green-600' : 'text-gray-300'}>
        {r.isVisibleMobile ? 'Yes' : 'No'}
      </span>
    ),
  },
  { header: 'Order', accessor: (r) => String(r.displayOrder) },
  { header: 'Status', accessor: (r) => <StatusBadge status={r.status} /> },
  { header: 'Updated', accessor: (r) => formatDate(r.updatedAt) },
]

function CategoriesTab() {
  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useServiceCategoriesInfinite()
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  if (isLoading) return <LoadingState message="Loading categories..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  const items = data?.pages.flatMap((p) => p.list) ?? []
  const total = data?.pages[0]?.totalCount

  return (
    <div>
      {total !== undefined && (
        <p className="text-sm text-gray-500 px-4 pt-3">{total} categor{total === 1 ? 'y' : 'ies'}</p>
      )}
      <DataTable
        columns={categoryColumns}
        data={items}
        keyFn={(r) => r.id}
        emptyMessage="No service categories found."
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

// ── Services tab ──────────────────────────────────────────────────────────────

const serviceColumns: Column<ServiceDto>[] = [
  { header: 'Code', accessor: 'code', className: 'font-mono text-xs w-28' },
  { header: 'Name', accessor: 'name' },
  { header: 'Pricing Model', accessor: (r) => <span className="capitalize">{r.pricingModel}</span> },
  {
    header: 'TAT (Base)',
    accessor: (r) => `${r.baseTatHours}h`,
    className: 'tabular-nums',
  },
  {
    header: 'Express',
    accessor: (r) =>
      r.isExpressAvailable ? (
        <Badge variant="success">Yes ×{r.expressMultiplier}</Badge>
      ) : (
        <Badge variant="secondary">No</Badge>
      ),
  },
  { header: 'Status', accessor: (r) => <StatusBadge status={r.status} /> },
  { header: 'Updated', accessor: (r) => formatDate(r.updatedAt) },
]

function ServicesTab() {
  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useServicesInfinite()
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  if (isLoading) return <LoadingState message="Loading services..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  const items = data?.pages.flatMap((p) => p.list) ?? []
  const total = data?.pages[0]?.totalCount

  return (
    <div>
      {total !== undefined && (
        <p className="text-sm text-gray-500 px-4 pt-3">{total} service{total === 1 ? '' : 's'}</p>
      )}
      <DataTable
        columns={serviceColumns}
        data={items}
        keyFn={(r) => r.id}
        emptyMessage="No services found."
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

// ── Price Lists tab ───────────────────────────────────────────────────────────

const priceListColumns: Column<PriceListDto>[] = [
  { header: 'Code', accessor: 'code', className: 'font-mono text-xs w-28' },
  { header: 'Name', accessor: 'name' },
  {
    header: 'Scope',
    accessor: (r) => (
      <Badge variant="secondary" className="capitalize">
        {r.scopeType}
      </Badge>
    ),
  },
  { header: 'Currency', accessor: 'currencyCode', className: 'tabular-nums' },
  { header: 'v', accessor: (r) => `v${r.versionNumber}`, className: 'tabular-nums text-xs' },
  {
    header: 'Default',
    accessor: (r) =>
      r.isDefault ? <Badge variant="default">Default</Badge> : null,
  },
  {
    header: 'Published',
    accessor: (r) =>
      r.isPublished ? (
        <Badge variant="success">Published</Badge>
      ) : (
        <Badge variant="warning">Draft</Badge>
      ),
  },
  { header: 'Effective From', accessor: (r) => formatDate(r.effectiveFrom) },
  { header: 'Status', accessor: (r) => <StatusBadge status={r.status} /> },
]

function PriceListsTab() {
  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } =
    usePriceListsInfinite()
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  if (isLoading) return <LoadingState message="Loading price lists..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  const items = data?.pages.flatMap((p) => p.list) ?? []
  const total = data?.pages[0]?.totalCount

  return (
    <div>
      {total !== undefined && (
        <p className="text-sm text-gray-500 px-4 pt-3">{total} price list{total === 1 ? '' : 's'}</p>
      )}
      <DataTable
        columns={priceListColumns}
        data={items}
        keyFn={(r) => r.id}
        emptyMessage="No price lists found."
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

export function CatalogPage() {
  const [activeTab, setActiveTab] = useState<Tab>('categories')

  const tabs: { id: Tab; label: string }[] = [
    { id: 'categories', label: 'Service Categories' },
    { id: 'services', label: 'Services' },
    { id: 'priceLists', label: 'Price Lists' },
  ]

  return (
    <div>
      <PageHeader
        title="Catalog & Pricing"
        description="Manage service categories, services, and price lists."
      />

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
        {activeTab === 'categories' && <CategoriesTab />}
        {activeTab === 'services' && <ServicesTab />}
        {activeTab === 'priceLists' && <PriceListsTab />}
      </Card>
    </div>
  )
}
