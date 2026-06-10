import { useEffect, useMemo, useState } from 'react'
import {
  ListChecks,
  Plus,
  Save,
  Send,
  Trash2,
  Pencil,
  Check,
  X,
  Info,
} from 'lucide-react'
import {
  useCreatePriceList,
  useUpdatePriceList,
  usePublishPriceList,
  usePriceListItems,
  useCreatePriceListItem,
  useUpdatePriceListItem,
  useServicesInfinite,
  useItemsInfinite,
} from '@/hooks/useCatalog'
import { useFranchises, useStores } from '@/hooks/useTenancy'
import {
  FormDrawer,
  DrawerSection,
  Field,
  drawerInputCls,
} from '@/components/shared/FormDrawer'
import { Badge } from '@/components/ui/badge'
import { apiErrorMessage } from '@/lib/apiError'
import type {
  PriceListDto,
  PriceListItemDto,
  ServiceDto,
  ItemDto,
} from '@/types/api'
import { formatCurrency } from '@/lib/utils'
import { dateOnly, toInstant } from './localized'

const SCOPES = [
  { value: 'brand', label: 'Brand (all franchises)' },
  { value: 'franchise', label: 'Franchise' },
  { value: 'store', label: 'Store' },
] as const

// ══════════════════════════════════════════════════════════════════════════════
// Create price list (header only — items are added after creation)
// ══════════════════════════════════════════════════════════════════════════════

export function CreatePriceListDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  const create = useCreatePriceList()
  const { data: franchiseData } = useFranchises({ pageSize: 100 })
  const franchises = franchiseData?.list ?? []
  const { data: storeData } = useStores({ pageSize: 100 })
  const stores = storeData?.list ?? []

  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [currencyCode, setCurrencyCode] = useState('INR')
  const [scopeType, setScopeType] = useState('brand')
  const [franchiseId, setFranchiseId] = useState('')
  const [storeId, setStoreId] = useState('')
  const [effectiveFrom, setEffectiveFrom] = useState(() => new Date().toISOString().slice(0, 10))
  const [effectiveTo, setEffectiveTo] = useState('')
  const [isDefault, setIsDefault] = useState(false)
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setError(null)
    setCode('')
    setName('')
    setDescription('')
    setCurrencyCode('INR')
    setScopeType('brand')
    setFranchiseId('')
    setStoreId('')
    setEffectiveFrom(new Date().toISOString().slice(0, 10))
    setEffectiveTo('')
    setIsDefault(false)
    setNotes('')
  }, [open])

  // Stores belonging to the chosen franchise narrow the store picker.
  const scopedStores = useMemo(
    () => (franchiseId ? stores.filter((s) => s.franchiseId === franchiseId) : stores),
    [stores, franchiseId],
  )

  if (!open) return null

  const submit = async () => {
    setError(null)
    if (!code.trim()) return setError('Code is required.')
    if (!name.trim()) return setError('Name is required.')
    if (currencyCode.trim().length !== 3) return setError('Currency must be a 3-letter code, e.g. INR.')
    if (scopeType === 'franchise' && !franchiseId) return setError('Pick a franchise for a franchise-scoped list.')
    if (scopeType === 'store' && !storeId) return setError('Pick a store for a store-scoped list.')
    if (effectiveTo && effectiveTo < effectiveFrom) return setError('Effective-to must be on or after effective-from.')

    try {
      await create.mutateAsync({
        code: code.trim(),
        name: name.trim(),
        description: description.trim() || null,
        currencyCode: currencyCode.trim().toUpperCase(),
        scopeType,
        franchiseId: scopeType === 'franchise' ? franchiseId : scopeType === 'store' ? (stores.find((s) => s.id === storeId)?.franchiseId ?? null) : null,
        storeId: scopeType === 'store' ? storeId : null,
        parentPriceListId: null,
        effectiveFrom: toInstant(effectiveFrom),
        effectiveTo: effectiveTo ? toInstant(effectiveTo) : null,
        isDefault,
        notes: notes.trim() || null,
      })
      onClose()
    } catch (e) {
      setError(apiErrorMessage(e, 'Could not create the price list.'))
    }
  }

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={ListChecks}
      eyebrow="Pricing · Price list"
      title="New price list"
      width="md"
      error={error}
      onSubmit={() => void submit()}
      submitLabel="Create draft"
      submittingLabel="Creating…"
      submitIcon={Plus}
      submitting={create.isPending}
    >
      <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2.5 text-xs text-amber-800">
        <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
        <span>
          Scope priority: a <b>store</b> list overrides its <b>franchise</b>, and a franchise
          overrides the <b>brand</b> list. The customer app reads the published <b>brand</b> list.
        </span>
      </div>

      <DrawerSection title="Identity">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Code *">
            <input value={code} onChange={(e) => setCode(e.target.value)} className={`${drawerInputCls} font-mono`} placeholder="MAIN-2026" />
          </Field>
          <Field label="Currency *">
            <input value={currencyCode} onChange={(e) => setCurrencyCode(e.target.value)} maxLength={3} className={`${drawerInputCls} font-mono uppercase`} placeholder="INR" />
          </Field>
        </div>
        <Field label="Name *">
          <input value={name} onChange={(e) => setName(e.target.value)} className={drawerInputCls} placeholder="Main Brand Price List" />
        </Field>
        <Field label="Description">
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={drawerInputCls} placeholder="Optional" />
        </Field>
      </DrawerSection>

      <DrawerSection title="Scope">
        <Field label="Scope type *">
          <select value={scopeType} onChange={(e) => { setScopeType(e.target.value); setFranchiseId(''); setStoreId('') }} className={drawerInputCls}>
            {SCOPES.map((s) => (<option key={s.value} value={s.value}>{s.label}</option>))}
          </select>
        </Field>
        {scopeType === 'franchise' && (
          <Field label="Franchise *">
            <select value={franchiseId} onChange={(e) => setFranchiseId(e.target.value)} className={drawerInputCls}>
              <option value="">Select a franchise…</option>
              {franchises.map((f) => (<option key={f.id} value={f.id}>{f.legalName}</option>))}
            </select>
          </Field>
        )}
        {scopeType === 'store' && (
          <>
            <Field label="Franchise" hint="Narrows the store list (optional).">
              <select value={franchiseId} onChange={(e) => { setFranchiseId(e.target.value); setStoreId('') }} className={drawerInputCls}>
                <option value="">All franchises</option>
                {franchises.map((f) => (<option key={f.id} value={f.id}>{f.legalName}</option>))}
              </select>
            </Field>
            <Field label="Store *">
              <select value={storeId} onChange={(e) => setStoreId(e.target.value)} className={drawerInputCls}>
                <option value="">Select a store…</option>
                {scopedStores.map((s) => (<option key={s.id} value={s.id}>{s.name} · {s.city}</option>))}
              </select>
            </Field>
          </>
        )}
      </DrawerSection>

      <DrawerSection title="Validity">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Effective from *">
            <input type="date" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} className={drawerInputCls} />
          </Field>
          <Field label="Effective to" hint="Blank = no end.">
            <input type="date" value={effectiveTo} onChange={(e) => setEffectiveTo(e.target.value)} className={drawerInputCls} />
          </Field>
        </div>
        <label className="flex items-center gap-2 text-sm text-gray-700">
          <input type="checkbox" checked={isDefault} onChange={(e) => setIsDefault(e.target.checked)} className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30" />
          Default list for this scope
        </label>
        <Field label="Notes">
          <textarea rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} className={drawerInputCls} placeholder="Optional internal notes" />
        </Field>
      </DrawerSection>
    </FormDrawer>
  )
}

// ══════════════════════════════════════════════════════════════════════════════
// Price-list detail editor (header + items) — the centerpiece
// ══════════════════════════════════════════════════════════════════════════════

interface RowDraft {
  serviceId: string
  itemId: string
  fabricTypeId: string
  displayLabel: string
  basePrice: string
  expressPrice: string
  minimumQuantity: string
  taxRatePercent: string
  isTaxable: boolean
}

function blankRow(): RowDraft {
  return {
    serviceId: '',
    itemId: '',
    fabricTypeId: '',
    displayLabel: '',
    basePrice: '',
    expressPrice: '',
    minimumQuantity: '1',
    taxRatePercent: '0',
    isTaxable: false,
  }
}

export function PriceListDetailDrawer({
  priceList,
  onClose,
}: {
  priceList: PriceListDto | null
  onClose: () => void
}) {
  const isOpen = !!priceList
  const id = priceList?.id ?? null
  const published = !!priceList?.isPublished

  const update = useUpdatePriceList()
  const publish = usePublishPriceList()
  const { data: itemsData, isLoading: itemsLoading } = usePriceListItems(id)
  const createItem = useCreatePriceListItem(id ?? '')
  const updateItem = useUpdatePriceListItem(id ?? '')

  const { data: serviceData } = useServicesInfinite()
  const services = serviceData?.pages.flatMap((p) => p.list) ?? []
  const { data: itemCatData } = useItemsInfinite()
  const catalogItems = itemCatData?.pages.flatMap((p) => p.list) ?? []

  const rows = useMemo(
    () => (itemsData?.list ?? []).filter((r) => r.isActive),
    [itemsData],
  )

  // Header edit state
  const [name, setName] = useState('')
  const [effectiveFrom, setEffectiveFrom] = useState('')
  const [effectiveTo, setEffectiveTo] = useState('')
  const [isDefault, setIsDefault] = useState(false)
  const [notes, setNotes] = useState('')
  const [headerError, setHeaderError] = useState<string | null>(null)

  // Item-editor state
  const [draft, setDraft] = useState<RowDraft>(blankRow())
  const [editingRowId, setEditingRowId] = useState<string | null>(null)
  const [rowError, setRowError] = useState<string | null>(null)

  useEffect(() => {
    if (!priceList) return
    setName(priceList.name)
    setEffectiveFrom(dateOnly(priceList.effectiveFrom))
    setEffectiveTo(dateOnly(priceList.effectiveTo))
    setIsDefault(priceList.isDefault)
    setNotes(priceList.notes ?? '')
    setHeaderError(null)
    setDraft(blankRow())
    setEditingRowId(null)
    setRowError(null)
  }, [priceList])

  const serviceName = (sid: string) => services.find((s) => s.id === sid)?.name ?? sid.slice(0, 8)
  const itemName = (iid: string) => catalogItems.find((i) => i.id === iid)?.name ?? iid.slice(0, 8)

  // Auto-suggest the display label from the picked item + service.
  const suggestLabel = (itemId: string, serviceId: string): string => {
    const i = catalogItems.find((c) => c.id === itemId)
    const s = services.find((c) => c.id === serviceId)
    if (i && s) return `${i.name} – ${s.name}`
    return i?.name ?? ''
  }

  const onPickItem = (itemId: string) => {
    setDraft((d) => {
      const label = d.displayLabel.trim() && editingRowId ? d.displayLabel : suggestLabel(itemId, d.serviceId)
      return { ...d, itemId, displayLabel: label }
    })
  }
  const onPickService = (serviceId: string) => {
    setDraft((d) => {
      const label = d.displayLabel.trim() && editingRowId ? d.displayLabel : suggestLabel(d.itemId, serviceId)
      return { ...d, serviceId, displayLabel: label }
    })
  }

  const saveHeader = async () => {
    if (!priceList) return
    setHeaderError(null)
    if (!name.trim()) return setHeaderError('Name is required.')
    if (effectiveTo && effectiveTo < effectiveFrom) return setHeaderError('Effective-to must be on or after effective-from.')
    try {
      await update.mutateAsync({
        id: priceList.id,
        payload: {
          name: name.trim(),
          description: priceList.description,
          effectiveFrom: toInstant(effectiveFrom),
          effectiveTo: effectiveTo ? toInstant(effectiveTo) : null,
          isDefault,
          notes: notes.trim() || null,
          status: priceList.status,
        },
      })
    } catch (e) {
      setHeaderError(apiErrorMessage(e, 'Could not save the price list.'))
    }
  }

  const validateRow = (d: RowDraft): string | null => {
    if (!d.serviceId) return 'Pick a service.'
    if (!d.itemId) return 'Pick an item.'
    if (!d.displayLabel.trim()) return 'Display label is required.'
    const price = Number(d.basePrice)
    if (!(price >= 0) || d.basePrice.trim() === '') return 'Base price must be 0 or more.'
    if (d.expressPrice.trim() && !(Number(d.expressPrice) >= 0)) return 'Express price must be 0 or more.'
    const minQ = Number(d.minimumQuantity)
    if (!(minQ >= 1)) return 'Minimum quantity must be at least 1.'
    return null
  }

  const addRow = async () => {
    if (!id) return
    setRowError(null)
    const err = validateRow(draft)
    if (err) return setRowError(err)
    try {
      await createItem.mutateAsync({
        serviceId: draft.serviceId,
        itemId: draft.itemId,
        itemVariantId: null,
        fabricTypeId: draft.fabricTypeId || null,
        itemGroupId: null,
        basePrice: Number(draft.basePrice),
        expressPrice: draft.expressPrice.trim() ? Number(draft.expressPrice) : null,
        minimumQuantity: Number(draft.minimumQuantity),
        taxRatePercent: Number(draft.taxRatePercent) || 0,
        isTaxable: draft.isTaxable,
        displayLabel: draft.displayLabel.trim(),
        notes: null,
      })
      setDraft(blankRow())
    } catch (e) {
      setRowError(apiErrorMessage(e, 'Could not add the priced item.'))
    }
  }

  const startEdit = (r: PriceListItemDto) => {
    setEditingRowId(r.id)
    setRowError(null)
    setDraft({
      serviceId: r.serviceId,
      itemId: r.itemId,
      fabricTypeId: r.fabricTypeId ?? '',
      displayLabel: r.displayLabel ?? '',
      basePrice: String(r.basePrice),
      expressPrice: r.expressPrice != null ? String(r.expressPrice) : '',
      minimumQuantity: String(r.minimumQuantity),
      taxRatePercent: String(r.taxRatePercent),
      isTaxable: r.isTaxable,
    })
  }

  const saveEdit = async () => {
    if (!id || !editingRowId) return
    setRowError(null)
    const err = validateRow(draft)
    if (err) return setRowError(err)
    try {
      await updateItem.mutateAsync({
        id: editingRowId,
        payload: {
          basePrice: Number(draft.basePrice),
          expressPrice: draft.expressPrice.trim() ? Number(draft.expressPrice) : null,
          minimumQuantity: Number(draft.minimumQuantity),
          taxRatePercent: Number(draft.taxRatePercent) || 0,
          isTaxable: draft.isTaxable,
          displayLabel: draft.displayLabel.trim(),
          notes: null,
          isActive: true,
        },
      })
      setEditingRowId(null)
      setDraft(blankRow())
    } catch (e) {
      setRowError(apiErrorMessage(e, 'Could not update the priced item.'))
    }
  }

  // "Remove" = deactivate (no DELETE endpoint for price-list-items).
  const removeRow = async (r: PriceListItemDto) => {
    if (!id) return
    setRowError(null)
    try {
      await updateItem.mutateAsync({
        id: r.id,
        payload: {
          basePrice: r.basePrice,
          expressPrice: r.expressPrice,
          minimumQuantity: r.minimumQuantity,
          taxRatePercent: r.taxRatePercent,
          isTaxable: r.isTaxable,
          displayLabel: r.displayLabel,
          notes: r.notes,
          isActive: false,
        },
      })
    } catch (e) {
      setRowError(apiErrorMessage(e, 'Could not remove the priced item.'))
    }
  }

  const doPublish = async () => {
    if (!priceList) return
    setHeaderError(null)
    if (rows.length === 0) return setHeaderError('Add at least one priced item before publishing.')
    try {
      await publish.mutateAsync(priceList.id)
      onClose()
    } catch (e) {
      setHeaderError(apiErrorMessage(e, 'Could not publish the price list.'))
    }
  }

  if (!priceList) return null

  return (
    <FormDrawer
      open={isOpen}
      onClose={onClose}
      icon={ListChecks}
      eyebrow={<>Price list · <span className="font-mono">{priceList.code}</span></>}
      title={priceList.name}
      width="lg"
      headerExtra={
        <div className="flex items-center gap-2">
          <Badge variant="secondary" className="capitalize">{priceList.scopeType}</Badge>
          {published ? (
            <Badge variant="success">Published</Badge>
          ) : (
            <Badge variant="warning">Draft</Badge>
          )}
          <span className="text-xs text-gray-400">{priceList.currencyCode} · v{priceList.versionNumber}</span>
        </div>
      }
      error={headerError}
      footer={
        <div className="flex justify-between gap-2">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50"
          >
            Close
          </button>
          {!published && (
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => void saveHeader()}
                disabled={update.isPending}
                className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-60"
              >
                <Save className="h-3.5 w-3.5" /> Save header
              </button>
              <button
                type="button"
                onClick={() => void doPublish()}
                disabled={publish.isPending}
                className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
              >
                <Send className="h-3.5 w-3.5" /> Publish
              </button>
            </div>
          )}
        </div>
      }
    >
      {published && (
        <div className="flex items-start gap-2 rounded-lg border border-blue-200 bg-blue-50 px-3 py-2.5 text-xs text-blue-800">
          <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
          <span>This list is published and read-only. Create a new draft to change prices.</span>
        </div>
      )}

      {/* Header fields */}
      <DrawerSection title="Details">
        <Field label="Name *">
          <input value={name} onChange={(e) => setName(e.target.value)} disabled={published} className={drawerInputCls} />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Effective from">
            <input type="date" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} disabled={published} className={drawerInputCls} />
          </Field>
          <Field label="Effective to">
            <input type="date" value={effectiveTo} onChange={(e) => setEffectiveTo(e.target.value)} disabled={published} className={drawerInputCls} />
          </Field>
        </div>
        <label className="flex items-center gap-2 text-sm text-gray-700">
          <input type="checkbox" checked={isDefault} disabled={published} onChange={(e) => setIsDefault(e.target.checked)} className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30" />
          Default list for this scope
        </label>
        <Field label="Notes">
          <textarea rows={2} value={notes} disabled={published} onChange={(e) => setNotes(e.target.value)} className={drawerInputCls} />
        </Field>
      </DrawerSection>

      {/* Priced items */}
      <DrawerSection title={`Priced items (${rows.length})`}>
        {itemsLoading ? (
          <p className="text-sm text-gray-400">Loading items…</p>
        ) : rows.length === 0 ? (
          <p className="text-sm text-gray-400">No priced items yet. Add the first one below.</p>
        ) : (
          <div className="overflow-hidden rounded-xl border border-gray-100">
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-xs text-gray-500">
                <tr>
                  <th className="px-3 py-2 text-left font-medium">Label</th>
                  <th className="px-3 py-2 text-left font-medium">Item · Service</th>
                  <th className="px-3 py-2 text-right font-medium">Base</th>
                  <th className="px-3 py-2 text-right font-medium">Express</th>
                  <th className="px-3 py-2 text-right font-medium">Min</th>
                  {!published && <th className="px-3 py-2" />}
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-50">
                {rows.map((r) => (
                  <tr key={r.id} className={editingRowId === r.id ? 'bg-lg-green/5' : ''}>
                    <td className="px-3 py-2 font-medium text-gray-800">{r.displayLabel ?? '—'}</td>
                    <td className="px-3 py-2 text-gray-500">{itemName(r.itemId)} · {serviceName(r.serviceId)}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{formatCurrency(r.basePrice, priceList.currencyCode)}</td>
                    <td className="px-3 py-2 text-right tabular-nums text-gray-500">{r.expressPrice != null ? formatCurrency(r.expressPrice, priceList.currencyCode) : '—'}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{r.minimumQuantity}</td>
                    {!published && (
                      <td className="px-3 py-2">
                        <div className="flex justify-end gap-1">
                          <button type="button" onClick={() => startEdit(r)} className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-700" title="Edit">
                            <Pencil className="h-3.5 w-3.5" />
                          </button>
                          <button type="button" onClick={() => void removeRow(r)} className="rounded p-1 text-gray-400 hover:bg-red-50 hover:text-red-600" title="Remove">
                            <Trash2 className="h-3.5 w-3.5" />
                          </button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Add / edit row form */}
        {!published && (
          <div className="space-y-3 rounded-xl border border-gray-100 bg-gray-50/60 p-3">
            <p className="text-xs font-semibold text-gray-600">
              {editingRowId ? 'Edit priced item' : 'Add a priced item'}
            </p>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Item *">
                <select value={draft.itemId} onChange={(e) => onPickItem(e.target.value)} disabled={!!editingRowId} className={drawerInputCls}>
                  <option value="">Select item…</option>
                  {catalogItems.map((i: ItemDto) => (<option key={i.id} value={i.id}>{i.name}</option>))}
                </select>
              </Field>
              <Field label="Service *">
                <select value={draft.serviceId} onChange={(e) => onPickService(e.target.value)} disabled={!!editingRowId} className={drawerInputCls}>
                  <option value="">Select service…</option>
                  {services.map((s: ServiceDto) => (<option key={s.id} value={s.id}>{s.name}</option>))}
                </select>
              </Field>
            </div>
            <Field label="Display label *" hint="Shown to customers — never leave blank.">
              <input value={draft.displayLabel} onChange={(e) => setDraft((d) => ({ ...d, displayLabel: e.target.value }))} className={drawerInputCls} placeholder="Shirt – Wash & Fold" />
            </Field>
            <div className="grid grid-cols-3 gap-3">
              <Field label={`Base price (${priceList.currencyCode}) *`}>
                <input type="number" min="0" step="0.01" value={draft.basePrice} onChange={(e) => setDraft((d) => ({ ...d, basePrice: e.target.value }))} className={drawerInputCls} placeholder="49" />
              </Field>
              <Field label="Express price">
                <input type="number" min="0" step="0.01" value={draft.expressPrice} onChange={(e) => setDraft((d) => ({ ...d, expressPrice: e.target.value }))} className={drawerInputCls} placeholder="Optional" />
              </Field>
              <Field label="Min qty *">
                <input type="number" min="1" step="1" value={draft.minimumQuantity} onChange={(e) => setDraft((d) => ({ ...d, minimumQuantity: e.target.value }))} className={drawerInputCls} />
              </Field>
            </div>
            <div className="grid grid-cols-2 items-end gap-3">
              <Field label="Tax rate %">
                <input type="number" min="0" max="100" step="0.01" value={draft.taxRatePercent} onChange={(e) => setDraft((d) => ({ ...d, taxRatePercent: e.target.value }))} className={drawerInputCls} />
              </Field>
              <label className="flex items-center gap-2 pb-2 text-sm text-gray-700">
                <input type="checkbox" checked={draft.isTaxable} onChange={(e) => setDraft((d) => ({ ...d, isTaxable: e.target.checked }))} className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30" />
                Taxable
              </label>
            </div>
            {rowError && (
              <p className="text-xs text-red-600">{rowError}</p>
            )}
            <div className="flex justify-end gap-2">
              {editingRowId && (
                <button type="button" onClick={() => { setEditingRowId(null); setDraft(blankRow()); setRowError(null) }} className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-gray-600 hover:bg-gray-50">
                  <X className="h-3.5 w-3.5" /> Cancel
                </button>
              )}
              <button
                type="button"
                onClick={() => void (editingRowId ? saveEdit() : addRow())}
                disabled={createItem.isPending || updateItem.isPending}
                className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-3 py-1.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
              >
                {editingRowId ? <><Check className="h-3.5 w-3.5" /> Save row</> : <><Plus className="h-3.5 w-3.5" /> Add item</>}
              </button>
            </div>
          </div>
        )}
      </DrawerSection>
    </FormDrawer>
  )
}
