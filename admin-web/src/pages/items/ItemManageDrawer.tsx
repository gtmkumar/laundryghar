import { useMemo, useState } from 'react'
import { Plus, Save, Shirt, Pencil, AlertTriangle, Layers, Ruler } from 'lucide-react'
import {
  useCreateItem,
  useUpdateItem,
  useSaveItemPricing,
  useItemGroups,
  useFabricTypes,
  useServicesInfinite,
} from '@/hooks/useCatalog'
import {
  FormDrawer,
  DrawerSection,
  Field,
  drawerInputCls,
} from '@/components/shared/FormDrawer'
import { apiErrorMessage, apiErrorHasCode } from '@/lib/apiError'
import { cn } from '@/lib/utils'
import { useAutoCode } from '@/hooks/useAutoCode'
import type { ManagedItemDto } from '@/types/api'
import { buildNameLocalized, parseNameLocalized } from '../catalog/localized'

const checkboxCls = 'h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30'

const VISIBILITY = [
  { value: 'active', label: 'Active' },
  { value: 'draft', label: 'Draft' },
  { value: 'archived', label: 'Archived' },
] as const

interface Props {
  open: boolean
  item?: ManagedItemDto | null
  onClose: () => void
}

/**
 * "Add / edit a laundry item" — identity + fabric variants + pricing-per-service
 * + operations + visibility. On save it writes the item, then the per-service base
 * prices + fabric set to the brand working list (single round-trip each).
 */
export function ItemManageDrawer({ open, item, onClose }: Props) {
  const isEdit = !!item
  const create = useCreateItem()
  const update = useUpdateItem()
  const savePricing = useSaveItemPricing()

  const { data: groupData } = useItemGroups()
  const itemGroups = groupData?.list ?? []
  const { data: fabricData } = useFabricTypes()
  const fabrics = useMemo(
    () => (fabricData?.list ?? []).filter((f) => f.status === 'active').sort((a, b) => a.priceMultiplier - b.priceMultiplier),
    [fabricData],
  )
  const { data: serviceData } = useServicesInfinite()
  const services = useMemo(
    () => (serviceData?.pages.flatMap((p) => p.list) ?? []).filter((s) => s.status === 'active'),
    [serviceData],
  )

  const [itemGroupId, setItemGroupId] = useState('')
  const codeF = useAutoCode()
  const [name, setName] = useState('')
  const [nameHi, setNameHi] = useState('')
  const [description, setDescription] = useState('')
  const [typicalWeightGrams, setTypicalWeightGrams] = useState('')
  const [tatHours, setTatHours] = useState('')
  const [expressEligible, setExpressEligible] = useState(false)
  const [expressSurcharge, setExpressSurcharge] = useState('')
  const [aliases, setAliases] = useState('')
  const [status, setStatus] = useState('active')
  const [fabricIds, setFabricIds] = useState<Set<string>>(new Set())
  const [prices, setPrices] = useState<Record<string, string>>({})
  const [pricingMode, setPricingMode] = useState<'standard' | 'value_slab'>('standard')
  // SKU is locked on edit — changing it can break imports/exports that reference
  // the code — so it opens only behind an explicit unlock affordance.
  const [codeUnlocked, setCodeUnlocked] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Seed on open / target change (adjust-state-while-rendering, not an effect).
  const [seededFor, setSeededFor] = useState<{ open: boolean; id: string | null }>({ open, id: item?.id ?? null })
  if ((seededFor.open !== open || seededFor.id !== (item?.id ?? null)) && open) {
    setSeededFor({ open, id: item?.id ?? null })
    setError(null)
    setCodeUnlocked(false)
    if (item) {
      const loc = parseNameLocalized(item.nameLocalized)
      setItemGroupId(item.itemGroupId ?? '')
      codeF.seed(item.code, true)
      setPricingMode(item.pricingMode ?? 'standard')
      setName(item.name)
      setNameHi(loc.hi)
      setDescription(item.description ?? '')
      setTypicalWeightGrams(item.typicalWeightGrams != null ? String(item.typicalWeightGrams) : '')
      setTatHours(item.tatHours != null ? String(item.tatHours) : '')
      setExpressEligible(item.expressEligible)
      setExpressSurcharge(item.expressSurcharge != null ? String(item.expressSurcharge) : '')
      setAliases((item.aliases ?? []).join(', '))
      setStatus(item.status)
      setFabricIds(new Set(item.fabricTypeIds))
      setPrices(Object.fromEntries(item.servicePrices.map((p) => [p.serviceId, String(p.basePrice)])))
    } else {
      setItemGroupId('')
      codeF.seed('', false)
      setPricingMode('standard')
      setName('')
      setNameHi('')
      setDescription('')
      setTypicalWeightGrams('')
      setTatHours('')
      setExpressEligible(false)
      setExpressSurcharge('')
      setAliases('')
      setStatus('active')
      // Pre-select the base (×1.0) fabric, matching the mockup's default "Cotton".
      setFabricIds(new Set(fabrics.filter((f) => f.priceMultiplier === 1).map((f) => f.id)))
      setPrices({})
    }
  } else if (seededFor.open !== open) {
    setSeededFor({ open, id: item?.id ?? null })
  }

  if (!open) return null

  const toggleFabric = (id: string) =>
    setFabricIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const submit = async () => {
    setError(null)
    if (!isEdit && !codeF.code.trim()) return setError('SKU code is required.')
    if (isEdit && codeUnlocked && !codeF.code.trim()) return setError('SKU code cannot be empty.')
    if (!name.trim()) return setError('Item name is required.')

    // Only send `code` on edit when it was unlocked AND actually changed — a
    // no-op rename would needlessly risk the item_code_taken race.
    const codeChanged = isEdit && codeUnlocked && codeF.code.trim() !== item!.code

    const aliasList = aliases.split(',').map((a) => a.trim()).filter(Boolean)
    const common = {
      itemGroupId: itemGroupId || null,
      name: name.trim(),
      nameLocalized: buildNameLocalized(name.trim(), nameHi),
      description: description.trim() || null,
      iconUrl: null,
      imageUrl: null,
      typicalWeightGrams: typicalWeightGrams ? Number(typicalWeightGrams) : null,
      requiresPerSidePrice: false,
      aliases: aliasList.length ? aliasList : null,
      displayOrder: item?.displayOrder ?? 0,
      tatHours: tatHours ? Number(tatHours) : null,
      expressEligible,
      expressSurcharge: expressEligible && expressSurcharge ? Number(expressSurcharge) : null,
    }

    try {
      const saved =
        isEdit && item
          ? await update.mutateAsync({
              id: item.id,
              payload: { ...common, status, pricingMode, ...(codeChanged ? { code: codeF.code.trim() } : {}) },
            })
          : await create.mutateAsync({ ...common, code: codeF.code.trim() })

      await savePricing.mutateAsync({
        id: saved.id,
        payload: {
          servicePrices: services.map((s) => ({
            serviceId: s.id,
            basePrice: prices[s.id]?.trim() ? Number(prices[s.id]) : null,
          })),
          fabricTypeIds: [...fabricIds],
        },
      })

      // Create can't carry pricingMode (only PUT accepts it) — persist a non-standard
      // choice with a follow-up update on the freshly created item.
      if (!isEdit && pricingMode !== 'standard') {
        await update.mutateAsync({ id: saved.id, payload: { ...common, status, pricingMode } })
      }
      onClose()
    } catch (e) {
      if (apiErrorHasCode(e, 'item_code_taken')) {
        setError('That SKU code is already used by another item. Choose a different code.')
        return
      }
      setError(apiErrorMessage(e, 'Could not save the item.'))
    }
  }

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Shirt}
      eyebrow="Catalogue · Item"
      title={isEdit ? `Edit ${item!.name}` : 'Add a laundry item'}
      width="lg"
      error={error}
      onSubmit={() => void submit()}
      submitLabel={isEdit ? 'Save item' : 'Create item'}
      submittingLabel="Saving…"
      submitIcon={isEdit ? Save : Plus}
      submitting={create.isPending || update.isPending || savePricing.isPending}
    >
      <DrawerSection title="Identity">
        <Field label="Item name *">
          <input value={name} onChange={(e) => { setName(e.target.value); codeF.syncFromName(e.target.value) }} className={drawerInputCls} placeholder="e.g. Shirt" />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field
            label="SKU code *"
            hint={
              !isEdit
                ? 'Auto-filled from the name; edit to override.'
                : codeUnlocked
                ? undefined
                : 'Locked — referenced by imports & exports.'
            }
          >
            <div className="relative">
              <input
                value={codeF.code}
                onChange={(e) => codeF.setCode(e.target.value)}
                disabled={isEdit && !codeUnlocked}
                className={cn(drawerInputCls, 'font-mono', isEdit && !codeUnlocked && 'pr-16')}
                placeholder="LG-SHRT"
              />
              {isEdit && !codeUnlocked && (
                <button
                  type="button"
                  onClick={() => setCodeUnlocked(true)}
                  className="absolute right-1.5 top-1/2 inline-flex -translate-y-1/2 items-center gap-1 rounded-md px-2 py-1 text-xs font-medium text-lg-green hover:bg-lg-green/10"
                >
                  <Pencil className="h-3 w-3" /> Edit
                </button>
              )}
            </div>
            {isEdit && codeUnlocked && (
              <p className="mt-1 flex items-start gap-1 text-xs text-amber-700">
                <AlertTriangle className="mt-0.5 h-3 w-3 shrink-0" />
                Changing the code affects imports and exports that reference it.
              </p>
            )}
          </Field>
          <Field label="Category">
            <select value={itemGroupId} onChange={(e) => setItemGroupId(e.target.value)} className={drawerInputCls}>
              <option value="">No category</option>
              {itemGroups.map((g) => (<option key={g.id} value={g.id}>{g.name}</option>))}
            </select>
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Localized name (HI)">
            <input value={nameHi} onChange={(e) => setNameHi(e.target.value)} className={drawerInputCls} placeholder="शर्ट" />
          </Field>
          <Field label="Aliases" hint="Comma-separated synonyms.">
            <input value={aliases} onChange={(e) => setAliases(e.target.value)} className={drawerInputCls} placeholder="tee, t-shirt" />
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="Fabric variants">
        <p className="-mt-1 text-xs text-gray-500">
          Each fabric carries its own price multiplier on top of the base (Cotton) rate.
        </p>
        <div className="flex flex-wrap gap-2">
          {fabrics.length === 0 ? (
            <span className="text-sm text-gray-400">No fabric types yet — add them on the Pricing page.</span>
          ) : fabrics.map((f) => {
            const on = fabricIds.has(f.id)
            return (
              <button
                key={f.id}
                type="button"
                onClick={() => toggleFabric(f.id)}
                className={cn(
                  'inline-flex items-center gap-1 rounded-full border px-3 py-1 text-sm font-medium transition-colors',
                  on ? 'border-lg-green bg-lg-green/10 text-lg-green' : 'border-gray-200 text-gray-500 hover:bg-gray-50',
                )}
              >
                {f.name}
                <span className="text-xs font-normal opacity-70">×{f.priceMultiplier.toFixed(2)}</span>
              </button>
            )
          })}
        </div>
      </DrawerSection>

      <DrawerSection title="Pricing mode">
        <div className="inline-flex rounded-lg border border-gray-200 p-0.5">
          {[
            { value: 'standard' as const, label: 'Standard', icon: Ruler },
            { value: 'value_slab' as const, label: 'By declared garment value', icon: Layers },
          ].map((m) => (
            <button
              key={m.value}
              type="button"
              onClick={() => setPricingMode(m.value)}
              className={cn(
                'inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                pricingMode === m.value ? 'bg-lg-green text-white' : 'text-gray-500 hover:text-gray-700',
              )}
            >
              <m.icon className="h-3.5 w-3.5" />
              {m.label}
            </button>
          ))}
        </div>
        {pricingMode === 'value_slab' && (
          <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2.5 text-xs text-amber-800">
            <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            <span>
              The per-service prices below are ignored for this item. Its price comes from the value slabs
              configured on the Pricing page, matched against the customer’s declared garment value.
            </span>
          </div>
        )}
      </DrawerSection>

      <DrawerSection title="Pricing per service">
        <p className="-mt-1 text-xs text-gray-500">
          Leave blank to hide this item from a service. Prices are the base (Cotton) rate.
        </p>
        {services.length === 0 ? (
          <p className="text-sm text-gray-400">No active services yet — add them under Services.</p>
        ) : (
          <div className="space-y-2">
            {services.map((s) => (
              <div key={s.id} className="flex items-center gap-3 rounded-lg border border-gray-100 px-3 py-2">
                <span className="flex-1 text-sm font-medium text-gray-700">{s.name}</span>
                <div className="relative w-28">
                  <span className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-sm text-gray-400">₹</span>
                  <input
                    type="number"
                    min="0"
                    step="0.01"
                    value={prices[s.id] ?? ''}
                    onChange={(e) => setPrices((p) => ({ ...p, [s.id]: e.target.value }))}
                    className={`${drawerInputCls} pl-6 text-right tabular-nums`}
                    placeholder="—"
                  />
                </div>
              </div>
            ))}
          </div>
        )}
      </DrawerSection>

      <DrawerSection title="Operations">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Turnaround (hours)">
            <input type="number" min="0" step="1" value={tatHours} onChange={(e) => setTatHours(e.target.value)} className={drawerInputCls} placeholder="24" />
          </Field>
          <Field label="Typical weight (g)" hint="For per-kg estimates.">
            <input type="number" min="0" step="1" value={typicalWeightGrams} onChange={(e) => setTypicalWeightGrams(e.target.value)} className={drawerInputCls} placeholder="200" />
          </Field>
        </div>
        <label className="flex items-center gap-2 text-sm text-gray-700">
          <input type="checkbox" checked={expressEligible} onChange={(e) => setExpressEligible(e.target.checked)} className={checkboxCls} />
          Express eligible
        </label>
        {expressEligible && (
          <Field label="Express surcharge (₹)">
            <input type="number" min="0" step="0.01" value={expressSurcharge} onChange={(e) => setExpressSurcharge(e.target.value)} className={drawerInputCls} placeholder="50" />
          </Field>
        )}
      </DrawerSection>

      <DrawerSection title="Visibility">
        <div className="inline-flex rounded-lg border border-gray-200 p-0.5">
          {VISIBILITY.map((v) => (
            <button
              key={v.value}
              type="button"
              onClick={() => setStatus(v.value)}
              className={cn(
                'rounded-md px-4 py-1.5 text-sm font-medium transition-colors',
                status === v.value ? 'bg-lg-green text-white' : 'text-gray-500 hover:text-gray-700',
              )}
            >
              {v.label}
            </button>
          ))}
        </div>
      </DrawerSection>
    </FormDrawer>
  )
}
