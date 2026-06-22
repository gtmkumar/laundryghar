import { useMemo, useState } from 'react'
import {
  Plus, Pencil, Trash2, Search, Download, FolderPlus, Loader2, Save, Upload,
} from 'lucide-react'
import {
  useManagedItems,
  useItemStats,
  useItemGroups,
  useCreateItemGroup,
  useFabricTypes,
  useServicesInfinite,
  useServiceCategoriesInfinite,
  useSaveItemPricing,
} from '@/hooks/useCatalog'
import { showToast } from '@/stores/toastStore'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { usePermissions } from '@/hooks/usePermissions'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { DataTable, type Column } from '@/components/shared/DataTable'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { ActionMenu, ActionMenuItem } from '@/components/ui/ActionMenu'
import { FormDrawer, DrawerSection, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import { cn } from '@/lib/utils'
import { apiErrorMessage } from '@/lib/apiError'
import { useAutoCode } from '@/hooks/useAutoCode'
import { buildNameLocalized, displayLocalized } from '../catalog/localized'
import {
  ServiceEditDrawer,
  CategoryEditDrawer,
  DeleteCatalogDrawer,
} from '../catalog/CatalogDrawers'
import { ItemManageDrawer } from './ItemManageDrawer'
import { ImportCsvDrawer } from './ImportCsvDrawer'
import type { ManagedItemDto, ServiceCategoryDto, ServiceDto } from '@/types/api'

type Tab = 'items' | 'services' | 'categories'

function StatusBadge({ status }: { status: string }) {
  const variant =
    status === 'active' ? 'success'
    : status === 'draft' ? 'warning'
    : status === 'archived' || status === 'disabled' ? 'destructive'
    : 'secondary'
  return <Badge variant={variant} className="capitalize">{status}</Badge>
}

// ── Stat cards ─────────────────────────────────────────────────────────────────

function StatCards() {
  const { data } = useItemStats()
  const cells = [
    { label: 'Total items', value: data?.totalItems ?? '—', sub: `across ${data?.categoryCount ?? 0} categories` },
    { label: 'Active', value: data?.activeItems ?? '—', sub: 'live in app & POS' },
    { label: 'Drafts', value: data?.draftItems ?? '—', sub: 'not yet published' },
    { label: 'Avg turnaround', value: data?.avgTatHours ? `${data.avgTatHours}h` : '—', sub: 'standard service' },
  ]
  return (
    <div className="mb-6 grid grid-cols-2 gap-3 lg:grid-cols-4">
      {cells.map((c) => (
        <Card key={c.label} className="px-4 py-3">
          <p className="text-xs font-medium uppercase tracking-wide text-gray-400">{c.label}</p>
          <p className="mt-1 text-2xl font-semibold text-gray-900">{c.value}</p>
          <p className="text-xs text-gray-400">{c.sub}</p>
        </Card>
      ))}
    </div>
  )
}

// ── New category (item group) drawer ───────────────────────────────────────────

function NewCategoryDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  const create = useCreateItemGroup()
  const codeF = useAutoCode()
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)

  const [wasOpen, setWasOpen] = useState(open)
  if (open !== wasOpen) {
    setWasOpen(open)
    if (open) { codeF.seed('', false); setName(''); setError(null) }
  }
  if (!open) return null

  const submit = async () => {
    setError(null)
    if (!codeF.code.trim()) return setError('Code is required.')
    if (!name.trim()) return setError('Name is required.')
    try {
      await create.mutateAsync({
        code: codeF.code.trim(),
        name: name.trim(),
        nameLocalized: buildNameLocalized(name.trim(), ''),
        iconUrl: null,
        displayOrder: 0,
        isVisibleMobile: true,
      })
      onClose()
    } catch (e) {
      setError(apiErrorMessage(e, 'Could not create the category.'))
    }
  }

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={FolderPlus}
      eyebrow="Catalogue · Category"
      title="New category"
      width="sm"
      error={error}
      onSubmit={() => void submit()}
      submitLabel="Create category"
      submittingLabel="Creating…"
      submitIcon={Plus}
      submitting={create.isPending}
    >
      <DrawerSection title="Identity">
        <Field label="Name *">
          <input value={name} onChange={(e) => { setName(e.target.value); codeF.syncFromName(e.target.value) }} className={drawerInputCls} placeholder="Men's wear" />
        </Field>
        <Field label="Code *" hint="Auto-filled from the name; edit to override.">
          <input value={codeF.code} onChange={(e) => codeF.setCode(e.target.value)} className={`${drawerInputCls} font-mono`} placeholder="MENS-WEAR" />
        </Field>
      </DrawerSection>
    </FormDrawer>
  )
}

// ── Items tab (the centrepiece) ─────────────────────────────────────────────────

function ItemsTab({ canManage, creating, onCloseCreate }: { canManage: boolean; creating: boolean; onCloseCreate: () => void }) {
  const { data, isLoading, isError, error, refetch } = useManagedItems()
  const { data: groupData } = useItemGroups()
  const { data: fabricData } = useFabricTypes()
  const { data: serviceData } = useServicesInfinite()

  const [groupId, setGroupId] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<ManagedItemDto | null>(null)
  const [deleting, setDeleting] = useState<{ id: string; name: string; code: string } | null>(null)
  const [newCategory, setNewCategory] = useState(false)

  // Bulk edit: inline-editable per-service price cells, saved in one batch.
  const savePricing = useSaveItemPricing()
  const [bulkMode, setBulkMode] = useState(false)
  const [edits, setEdits] = useState<Record<string, Record<string, string>>>({})
  const [saving, setSaving] = useState(false)

  const allItems = useMemo(() => data?.list ?? [], [data])
  const groups = groupData?.list ?? []
  const fabricName = useMemo(() => {
    const m = new Map<string, string>()
    for (const f of fabricData?.list ?? []) m.set(f.id, f.name)
    return m
  }, [fabricData])
  const services = useMemo(
    () => (serviceData?.pages.flatMap((p) => p.list) ?? []).filter((s) => s.status === 'active'),
    [serviceData],
  )

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase()
    return allItems.filter((i) => {
      if (groupId && i.itemGroupId !== groupId) return false
      if (q && !i.name.toLowerCase().includes(q) && !i.code.toLowerCase().includes(q)) return false
      return true
    })
  }, [allItems, groupId, search])

  const countFor = (gid: string | null) =>
    gid === null ? allItems.length : allItems.filter((i) => i.itemGroupId === gid).length

  const priceFor = (item: ManagedItemDto, serviceId: string) =>
    item.servicePrices.find((p) => p.serviceId === serviceId)?.basePrice

  // ── Bulk edit helpers ──────────────────────────────────────────────────────
  const cellEdited = (itemId: string, serviceId: string) => edits[itemId]?.[serviceId]
  const setCell = (itemId: string, serviceId: string, value: string) =>
    setEdits((e) => ({ ...e, [itemId]: { ...e[itemId], [serviceId]: value } }))

  // An item is "dirty" if any of its edited cells differ from the stored price.
  const itemDirty = (item: ManagedItemDto) => {
    const e = edits[item.id]
    if (!e) return false
    return Object.entries(e).some(([sid, v]) => {
      const cur = priceFor(item, sid)
      const curStr = cur != null ? String(cur) : ''
      return v.trim() !== curStr
    })
  }
  const dirtyItems = useMemo(() => rows.filter(itemDirty), [rows, edits]) // eslint-disable-line react-hooks/exhaustive-deps

  const exitBulk = () => { setBulkMode(false); setEdits({}) }

  const saveBulk = async () => {
    if (dirtyItems.length === 0) { exitBulk(); return }
    setSaving(true)
    try {
      for (const item of dirtyItems) {
        await savePricing.mutateAsync({
          id: item.id,
          payload: {
            servicePrices: services.map((s) => {
              const edited = edits[item.id]?.[s.id]
              if (edited !== undefined) return { serviceId: s.id, basePrice: edited.trim() ? Number(edited) : null }
              const cur = priceFor(item, s.id)
              return { serviceId: s.id, basePrice: cur ?? null }
            }),
            fabricTypeIds: item.fabricTypeIds,
          },
        })
      }
      showToast('success', `Updated ${dirtyItems.length} item${dirtyItems.length === 1 ? '' : 's'}.`)
      exitBulk()
    } catch (e) {
      showToast('error', e instanceof Error ? e.message : 'Bulk save failed.')
    } finally {
      setSaving(false)
    }
  }

  const exportCsv = () => {
    const header = ['Code', 'Name', 'Category', 'Status', 'TAT', ...services.map((s) => s.name)]
    const lines = rows.map((i) => [
      i.code, i.name, i.itemGroupName ?? '', i.status, i.tatHours ?? '',
      ...services.map((s) => priceFor(i, s.id) ?? ''),
    ])
    const csv = [header, ...lines].map((r) => r.map((c) => `"${String(c).replace(/"/g, '""')}"`).join(',')).join('\n')
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }))
    const a = document.createElement('a')
    a.href = url; a.download = 'items.csv'; a.click()
    URL.revokeObjectURL(url)
  }

  const columns: Column<ManagedItemDto>[] = [
    {
      header: 'Item',
      accessor: (i) => (
        <div className="min-w-0">
          <p className="truncate font-medium text-gray-800">{i.name}</p>
          <p className="font-mono text-xs text-gray-400">{i.code}</p>
        </div>
      ),
    },
    {
      header: 'Fabric variants',
      accessor: (i) => {
        const names = i.fabricTypeIds.map((id) => fabricName.get(id)).filter(Boolean) as string[]
        if (names.length === 0) return <span className="text-gray-300">—</span>
        return (
          <div className="flex flex-wrap gap-1">
            {names.slice(0, 3).map((n) => (
              <span key={n} className="rounded bg-gray-100 px-1.5 py-0.5 text-xs text-gray-600">{n}</span>
            ))}
            {names.length > 3 && <span className="text-xs text-gray-400">+{names.length - 3}</span>}
          </div>
        )
      },
    },
    ...services.map((s): Column<ManagedItemDto> => ({
      header: s.name,
      className: 'text-right',
      accessor: (i) => {
        const p = priceFor(i, s.id)
        if (bulkMode && canManage) {
          const edited = cellEdited(i.id, s.id)
          const value = edited !== undefined ? edited : p != null ? String(p) : ''
          const dirty = edited !== undefined && edited.trim() !== (p != null ? String(p) : '')
          return (
            <div onClick={(e) => e.stopPropagation()} className="relative inline-block w-20">
              <span className="pointer-events-none absolute left-2 top-1/2 -translate-y-1/2 text-xs text-gray-400">₹</span>
              <input
                type="number"
                min="0"
                step="0.01"
                value={value}
                onChange={(e) => setCell(i.id, s.id, e.target.value)}
                className={cn(
                  'w-full rounded-md border py-1 pl-5 pr-1.5 text-right text-sm tabular-nums outline-none focus:border-lg-green',
                  dirty ? 'border-lg-green bg-lg-green/5' : 'border-gray-200',
                )}
                placeholder="—"
              />
            </div>
          )
        }
        return <span className="tabular-nums">{p != null ? `₹${p}` : <span className="text-gray-300">—</span>}</span>
      },
    })),
    { header: 'TAT', accessor: (i) => (i.tatHours != null ? `${i.tatHours}h` : '—'), className: 'tabular-nums text-gray-500' },
    { header: 'Status', accessor: (i) => <StatusBadge status={i.status} /> },
    ...(canManage
      ? [{
          header: '',
          className: 'w-12 text-right',
          accessor: (i: ManagedItemDto) => (
            <div onClick={(e) => e.stopPropagation()}>
              <ActionMenu label="Item actions">
                {(close) => (
                  <>
                    <ActionMenuItem icon={Pencil} onClick={() => { close(); setEditing(i) }}>Edit</ActionMenuItem>
                    <ActionMenuItem icon={Trash2} danger onClick={() => { close(); setDeleting({ id: i.id, name: i.name, code: i.code }) }}>Delete</ActionMenuItem>
                  </>
                )}
              </ActionMenu>
            </div>
          ),
        }]
      : []),
  ]

  if (isLoading) return <LoadingState message="Loading items..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  return (
    <div className="flex gap-5">
      {/* Category rail */}
      <aside className="w-52 shrink-0">
        <p className="mb-1 px-2 text-[11px] font-semibold uppercase tracking-wide text-gray-400">Categories</p>
        <div className="space-y-0.5">
          <button
            type="button"
            onClick={() => setGroupId(null)}
            className={cn('flex w-full items-center justify-between rounded-lg px-2.5 py-1.5 text-sm',
              groupId === null ? 'bg-lg-green/10 font-medium text-lg-green' : 'text-gray-600 hover:bg-gray-50')}
          >
            <span>All items</span><span className="text-xs text-gray-400">{countFor(null)}</span>
          </button>
          {groups.map((g) => (
            <button
              key={g.id}
              type="button"
              onClick={() => setGroupId(g.id)}
              className={cn('flex w-full items-center justify-between rounded-lg px-2.5 py-1.5 text-sm',
                groupId === g.id ? 'bg-lg-green/10 font-medium text-lg-green' : 'text-gray-600 hover:bg-gray-50')}
            >
              <span className="truncate">{g.name}</span><span className="text-xs text-gray-400">{countFor(g.id)}</span>
            </button>
          ))}
        </div>
        {canManage && (
          <button
            type="button"
            onClick={() => setNewCategory(true)}
            className="mt-2 flex w-full items-center gap-1.5 rounded-lg border border-dashed border-gray-200 px-2.5 py-1.5 text-sm text-gray-500 hover:bg-gray-50"
          >
            <FolderPlus className="h-3.5 w-3.5" /> New category
          </button>
        )}
      </aside>

      {/* Table */}
      <div className="min-w-0 flex-1">
        <div className="mb-3 flex items-center gap-2">
          <div className="relative flex-1">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-gray-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search item or SKU…"
              className="w-full rounded-xl border border-gray-200 py-2 pl-9 pr-3 text-sm focus:border-lg-green focus:outline-none"
            />
          </div>
          {canManage && services.length > 0 && (
            bulkMode ? (
              <button type="button" onClick={exitBulk} className="inline-flex items-center gap-1.5 rounded-xl border border-gray-200 px-3 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50">
                Cancel
              </button>
            ) : (
              <button type="button" onClick={() => setBulkMode(true)} className="inline-flex items-center gap-1.5 rounded-xl border border-gray-200 px-3 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50">
                <Pencil className="h-4 w-4" /> Bulk edit
              </button>
            )
          )}
          <button type="button" onClick={exportCsv} className="inline-flex items-center gap-1.5 rounded-xl border border-gray-200 px-3 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50">
            <Download className="h-4 w-4" /> Export
          </button>
        </div>
        {bulkMode ? (
          <div className="mb-2 flex items-center justify-between rounded-lg border border-lg-green/30 bg-lg-green/5 px-3 py-2">
            <p className="text-sm text-gray-600">
              Editing base prices inline — <span className="font-medium text-gray-800">{dirtyItems.length}</span> item{dirtyItems.length === 1 ? '' : 's'} changed.
            </p>
            <button
              type="button"
              onClick={() => void saveBulk()}
              disabled={saving || dirtyItems.length === 0}
              className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-3 py-1.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-50"
            >
              {saving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Save className="h-3.5 w-3.5" />}
              Save changes
            </button>
          </div>
        ) : (
          <p className="mb-2 text-sm text-gray-500">{rows.length} item{rows.length === 1 ? '' : 's'}</p>
        )}
        <Card className="overflow-hidden">
          <DataTable
            columns={columns}
            data={rows}
            keyFn={(r) => r.id}
            onRowClick={canManage && !bulkMode ? (r) => setEditing(r) : undefined}
            emptyMessage="No items found."
          />
        </Card>
      </div>

      <ItemManageDrawer open={creating} onClose={onCloseCreate} />
      <ItemManageDrawer open={!!editing} item={editing} onClose={() => setEditing(null)} />
      <DeleteCatalogDrawer entity={deleting} kind="item" onClose={() => setDeleting(null)} />
      <NewCategoryDrawer open={newCategory} onClose={() => setNewCategory(false)} />
    </div>
  )
}

// ── Services tab ────────────────────────────────────────────────────────────────

function ServicesTab({ canManage }: { canManage: boolean }) {
  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } = useServicesInfinite()
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })
  const [editing, setEditing] = useState<ServiceDto | null>(null)
  const [deleting, setDeleting] = useState<ServiceDto | null>(null)
  const [creating, setCreating] = useState(false)

  const columns: Column<ServiceDto>[] = [
    { header: 'Code', accessor: 'code', className: 'font-mono text-xs w-28' },
    { header: 'Name', accessor: 'name' },
    { header: 'Pricing', accessor: (r) => <span className="capitalize">{r.pricingModel.replace(/_/g, ' ')}</span> },
    { header: 'TAT', accessor: (r) => `${r.baseTatHours}h`, className: 'tabular-nums' },
    { header: 'Express', accessor: (r) => (r.isExpressAvailable ? <Badge variant="success">×{r.expressMultiplier}</Badge> : <Badge variant="secondary">No</Badge>) },
    { header: 'Status', accessor: (r) => <StatusBadge status={r.status} /> },
    ...(canManage
      ? [{
          header: '', className: 'w-12 text-right',
          accessor: (r: ServiceDto) => (
            <div onClick={(e) => e.stopPropagation()}>
              <ActionMenu label="Service actions">
                {(close) => (
                  <>
                    <ActionMenuItem icon={Pencil} onClick={() => { close(); setEditing(r) }}>Edit</ActionMenuItem>
                    <ActionMenuItem icon={Trash2} danger onClick={() => { close(); setDeleting(r) }}>Delete</ActionMenuItem>
                  </>
                )}
              </ActionMenu>
            </div>
          ),
        }]
      : []),
  ]

  if (isLoading) return <LoadingState message="Loading services..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />
  const items = data?.pages.flatMap((p) => p.list) ?? []

  return (
    <div>
      <div className="mb-3 flex justify-end">
        {canManage && (
          <button type="button" onClick={() => setCreating(true)} className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-3 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]">
            <Plus className="h-4 w-4" /> New service
          </button>
        )}
      </div>
      <Card className="overflow-hidden">
        <DataTable columns={columns} data={items} keyFn={(r) => r.id} onRowClick={canManage ? (r) => setEditing(r) : undefined} emptyMessage="No services found." />
        <div ref={sentinelRef} className="h-1" />
        {isFetchingNextPage && <div className="flex items-center justify-center py-4 text-gray-400"><Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more…</div>}
      </Card>
      <ServiceEditDrawer open={creating} onClose={() => setCreating(false)} />
      <ServiceEditDrawer open={!!editing} service={editing} onClose={() => setEditing(null)} />
      <DeleteCatalogDrawer entity={deleting} kind="service" onClose={() => setDeleting(null)} />
    </div>
  )
}

// ── Service categories tab ───────────────────────────────────────────────────────

function CategoriesTab({ canManage }: { canManage: boolean }) {
  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } = useServiceCategoriesInfinite()
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })
  const [editing, setEditing] = useState<ServiceCategoryDto | null>(null)
  const [deleting, setDeleting] = useState<ServiceCategoryDto | null>(null)
  const [creating, setCreating] = useState(false)

  const columns: Column<ServiceCategoryDto>[] = [
    { header: 'Code', accessor: 'code', className: 'font-mono text-xs w-28' },
    { header: 'Name', accessor: 'name' },
    { header: 'Localized', accessor: (r) => displayLocalized(r.nameLocalized), className: 'text-gray-400' },
    { header: 'Order', accessor: (r) => String(r.displayOrder) },
    { header: 'Status', accessor: (r) => <StatusBadge status={r.status} /> },
    ...(canManage
      ? [{
          header: '', className: 'w-12 text-right',
          accessor: (r: ServiceCategoryDto) => (
            <div onClick={(e) => e.stopPropagation()}>
              <ActionMenu label="Category actions">
                {(close) => (
                  <>
                    <ActionMenuItem icon={Pencil} onClick={() => { close(); setEditing(r) }}>Edit</ActionMenuItem>
                    <ActionMenuItem icon={Trash2} danger onClick={() => { close(); setDeleting(r) }}>Delete</ActionMenuItem>
                  </>
                )}
              </ActionMenu>
            </div>
          ),
        }]
      : []),
  ]

  if (isLoading) return <LoadingState message="Loading categories..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />
  const items = data?.pages.flatMap((p) => p.list) ?? []

  return (
    <div>
      <div className="mb-3 flex justify-end">
        {canManage && (
          <button type="button" onClick={() => setCreating(true)} className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-3 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]">
            <Plus className="h-4 w-4" /> New service category
          </button>
        )}
      </div>
      <Card className="overflow-hidden">
        <DataTable columns={columns} data={items} keyFn={(r) => r.id} onRowClick={canManage ? (r) => setEditing(r) : undefined} emptyMessage="No service categories found." />
        <div ref={sentinelRef} className="h-1" />
        {isFetchingNextPage && <div className="flex items-center justify-center py-4 text-gray-400"><Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more…</div>}
      </Card>
      <CategoryEditDrawer open={creating} onClose={() => setCreating(false)} />
      <CategoryEditDrawer open={!!editing} category={editing} onClose={() => setEditing(null)} />
      <DeleteCatalogDrawer entity={deleting} kind="category" onClose={() => setDeleting(null)} />
    </div>
  )
}

// ── Page ─────────────────────────────────────────────────────────────────────────

export function ItemsPage() {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('catalog.read') // platform_admin bypasses
  const [activeTab, setActiveTab] = useState<Tab>('items')
  const [creatingItem, setCreatingItem] = useState(false)
  const [importing, setImporting] = useState(false)

  const tabs: { id: Tab; label: string }[] = [
    { id: 'items', label: 'Items' },
    { id: 'services', label: 'Services' },
    { id: 'categories', label: 'Service Categories' },
  ]

  return (
    <div>
      <PageHeader
        title="Manage laundry items"
        description="Manage service categories, services, items, and the prices customers see."
        action={
          activeTab === 'items' && canManage ? (
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => setImporting(true)}
                className="inline-flex items-center gap-1.5 rounded-xl border border-gray-200 px-4 py-2.5 text-sm font-medium text-gray-600 hover:bg-gray-50"
              >
                <Upload className="h-4 w-4" /> Import CSV
              </button>
              <button
                type="button"
                onClick={() => setCreatingItem(true)}
                className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
              >
                <Plus className="h-4 w-4" /> New item
              </button>
            </div>
          ) : undefined
        }
      />

      {activeTab === 'items' && <StatCards />}

      <div className="mb-6 flex gap-1 border-b border-gray-200">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={cn(
              'border-b-2 px-4 py-2 text-sm font-medium transition-colors',
              activeTab === tab.id ? 'border-lg-green text-lg-green' : 'border-transparent text-gray-500 hover:text-gray-700',
            )}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === 'items' && <ItemsTab canManage={canManage} creating={creatingItem} onCloseCreate={() => setCreatingItem(false)} />}
      {activeTab === 'services' && <ServicesTab canManage={canManage} />}
      {activeTab === 'categories' && <CategoriesTab canManage={canManage} />}

      <ImportCsvDrawer open={importing} onClose={() => setImporting(false)} />
    </div>
  )
}
