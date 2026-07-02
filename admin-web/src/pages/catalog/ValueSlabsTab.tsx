import { useMemo, useState } from 'react'
import { Loader2, Plus, Pencil, Archive, Layers, Info } from 'lucide-react'
import { FormDrawer, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import { useConfirm } from '@/components/shared/useConfirm'
import {
  useValueSlabs,
  useCreateValueSlab,
  useUpdateValueSlab,
  useDeleteValueSlab,
  useServicesInfinite,
} from '@/hooks/useCatalog'
import { usePermissions } from '@/hooks/usePermissions'
import { showToast } from '@/stores/toastStore'
import { apiErrorHasCode, apiErrorMessage } from '@/lib/apiError'
import { cn } from '@/lib/utils'
import type { ValueSlabDto, ValueSlabStatus } from '@/types/api'

const BRAND_WIDE = '__brand__'

/** ₹ with Indian grouping and no decimals (slab bounds are whole rupees). */
function rupees(n: number): string {
  return `₹${n.toLocaleString('en-IN', { maximumFractionDigits: 0 })}`
}

/** "₹10,000 – ₹30,000" or "₹90,000+" when open-ended. */
function rangeLabel(s: ValueSlabDto): string {
  return s.maxValue == null ? `${rupees(s.minValue)}+` : `${rupees(s.minValue)} – ${rupees(s.maxValue)}`
}

function StatusBadge({ status }: { status: ValueSlabStatus }) {
  const cls =
    status === 'active'
      ? 'bg-emerald-50 text-emerald-700'
      : status === 'inactive'
      ? 'bg-amber-50 text-amber-700'
      : 'bg-gray-100 text-gray-500'
  return <span className={cn('rounded-full px-2 py-0.5 text-xs capitalize', cls)}>{status}</span>
}

interface SlabForm {
  lane: string // BRAND_WIDE or a serviceId
  minValue: string
  maxValue: string
  price: string
  status: ValueSlabStatus
}

function blankForm(): SlabForm {
  return { lane: BRAND_WIDE, minValue: '', maxValue: '', price: '', status: 'active' }
}

export function ValueSlabsTab() {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('pricing.item.manage')

  const [includeArchived, setIncludeArchived] = useState(false)
  const [laneFilter, setLaneFilter] = useState<string>('all') // 'all' | BRAND_WIDE | serviceId

  const { data: slabs, isLoading } = useValueSlabs({ includeArchived })
  const { data: serviceData } = useServicesInfinite()
  const services = useMemo(
    () => (serviceData?.pages.flatMap((p) => p.list) ?? []).filter((s) => s.status === 'active'),
    [serviceData],
  )

  const create = useCreateValueSlab()
  const update = useUpdateValueSlab()
  const del = useDeleteValueSlab()
  const gate = useConfirm()

  const [editing, setEditing] = useState<ValueSlabDto | 'new' | null>(null)
  const [form, setForm] = useState<SlabForm>(blankForm())
  const [err, setErr] = useState<string | null>(null)

  const rows = useMemo(() => {
    const all = slabs ?? []
    const filtered = all.filter((s) => {
      if (laneFilter === 'all') return true
      if (laneFilter === BRAND_WIDE) return s.serviceId == null
      return s.serviceId === laneFilter
    })
    return filtered
      .slice()
      .sort((a, b) => a.minValue - b.minValue || (a.serviceName ?? '').localeCompare(b.serviceName ?? ''))
  }, [slabs, laneFilter])

  const laneName = (s: ValueSlabDto) => s.serviceName ?? 'Brand-wide'

  const open = (s: ValueSlabDto | 'new') => {
    setErr(null)
    setEditing(s)
    if (s === 'new') setForm(blankForm())
    else
      setForm({
        lane: s.serviceId ?? BRAND_WIDE,
        minValue: String(s.minValue),
        maxValue: s.maxValue != null ? String(s.maxValue) : '',
        price: String(s.price),
        status: s.status,
      })
  }

  const submit = async () => {
    setErr(null)
    const minValue = Number(form.minValue)
    const price = Number(form.price)
    const hasMax = form.maxValue.trim() !== ''
    const maxValue = hasMax ? Number(form.maxValue) : null
    if (!Number.isFinite(minValue) || minValue < 0) return setErr('Enter a valid minimum value.')
    if (hasMax && (!Number.isFinite(maxValue!) || maxValue! <= minValue))
      return setErr('Maximum must be greater than the minimum (leave blank for open-ended).')
    if (!Number.isFinite(price) || price < 0) return setErr('Enter a valid price.')

    const serviceId = form.lane === BRAND_WIDE ? null : form.lane
    try {
      if (editing === 'new') {
        await create.mutateAsync({ serviceId, minValue, maxValue, price })
      } else if (editing) {
        await update.mutateAsync({
          id: editing.id,
          payload: { serviceId, minValue, maxValue, price, status: form.status },
        })
      }
      setEditing(null)
      showToast('success', 'Value slab saved.')
    } catch (e) {
      if (apiErrorHasCode(e, 'value_slab_overlap')) {
        setErr('This range overlaps an existing slab in the same lane. Adjust the min/max so lanes don’t overlap.')
        return
      }
      setErr(apiErrorMessage(e, 'Could not save the value slab.'))
    }
  }

  const archive = (s: ValueSlabDto) =>
    gate.confirm({
      title: 'Archive value slab?',
      description: `The ${rangeLabel(s)} slab (${laneName(s)}) will be archived and stop applying to new orders.`,
      confirmLabel: 'Archive',
      tone: 'warning',
      onConfirm: async () => {
        try {
          await del.mutateAsync(s.id)
          showToast('success', 'Value slab archived.')
        } catch (e) {
          showToast('error', apiErrorMessage(e, 'Could not archive the slab.'))
        }
      },
    })

  if (isLoading)
    return (
      <div className="flex items-center justify-center py-20 text-gray-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading…
      </div>
    )

  return (
    <div>
      <div className="mb-3 flex items-start justify-between gap-4">
        <p className="text-sm text-gray-500">
          Price branded garments by the customer’s declared value instead of a per-service rate. A garment’s
          value falls into a slab, and the slab’s price applies.
        </p>
        {canManage && (
          <button
            type="button"
            onClick={() => open('new')}
            className="inline-flex shrink-0 items-center gap-1.5 rounded-lg bg-lg-green px-3 py-1.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
          >
            <Plus className="h-3.5 w-3.5" /> Add slab
          </button>
        )}
      </div>

      <div className="mb-3 flex items-start gap-2 rounded-lg border border-blue-100 bg-blue-50/60 px-3 py-2 text-xs text-blue-800">
        <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
        <span>
          When a garment matches both a service-specific slab and a brand-wide slab, the{' '}
          <span className="font-semibold">service-specific</span> lane wins. Brand-wide slabs are the fallback
          for services without their own lane.
        </span>
      </div>

      {/* Lane filter */}
      <div className="mb-3 flex flex-wrap items-center gap-1.5">
        <span className="mr-1 text-xs text-gray-500">Lane</span>
        {[
          { id: 'all', label: 'All lanes' },
          { id: BRAND_WIDE, label: 'Brand-wide' },
          ...services.map((s) => ({ id: s.id, label: s.name })),
        ].map((opt) => (
          <button
            key={opt.id}
            type="button"
            onClick={() => setLaneFilter(opt.id)}
            className={cn(
              'rounded-full px-2.5 py-1 text-xs font-medium',
              laneFilter === opt.id ? 'bg-lg-green/10 text-lg-green' : 'bg-gray-100 text-gray-600 hover:bg-gray-200',
            )}
          >
            {opt.label}
          </button>
        ))}
        <label className="ml-auto flex items-center gap-1.5 text-xs text-gray-500">
          <input
            type="checkbox"
            checked={includeArchived}
            onChange={(e) => setIncludeArchived(e.target.checked)}
            className="h-3.5 w-3.5 rounded border-gray-300 text-lg-green focus:ring-lg-green/30"
          />
          Show archived
        </label>
      </div>

      {rows.length === 0 ? (
        <p className="py-12 text-center text-sm text-gray-400">No value slabs in this lane yet.</p>
      ) : (
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-100 text-left text-[11px] font-semibold uppercase tracking-wide text-gray-500">
              <th className="px-4 py-2.5">Declared value range</th>
              <th className="px-4 py-2.5">Lane</th>
              <th className="px-4 py-2.5 text-right">Price</th>
              <th className="px-4 py-2.5">Status</th>
              {canManage && <th className="px-4 py-2.5" />}
            </tr>
          </thead>
          <tbody>
            {rows.map((s) => (
              <tr key={s.id} className="border-b border-gray-50 last:border-0">
                <td className="px-4 py-2.5">
                  <span className="inline-flex items-center gap-1.5 font-medium tabular-nums text-gray-800">
                    <Layers className="h-3.5 w-3.5 text-gray-400" />
                    {rangeLabel(s)}
                  </span>
                </td>
                <td className="px-4 py-2.5">
                  {s.serviceId == null ? (
                    <span className="rounded bg-gray-100 px-1.5 py-0.5 text-xs text-gray-600">Brand-wide</span>
                  ) : (
                    <span className="text-gray-600">{laneName(s)}</span>
                  )}
                </td>
                <td className="px-4 py-2.5 text-right font-medium tabular-nums text-gray-900">{rupees(s.price)}</td>
                <td className="px-4 py-2.5">
                  <StatusBadge status={s.status} />
                </td>
                {canManage && (
                  <td className="px-4 py-2.5 text-right">
                    <div className="inline-flex gap-1">
                      <button
                        type="button"
                        onClick={() => open(s)}
                        title="Edit"
                        className="rounded p-1 text-gray-500 hover:bg-gray-100"
                      >
                        <Pencil className="h-3.5 w-3.5" />
                      </button>
                      {s.status !== 'archived' && (
                        <button
                          type="button"
                          onClick={() => archive(s)}
                          title="Archive"
                          className="rounded p-1 text-red-500 hover:bg-red-50"
                        >
                          <Archive className="h-3.5 w-3.5" />
                        </button>
                      )}
                    </div>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <FormDrawer
        open={editing !== null}
        onClose={() => setEditing(null)}
        icon={Layers}
        eyebrow="Pricing · Value slab"
        title={editing === 'new' ? 'Add value slab' : 'Edit value slab'}
        width="sm"
        error={err}
        onSubmit={submit}
        submitLabel="Save"
        submittingLabel="Saving…"
        submitIcon={Layers}
        submitting={create.isPending || update.isPending}
      >
        <div className="space-y-3">
          <Field label="Lane" hint="Brand-wide applies to any service without its own slab.">
            <select
              value={form.lane}
              onChange={(e) => setForm((f) => ({ ...f, lane: e.target.value }))}
              className={drawerInputCls}
            >
              <option value={BRAND_WIDE}>Brand-wide (all services)</option>
              {services.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </Field>
          <div className="grid grid-cols-2 gap-3">
            <Field label="Min value (₹)">
              <input
                value={form.minValue}
                onChange={(e) => setForm((f) => ({ ...f, minValue: e.target.value }))}
                inputMode="decimal"
                className={drawerInputCls}
                placeholder="10000"
              />
            </Field>
            <Field label="Max value (₹)" hint="Blank = open-ended.">
              <input
                value={form.maxValue}
                onChange={(e) => setForm((f) => ({ ...f, maxValue: e.target.value }))}
                inputMode="decimal"
                className={drawerInputCls}
                placeholder="30000"
              />
            </Field>
          </div>
          <Field label="Price (₹)" hint="Charged when a garment’s declared value falls in this range.">
            <input
              value={form.price}
              onChange={(e) => setForm((f) => ({ ...f, price: e.target.value }))}
              inputMode="decimal"
              className={drawerInputCls}
              placeholder="450"
            />
          </Field>
          <p className="text-xs text-gray-400">
            Ranges are half-open: a garment worth exactly the max value falls into the next slab.
          </p>
          {editing !== 'new' && (
            <Field label="Status">
              <select
                value={form.status}
                onChange={(e) => setForm((f) => ({ ...f, status: e.target.value as ValueSlabStatus }))}
                className={drawerInputCls}
              >
                <option value="active">Active</option>
                <option value="inactive">Inactive</option>
                <option value="archived">Archived</option>
              </select>
            </Field>
          )}
        </div>
      </FormDrawer>
      <ConfirmDialog {...gate.dialogProps} />
    </div>
  )
}
