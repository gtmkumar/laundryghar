import { useMemo, useState } from 'react'
import { Loader2, Plus, Pencil, Trash2 } from 'lucide-react'
import { ActionMenu, ActionMenuItem } from '@/components/ui/ActionMenu'
import {
  useServiceCategoriesInfinite,
  useServicesInfinite,
  useItemsInfinite,
  usePriceListsInfinite,
} from '@/hooks/useCatalog'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { usePermissions } from '@/hooks/usePermissions'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { DataTable, type Column } from '@/components/shared/DataTable'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import type {
  ServiceCategoryDto,
  ServiceDto,
  ItemDto,
  PriceListDto,
} from '@/types/api'
import { formatDate } from '@/lib/utils'
import { displayLocalized } from './localized'
import {
  CategoryEditDrawer,
  ServiceEditDrawer,
  ItemEditDrawer,
  DeleteCatalogDrawer,
} from './CatalogDrawers'
import { CreatePriceListDrawer, PriceListDetailDrawer } from './PriceListDrawer'

type Tab = 'categories' | 'services' | 'items' | 'priceLists'

function StatusBadge({ status }: { status: string }) {
  const variant = status === 'active' ? 'success' : status === 'archived' ? 'destructive' : 'secondary'
  return (
    <Badge variant={variant} className="capitalize">
      {status}
    </Badge>
  )
}

function InfiniteFooter({
  isFetchingNextPage,
  sentinelRef,
}: {
  isFetchingNextPage: boolean
  sentinelRef: (node: HTMLElement | null) => void
}) {
  return (
    <>
      <div ref={sentinelRef} className="h-1" />
      {isFetchingNextPage && (
        <div className="flex items-center justify-center py-4 text-gray-400">
          <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more…
        </div>
      )}
    </>
  )
}

// ── Service Categories ────────────────────────────────────────────────────────

function CategoriesTab({ canManage }: { canManage: boolean }) {
  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useServiceCategoriesInfinite()
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })
  const [editing, setEditing] = useState<ServiceCategoryDto | null>(null)
  const [deleting, setDeleting] = useState<ServiceCategoryDto | null>(null)

  const columns: Column<ServiceCategoryDto>[] = [
    { header: 'Code', accessor: 'code', className: 'font-mono text-xs w-28' },
    { header: 'Name', accessor: 'name' },
    { header: 'Localized', accessor: (r) => displayLocalized(r.nameLocalized), className: 'text-gray-400' },
    {
      header: 'Mobile',
      accessor: (r) => <span className={r.isVisibleMobile ? 'text-green-600' : 'text-gray-300'}>{r.isVisibleMobile ? 'Yes' : 'No'}</span>,
    },
    { header: 'Order', accessor: (r) => String(r.displayOrder) },
    { header: 'Status', accessor: (r) => <StatusBadge status={r.status} /> },
    { header: 'Updated', accessor: (r) => formatDate(r.updatedAt) },
    ...(canManage
      ? [
          {
            header: '',
            className: 'w-12 text-right',
            accessor: (r: ServiceCategoryDto) => (
              <div onClick={(e) => e.stopPropagation()}>
                <ActionMenu label="Category actions">
                  {(close) => (
                    <>
                      <ActionMenuItem icon={Pencil} onClick={() => { close(); setEditing(r) }}>
                        Edit
                      </ActionMenuItem>
                      <ActionMenuItem icon={Trash2} danger onClick={() => { close(); setDeleting(r) }}>
                        Delete
                      </ActionMenuItem>
                    </>
                  )}
                </ActionMenu>
              </div>
            ),
          },
        ]
      : []),
  ]

  if (isLoading) return <LoadingState message="Loading categories..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  const items = data?.pages.flatMap((p) => p.list) ?? []
  const total = data?.pages[0]?.totalCount

  return (
    <div>
      {total !== undefined && <p className="px-4 pt-3 text-sm text-gray-500">{total} categor{total === 1 ? 'y' : 'ies'}</p>}
      <DataTable
        columns={columns}
        data={items}
        keyFn={(r) => r.id}
        onRowClick={canManage ? (r) => setEditing(r) : undefined}
        emptyMessage="No service categories found."
      />
      <InfiniteFooter isFetchingNextPage={isFetchingNextPage} sentinelRef={sentinelRef} />
      <CategoryEditDrawer open={!!editing} category={editing} onClose={() => setEditing(null)} />
      <DeleteCatalogDrawer entity={deleting} kind="category" onClose={() => setDeleting(null)} />
    </div>
  )
}

// ── Services ──────────────────────────────────────────────────────────────────

function ServicesTab({ canManage }: { canManage: boolean }) {
  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useServicesInfinite()
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })
  const [editing, setEditing] = useState<ServiceDto | null>(null)
  const [deleting, setDeleting] = useState<ServiceDto | null>(null)

  const columns: Column<ServiceDto>[] = [
    { header: 'Code', accessor: 'code', className: 'font-mono text-xs w-28' },
    { header: 'Name', accessor: 'name' },
    { header: 'Pricing', accessor: (r) => <span className="capitalize">{r.pricingModel.replace(/_/g, ' ')}</span> },
    { header: 'TAT (Base)', accessor: (r) => `${r.baseTatHours}h`, className: 'tabular-nums' },
    { header: 'TAT (Express)', accessor: (r) => (r.isExpressAvailable ? `${r.expressTatHours}h` : '—'), className: 'tabular-nums' },
    {
      header: 'Express',
      accessor: (r) => (r.isExpressAvailable ? <Badge variant="success">×{r.expressMultiplier}</Badge> : <Badge variant="secondary">No</Badge>),
    },
    { header: 'Status', accessor: (r) => <StatusBadge status={r.status} /> },
    ...(canManage
      ? [
          {
            header: '',
            className: 'w-12 text-right',
            accessor: (r: ServiceDto) => (
              <div onClick={(e) => e.stopPropagation()}>
                <ActionMenu label="Service actions">
                  {(close) => (
                    <>
                      <ActionMenuItem icon={Pencil} onClick={() => { close(); setEditing(r) }}>
                        Edit
                      </ActionMenuItem>
                      <ActionMenuItem icon={Trash2} danger onClick={() => { close(); setDeleting(r) }}>
                        Delete
                      </ActionMenuItem>
                    </>
                  )}
                </ActionMenu>
              </div>
            ),
          },
        ]
      : []),
  ]

  if (isLoading) return <LoadingState message="Loading services..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  const items = data?.pages.flatMap((p) => p.list) ?? []
  const total = data?.pages[0]?.totalCount

  return (
    <div>
      {total !== undefined && <p className="px-4 pt-3 text-sm text-gray-500">{total} service{total === 1 ? '' : 's'}</p>}
      <DataTable
        columns={columns}
        data={items}
        keyFn={(r) => r.id}
        onRowClick={canManage ? (r) => setEditing(r) : undefined}
        emptyMessage="No services found."
      />
      <InfiniteFooter isFetchingNextPage={isFetchingNextPage} sentinelRef={sentinelRef} />
      <ServiceEditDrawer open={!!editing} service={editing} onClose={() => setEditing(null)} />
      <DeleteCatalogDrawer entity={deleting} kind="service" onClose={() => setDeleting(null)} />
    </div>
  )
}

// ── Items ─────────────────────────────────────────────────────────────────────

function ItemsTab({ canManage }: { canManage: boolean }) {
  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useItemsInfinite()
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })
  const [editing, setEditing] = useState<ItemDto | null>(null)
  const [deleting, setDeleting] = useState<ItemDto | null>(null)

  const columns: Column<ItemDto>[] = [
    { header: 'Code', accessor: 'code', className: 'font-mono text-xs w-28' },
    { header: 'Name', accessor: 'name' },
    { header: 'Localized', accessor: (r) => displayLocalized(r.nameLocalized), className: 'text-gray-400' },
    { header: 'Weight', accessor: (r) => (r.typicalWeightGrams != null ? `${r.typicalWeightGrams}g` : '—'), className: 'tabular-nums' },
    { header: 'Order', accessor: (r) => String(r.displayOrder) },
    { header: 'Status', accessor: (r) => <StatusBadge status={r.status} /> },
    { header: 'Updated', accessor: (r) => formatDate(r.updatedAt) },
    ...(canManage
      ? [
          {
            header: '',
            className: 'w-12 text-right',
            accessor: (r: ItemDto) => (
              <div onClick={(e) => e.stopPropagation()}>
                <ActionMenu label="Item actions">
                  {(close) => (
                    <>
                      <ActionMenuItem icon={Pencil} onClick={() => { close(); setEditing(r) }}>
                        Edit
                      </ActionMenuItem>
                      <ActionMenuItem icon={Trash2} danger onClick={() => { close(); setDeleting(r) }}>
                        Delete
                      </ActionMenuItem>
                    </>
                  )}
                </ActionMenu>
              </div>
            ),
          },
        ]
      : []),
  ]

  if (isLoading) return <LoadingState message="Loading items..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  const items = data?.pages.flatMap((p) => p.list) ?? []
  const total = data?.pages[0]?.totalCount

  return (
    <div>
      {total !== undefined && <p className="px-4 pt-3 text-sm text-gray-500">{total} item{total === 1 ? '' : 's'}</p>}
      <DataTable
        columns={columns}
        data={items}
        keyFn={(r) => r.id}
        onRowClick={canManage ? (r) => setEditing(r) : undefined}
        emptyMessage="No items found."
      />
      <InfiniteFooter isFetchingNextPage={isFetchingNextPage} sentinelRef={sentinelRef} />
      <ItemEditDrawer open={!!editing} item={editing} onClose={() => setEditing(null)} />
      <DeleteCatalogDrawer entity={deleting} kind="item" onClose={() => setDeleting(null)} />
    </div>
  )
}

// ── Price Lists ───────────────────────────────────────────────────────────────

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
      <InfiniteFooter isFetchingNextPage={isFetchingNextPage} sentinelRef={sentinelRef} />
      <PriceListDetailDrawer priceList={opened} onClose={() => setOpened(null)} />
    </div>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function CatalogPage() {
  const { hasPermission } = usePermissions()
  const canManageCatalog = hasPermission('catalog.read') // platform_admin bypasses
  const canManagePricing = hasPermission('pricing.read')

  const [activeTab, setActiveTab] = useState<Tab>('categories')
  const [creatingCategory, setCreatingCategory] = useState(false)
  const [creatingService, setCreatingService] = useState(false)
  const [creatingItem, setCreatingItem] = useState(false)
  const [creatingPriceList, setCreatingPriceList] = useState(false)

  const tabs: { id: Tab; label: string }[] = useMemo(
    () => [
      { id: 'categories', label: 'Service Categories' },
      { id: 'services', label: 'Services' },
      { id: 'items', label: 'Items' },
      { id: 'priceLists', label: 'Price lists' },
    ],
    [],
  )

  const action = (() => {
    switch (activeTab) {
      case 'categories':
        return canManageCatalog ? { label: 'New category', onClick: () => setCreatingCategory(true) } : null
      case 'services':
        return canManageCatalog ? { label: 'New service', onClick: () => setCreatingService(true) } : null
      case 'items':
        return canManageCatalog ? { label: 'New item', onClick: () => setCreatingItem(true) } : null
      case 'priceLists':
        return canManagePricing ? { label: 'New price list', onClick: () => setCreatingPriceList(true) } : null
    }
  })()

  return (
    <div>
      <PageHeader
        title="Catalog & Pricing"
        description="Manage service categories, services, items, and the price lists customers see."
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

      <Card className="overflow-hidden">
        {activeTab === 'categories' && <CategoriesTab canManage={canManageCatalog} />}
        {activeTab === 'services' && <ServicesTab canManage={canManageCatalog} />}
        {activeTab === 'items' && <ItemsTab canManage={canManageCatalog} />}
        {activeTab === 'priceLists' && <PriceListsTab />}
      </Card>

      <CategoryEditDrawer open={creatingCategory} onClose={() => setCreatingCategory(false)} />
      <ServiceEditDrawer open={creatingService} onClose={() => setCreatingService(false)} />
      <ItemEditDrawer open={creatingItem} onClose={() => setCreatingItem(false)} />
      <CreatePriceListDrawer open={creatingPriceList} onClose={() => setCreatingPriceList(false)} />
    </div>
  )
}
