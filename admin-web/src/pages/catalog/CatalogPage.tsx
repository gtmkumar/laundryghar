import { useState } from 'react'
import { Plus, Loader2 } from 'lucide-react'
import { usePriceListsInfinite } from '@/hooks/useCatalog'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { usePermissions } from '@/hooks/usePermissions'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { DataTable, type Column } from '@/components/shared/DataTable'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import type { PriceListDto } from '@/types/api'
import { formatDate } from '@/lib/utils'
import { CreatePriceListDrawer, PriceListDetailDrawer } from './PriceListDrawer'
import { FabricMultipliersTab } from './FabricMultipliersTab'
import { AddOnsTab } from './AddOnsTab'
import { PriceMatrixTab } from './PriceMatrixTab'
import { ChangeHistoryTab } from './ChangeHistoryTab'
import { ValueSlabsTab } from './ValueSlabsTab'

type Tab = 'priceMatrix' | 'fabricMultipliers' | 'addOns' | 'valueSlabs' | 'changeHistory' | 'priceLists'

// ── Price Lists tab ─────────────────────────────────────────────────────────────

function PriceListsTab() {
  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } =
    usePriceListsInfinite()
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })
  const [opened, setOpened] = useState<PriceListDto | null>(null)

  const columns: Column<PriceListDto>[] = [
    { header: 'Code', accessor: 'code', className: 'font-mono text-xs w-28' },
    { header: 'Name', accessor: 'name' },
    { header: 'Scope', accessor: (r) => <Badge variant="secondary" className="capitalize">{r.scopeType}</Badge> },
    { header: 'Currency', accessor: 'currencyCode', className: 'tabular-nums' },
    { header: 'v', accessor: (r) => `v${r.versionNumber}`, className: 'tabular-nums text-xs' },
    { header: 'Default', accessor: (r) => (r.isDefault ? <Badge variant="default">Default</Badge> : null) },
    {
      header: 'State',
      accessor: (r) => (r.isPublished ? <Badge variant="success">Published</Badge> : <Badge variant="warning">Draft</Badge>),
    },
    { header: 'Effective From', accessor: (r) => formatDate(r.effectiveFrom) },
  ]

  if (isLoading) return <LoadingState message="Loading price lists..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  const items = data?.pages.flatMap((p) => p.list) ?? []
  const total = data?.pages[0]?.totalCount

  return (
    <div>
      {total !== undefined && <p className="px-4 pt-3 text-sm text-gray-500">{total} price list{total === 1 ? '' : 's'}</p>}
      <DataTable
        columns={columns}
        data={items}
        keyFn={(r) => r.id}
        onRowClick={(r) => setOpened(r)}
        emptyMessage="No price lists found."
      />
      <div ref={sentinelRef} className="h-1" />
      {isFetchingNextPage && (
        <div className="flex items-center justify-center py-4 text-gray-400">
          <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more…
        </div>
      )}
      <PriceListDetailDrawer priceList={opened} onClose={() => setOpened(null)} />
    </div>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function CatalogPage() {
  const { hasPermission } = usePermissions()
  const canManagePricing = hasPermission('pricing.read')

  const [activeTab, setActiveTab] = useState<Tab>('priceMatrix')
  const [creatingPriceList, setCreatingPriceList] = useState(false)

  const tabs: { id: Tab; label: string }[] = [
    { id: 'priceMatrix', label: 'Price matrix' },
    { id: 'fabricMultipliers', label: 'Fabric multipliers' },
    { id: 'addOns', label: 'Surcharges & add-ons' },
    { id: 'valueSlabs', label: 'Value slabs' },
    { id: 'changeHistory', label: 'Change history' },
    { id: 'priceLists', label: 'Price lists' },
  ]

  const action =
    activeTab === 'priceLists' && canManagePricing
      ? { label: 'New price list', onClick: () => setCreatingPriceList(true) }
      : null

  return (
    <div>
      <PageHeader
        title="Manage pricing"
        description="Base rates, fabric multipliers, surcharges and the price lists customers see."
        action={
          action ? (
            <button
              type="button"
              onClick={action.onClick}
              className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
            >
              <Plus className="h-4 w-4" /> {action.label}
            </button>
          ) : undefined
        }
      />

      <div className="mb-6 flex gap-1 border-b border-gray-200">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={[
              'border-b-2 px-4 py-2 text-sm font-medium transition-colors',
              activeTab === tab.id
                ? 'border-lg-green text-lg-green'
                : 'border-transparent text-gray-500 hover:text-gray-700',
            ].join(' ')}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <div className="mb-4 rounded-xl border border-amber-100 bg-amber-50/60 px-4 py-3 text-xs text-amber-800">
        <span className="font-semibold">How pricing works:</span> each item has a <span className="font-medium">base (Cotton) rate</span> set on the Items page.
        Fabric multipliers derive Silk/Wool/Premium prices automatically, and surcharges (express, stain) stack on top.
        Store-scoped price lists override the brand default.
      </div>

      <Card className="overflow-hidden">
        {activeTab === 'priceMatrix' && <div className="p-5"><PriceMatrixTab /></div>}
        {activeTab === 'fabricMultipliers' && <div className="p-5"><FabricMultipliersTab /></div>}
        {activeTab === 'addOns' && <div className="p-5"><AddOnsTab /></div>}
        {activeTab === 'valueSlabs' && <div className="p-5"><ValueSlabsTab /></div>}
        {activeTab === 'changeHistory' && <div className="p-5"><ChangeHistoryTab /></div>}
        {activeTab === 'priceLists' && <PriceListsTab />}
      </Card>

      <CreatePriceListDrawer open={creatingPriceList} onClose={() => setCreatingPriceList(false)} />
    </div>
  )
}
