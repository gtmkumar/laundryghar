import { useState } from 'react'
import {
  Loader2, Save, SlidersHorizontal, Building2, Store as StoreIcon, Globe2,
  Trash2, X, Check,
} from 'lucide-react'
import {
  useBusinessSettings,
  useUpsertBusinessSetting,
  useClearBusinessSetting,
} from '@/hooks/useBusinessSettings'
import { useFranchises, useStores } from '@/hooks/useTenancy'
import { useEffectiveBrandId } from '@/hooks/useBrandContext'
import { useCanManageSettings } from '@/hooks/usePermissions'
import { apiErrorMessage } from '@/lib/apiError'
import { cn } from '@/lib/utils'
import type {
  SettingRow, EffectiveSetting, SettingDataType, SettingScopeType,
} from '@/types/api'

// ── Key catalog ────────────────────────────────────────────────────────────────
// Static because the set of tunable business rules is code-owned; the API only
// carries their values, not their labels/units.

type Unit = 'percent' | 'rupee' | 'hours' | 'minutes' | 'km' | 'none'

interface KeyMeta {
  key: string
  label: string
  description: string
  dataType: SettingDataType
  unit: Unit
}

interface CategoryMeta {
  category: string
  title: string
  keys: KeyMeta[]
}

const CATEGORIES: CategoryMeta[] = [
  {
    category: 'orders',
    title: 'Orders & checkout',
    keys: [
      { key: 'tax_rate_percent', label: 'Tax rate', description: 'GST/tax applied to order subtotals.', dataType: 'decimal', unit: 'percent' },
      { key: 'express_surcharge_percent', label: 'Express surcharge', description: 'Extra charged on express-tier orders.', dataType: 'decimal', unit: 'percent' },
      { key: 'default_tat_hours', label: 'Standard turnaround', description: 'Default promised turnaround for standard orders.', dataType: 'int', unit: 'hours' },
      { key: 'express_tat_hours', label: 'Express turnaround', description: 'Promised turnaround for express orders.', dataType: 'int', unit: 'hours' },
      { key: 'currency_code', label: 'Currency', description: 'ISO currency code used across the brand.', dataType: 'string', unit: 'none' },
      { key: 'min_order_value', label: 'Minimum order value', description: 'Orders below this are blocked at checkout.', dataType: 'decimal', unit: 'rupee' },
      { key: 'cancellation_fee', label: 'Cancellation fee', description: 'Charged when a customer cancels outside the free window.', dataType: 'decimal', unit: 'rupee' },
      { key: 'cancellation_free_window_minutes', label: 'Free cancellation window', description: 'Grace period after placing an order for a free cancellation.', dataType: 'int', unit: 'minutes' },
    ],
  },
  {
    category: 'catalog',
    title: 'Catalogue',
    keys: [
      { key: 'high_value_garment_threshold', label: 'High-value garment threshold', description: 'Garments priced above this are flagged as high-value.', dataType: 'decimal', unit: 'rupee' },
    ],
  },
  {
    category: 'logistics',
    title: 'Logistics',
    keys: [
      { key: 'free_pickup_radius_km', label: 'Free pickup radius', description: 'Pickups within this distance carry no travel fee.', dataType: 'decimal', unit: 'km' },
      { key: 'waiting_free_minutes', label: 'Free waiting time', description: 'Rider waiting time included before charges begin.', dataType: 'int', unit: 'minutes' },
      { key: 'waiting_per_minute_rate', label: 'Waiting charge', description: 'Charged per minute once free waiting time is used up.', dataType: 'decimal', unit: 'rupee' },
    ],
  },
]

// ── Scope model ──────────────────────────────────────────────────────────────

type ScopeType = 'brand' | 'franchise' | 'store'

interface Scope {
  scopeType: ScopeType
  franchiseId?: string
  storeId?: string
}

// ── Formatting helpers ────────────────────────────────────────────────────────

function formatValue(value: string, unit: Unit, dataType: SettingDataType): string {
  if (dataType === 'bool') return value === 'true' ? 'On' : 'Off'
  switch (unit) {
    case 'rupee': return `₹${value}`
    case 'percent': return `${value}%`
    case 'hours': return `${value} hrs`
    case 'minutes': return `${value} min`
    case 'km': return `${value} km`
    default: return value
  }
}

const SCOPE_LABEL: Record<SettingScopeType, string> = {
  platform: 'Platform default',
  brand: 'Brand',
  franchise: 'Franchise',
  store: 'Store',
}

interface Band { min?: number; max?: number }

function parseBand(schema: string | null | undefined): Band | null {
  if (!schema) return null
  try {
    const o = JSON.parse(schema) as { min?: number; max?: number }
    const band: Band = {}
    if (typeof o.min === 'number') band.min = o.min
    if (typeof o.max === 'number') band.max = o.max
    return band.min == null && band.max == null ? null : band
  } catch {
    return null
  }
}

function bandText(band: Band | null): string | null {
  if (!band) return null
  if (band.min != null && band.max != null) return `Allowed: ${band.min}–${band.max}`
  if (band.min != null) return `Allowed: ≥ ${band.min}`
  if (band.max != null) return `Allowed: ≤ ${band.max}`
  return null
}

const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'

// ── Panel ────────────────────────────────────────────────────────────────────

export function BusinessRulesPanel() {
  const brandId = useEffectiveBrandId()
  const canManage = useCanManageSettings()

  const [scope, setScope] = useState<Scope>({ scopeType: 'brand' })

  const franchisesQuery = useFranchises({ brandId: brandId ?? undefined, pageSize: 100 })
  const storesQuery = useStores(
    { brandId: brandId ?? undefined, pageSize: 100 },
    scope.scopeType === 'store',
  )

  const setScopeType = (scopeType: ScopeType) =>
    setScope({ scopeType }) // reset any franchise/store selection when the axis changes

  // A franchise/store scope isn't "ready" (queries stay disabled) until its id is picked.
  const scopeReady =
    scope.scopeType === 'brand' ||
    (scope.scopeType === 'franchise' && !!scope.franchiseId) ||
    (scope.scopeType === 'store' && !!scope.storeId)

  return (
    <div className="space-y-6">
      <div className="flex items-start gap-2.5">
        <span className="mt-0.5 flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
          <SlidersHorizontal className="h-4 w-4" />
        </span>
        <div>
          <h2 className="text-lg font-bold text-gray-900">Business rules</h2>
          <p className="text-sm text-gray-500">
            Tune pricing, turnaround, and logistics limits. Values inherit brand → franchise → store; set an
            override at any level, and cap the range franchises and stores may choose from at the brand level.
          </p>
        </div>
      </div>

      {/* Scope switcher */}
      <div className="rounded-2xl border border-gray-200 bg-white p-4 space-y-3">
        <p className="text-[11px] font-semibold uppercase tracking-wide text-gray-400">Editing scope</p>
        <div className="flex flex-wrap items-center gap-2">
          <ScopeTab active={scope.scopeType === 'brand'} onClick={() => setScopeType('brand')} icon={Building2} label="Brand" />
          <ScopeTab active={scope.scopeType === 'franchise'} onClick={() => setScopeType('franchise')} icon={StoreIcon} label="Franchise" />
          <ScopeTab active={scope.scopeType === 'store'} onClick={() => setScopeType('store')} icon={StoreIcon} label="Store" />
        </div>

        {scope.scopeType === 'franchise' && (
          <select
            value={scope.franchiseId ?? ''}
            onChange={(e) => setScope({ scopeType: 'franchise', franchiseId: e.target.value || undefined })}
            className={inputCls}
          >
            <option value="">Select a franchise…</option>
            {(franchisesQuery.data?.list ?? []).map((f) => (
              <option key={f.id} value={f.id}>{f.legalName} ({f.code})</option>
            ))}
          </select>
        )}

        {scope.scopeType === 'store' && (
          <select
            value={scope.storeId ?? ''}
            onChange={(e) => setScope({ scopeType: 'store', storeId: e.target.value || undefined })}
            className={inputCls}
          >
            <option value="">Select a store…</option>
            {(storesQuery.data?.list ?? []).map((s) => (
              <option key={s.id} value={s.id}>{s.name} ({s.code}) · {s.city}</option>
            ))}
          </select>
        )}

        <p className="text-xs text-gray-500">
          {scope.scopeType === 'brand'
            ? 'Brand-wide defaults and the allowed range for every franchise and store.'
            : scope.scopeType === 'franchise'
              ? 'Overrides for one franchise. Values must stay within the brand’s allowed range.'
              : 'Overrides for one store. Values must stay within the brand’s allowed range.'}
        </p>
      </div>

      {!scopeReady ? (
        <div className="rounded-2xl border border-dashed border-gray-200 bg-white py-16 text-center text-sm text-gray-400">
          Choose a {scope.scopeType} above to view and edit its settings.
        </div>
      ) : (
        CATEGORIES.map((cat) => (
          <CategoryCard key={cat.category} meta={cat} scope={scope} canManage={canManage} />
        ))
      )}
    </div>
  )
}

function ScopeTab({ active, onClick, icon: Icon, label }: {
  active: boolean
  onClick: () => void
  icon: React.ElementType
  label: string
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
        active ? 'border-lg-green bg-lg-green/10 text-lg-green' : 'border-gray-200 text-gray-600 hover:bg-gray-50',
      )}
    >
      <Icon className="h-3.5 w-3.5" /> {label}
    </button>
  )
}

// ── One category's rows ────────────────────────────────────────────────────────

function CategoryCard({ meta, scope, canManage }: {
  meta: CategoryMeta
  scope: Scope
  canManage: boolean
}) {
  // Rows + resolved values at the current scope.
  const scopedQuery = useBusinessSettings(
    { category: meta.category, franchiseId: scope.franchiseId, storeId: scope.storeId },
    true,
  )
  // Brand-level rows (only when we're below brand) — they carry the clamp bands
  // we display as guidance and validate against.
  const brandQuery = useBusinessSettings(
    { category: meta.category },
    scope.scopeType !== 'brand',
  )

  const isLoading = scopedQuery.isLoading || (scope.scopeType !== 'brand' && brandQuery.isLoading)

  if (isLoading) {
    return (
      <div className="flex items-center justify-center rounded-2xl border border-gray-200 bg-white py-12 text-gray-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading {meta.title.toLowerCase()}…
      </div>
    )
  }
  if (scopedQuery.isError) {
    return (
      <div className="rounded-2xl border border-gray-200 bg-white py-12 text-center text-sm text-red-600">
        Could not load {meta.title.toLowerCase()}.
      </div>
    )
  }

  const rows = scopedQuery.data?.rows ?? []
  const effective = scopedQuery.data?.effective ?? []
  const brandRows = scope.scopeType === 'brand' ? rows : (brandQuery.data?.rows ?? [])

  const rowFor = (key: string): SettingRow | undefined =>
    rows.find(
      (r) =>
        r.key === key &&
        r.scopeType === scope.scopeType &&
        (r.franchiseId ?? null) === (scope.franchiseId ?? null) &&
        (r.storeId ?? null) === (scope.storeId ?? null),
    )
  const effectiveFor = (key: string): EffectiveSetting | undefined => effective.find((e) => e.key === key)
  const brandSchemaFor = (key: string): string | null =>
    brandRows.find((r) => r.key === key && r.scopeType === 'brand')?.validationSchema ?? null

  return (
    <div className="rounded-2xl border border-gray-200 bg-white p-5 space-y-1">
      <p className="mb-3 text-[11px] font-semibold uppercase tracking-wide text-gray-400">{meta.title}</p>
      <div className="divide-y divide-gray-100">
        {meta.keys.map((k) => (
          <SettingRowEditor
            key={`${scope.scopeType}:${scope.franchiseId ?? ''}:${scope.storeId ?? ''}:${k.key}`}
            meta={k}
            category={meta.category}
            scope={scope}
            canManage={canManage}
            row={rowFor(k.key)}
            effective={effectiveFor(k.key)}
            brandSchema={brandSchemaFor(k.key)}
          />
        ))}
      </div>
    </div>
  )
}

// ── One editable key row ────────────────────────────────────────────────────────

function SettingRowEditor({ meta, category, scope, canManage, row, effective, brandSchema }: {
  meta: KeyMeta
  category: string
  scope: Scope
  canManage: boolean
  row: SettingRow | undefined
  effective: EffectiveSetting | undefined
  brandSchema: string | null
}) {
  const upsert = useUpsertBusinessSetting()
  const clear = useClearBusinessSetting()

  const rowValue = row?.value ?? ''
  const [draft, setDraft] = useState(rowValue)
  // Re-seed when the stored value changes (post-save/clear refetch).
  const [seed, setSeed] = useState(rowValue)
  if (seed !== rowValue) {
    setSeed(rowValue)
    setDraft(rowValue)
  }

  const [error, setError] = useState<string | null>(null)
  const [savedAt, setSavedAt] = useState<string | null>(null)
  const [bandOpen, setBandOpen] = useState(false)

  const isNumeric = meta.dataType === 'decimal' || meta.dataType === 'int'
  const band = parseBand(brandSchema)
  const hasRowAtScope = !!row

  const validate = (value: string): string | null => {
    if (value.trim() === '') return null // clearing the value
    if (isNumeric) {
      const n = Number(value)
      if (Number.isNaN(n)) return 'Enter a number.'
      if (meta.dataType === 'int' && !Number.isInteger(n)) return 'Enter a whole number.'
      if (n < 0) return 'Value can’t be negative.'
      if (band && scope.scopeType !== 'brand') {
        if (band.min != null && n < band.min) return `Must be at least ${band.min}.`
        if (band.max != null && n > band.max) return `Must be at most ${band.max}.`
      }
    }
    return null
  }

  const save = async () => {
    setError(null); setSavedAt(null)
    const problem = validate(draft)
    if (problem) return setError(problem)
    const value = draft.trim() === '' ? null : draft.trim()
    try {
      await upsert.mutateAsync({
        category,
        key: meta.key,
        scopeType: scope.scopeType,
        franchiseId: scope.franchiseId,
        storeId: scope.storeId,
        value,
        dataType: meta.dataType,
        // Preserve the existing clamp band on a brand-scope value save.
        ...(scope.scopeType === 'brand' && brandSchema ? { validationSchema: brandSchema } : {}),
      })
      setSavedAt(new Date().toLocaleTimeString())
    } catch (e) {
      setError(apiErrorMessage(e, 'Could not save this setting.'))
    }
  }

  const clearRow = async () => {
    setError(null); setSavedAt(null)
    try {
      await clear.mutateAsync({
        category,
        key: meta.key,
        scopeType: scope.scopeType,
        franchiseId: scope.franchiseId,
        storeId: scope.storeId,
      })
    } catch (e) {
      setError(apiErrorMessage(e, 'Could not clear this setting.'))
    }
  }

  const saveBand = async (schema: string | null) => {
    setError(null); setSavedAt(null)
    try {
      await upsert.mutateAsync({
        category,
        key: meta.key,
        scopeType: 'brand',
        // Keep the brand's current value; we're only changing the band.
        value: row?.value ?? null,
        dataType: meta.dataType,
        ...(schema ? { validationSchema: schema } : {}),
      })
      setBandOpen(false)
      setSavedAt(new Date().toLocaleTimeString())
    } catch (e) {
      setError(apiErrorMessage(e, 'Could not save the allowed range.'))
    }
  }

  const busy = upsert.isPending || clear.isPending
  const dirty = draft !== rowValue

  // Effective (resolved) value + where it came from.
  const effLabel = effective ? formatValue(effective.value, meta.unit, meta.dataType) : null
  const badgeScope = effective?.sourceScope ?? null

  return (
    <div className="py-3.5">
      <div className="flex flex-wrap items-start gap-x-4 gap-y-2">
        {/* Label + description + effective */}
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="text-sm font-medium text-gray-800">{meta.label}</span>
            {badgeScope && <InheritanceBadge scope={badgeScope} />}
          </div>
          <p className="mt-0.5 text-xs text-gray-500">{meta.description}</p>
          <p className="mt-1 text-xs">
            {effLabel != null ? (
              <span className="text-gray-600">
                Effective: <span className="font-medium text-gray-800">{effLabel}</span>
                {badgeScope && badgeScope !== scope.scopeType && (
                  <span className="text-gray-400"> · inherited from {SCOPE_LABEL[badgeScope].toLowerCase()}</span>
                )}
              </span>
            ) : (
              <span className="text-gray-400">Not set — no restriction</span>
            )}
          </p>
          {scope.scopeType !== 'brand' && bandText(band) && (
            <p className="mt-0.5 text-[11px] text-gray-400">{bandText(band)}</p>
          )}
        </div>

        {/* Editor */}
        <div className="flex items-center gap-2">
          <div className="w-44">
            <ValueInput meta={meta} value={draft} onChange={setDraft} disabled={!canManage || busy} />
          </div>
          <button
            type="button"
            onClick={save}
            disabled={!canManage || busy || !dirty}
            title={canManage ? undefined : 'You don’t have permission to change these settings.'}
            className="inline-flex items-center gap-1 rounded-lg bg-lg-green px-2.5 py-2 text-xs font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-50"
          >
            {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Save className="h-3.5 w-3.5" />}
            Save
          </button>
          <button
            type="button"
            onClick={clearRow}
            disabled={!canManage || busy || !hasRowAtScope}
            title={hasRowAtScope ? 'Clear this override' : 'No override set at this scope'}
            className="inline-flex items-center gap-1 rounded-lg border border-gray-200 px-2.5 py-2 text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-40"
          >
            <Trash2 className="h-3.5 w-3.5" /> Clear
          </button>
        </div>
      </div>

      {/* Brand-only clamp band editor. A band can only live on a brand row that
          has a value, so gate it on a brand value being set (saving a band with
          no value would clear the row and silently drop the band). */}
      {scope.scopeType === 'brand' && isNumeric && canManage && (
        <div className="mt-2">
          {hasRowAtScope ? (
            <>
              <button
                type="button"
                onClick={() => setBandOpen((o) => !o)}
                className="text-[11px] font-medium text-lg-green hover:underline"
              >
                {band ? `Allowed range: ${bandText(band)?.replace('Allowed: ', '')}` : 'Set allowed range for franchises & stores'}
              </button>
              {bandOpen && (
                <BandEditor band={band} busy={busy} onSave={saveBand} onCancel={() => setBandOpen(false)} />
              )}
            </>
          ) : (
            <p className="text-[11px] text-gray-400">Set a brand value first to define an allowed band.</p>
          )}
        </div>
      )}

      {(error || savedAt) && (
        <p className={cn('mt-1.5 text-xs', error ? 'text-red-600' : 'text-lg-green')}>
          {error ?? `Saved at ${savedAt}`}
        </p>
      )}
    </div>
  )
}

function ValueInput({ meta, value, onChange, disabled }: {
  meta: KeyMeta
  value: string
  onChange: (v: string) => void
  disabled: boolean
}) {
  if (meta.dataType === 'bool') {
    return (
      <select value={value || 'false'} onChange={(e) => onChange(e.target.value)} disabled={disabled} className={inputCls}>
        <option value="true">On</option>
        <option value="false">Off</option>
      </select>
    )
  }
  const isNumeric = meta.dataType === 'decimal' || meta.dataType === 'int'
  const prefix = meta.unit === 'rupee' ? '₹' : null
  const suffix =
    meta.unit === 'percent' ? '%'
    : meta.unit === 'hours' ? 'hrs'
    : meta.unit === 'minutes' ? 'min'
    : meta.unit === 'km' ? 'km'
    : null
  return (
    <div className="relative">
      {prefix && <span className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-sm text-gray-400">{prefix}</span>}
      <input
        type={isNumeric ? 'number' : 'text'}
        min={isNumeric ? '0' : undefined}
        step={meta.dataType === 'decimal' ? '0.01' : undefined}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        disabled={disabled}
        placeholder="Not set"
        className={cn(inputCls, prefix && 'pl-7', suffix && 'pr-10')}
      />
      {suffix && <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-xs text-gray-400">{suffix}</span>}
    </div>
  )
}

function BandEditor({ band, busy, onSave, onCancel }: {
  band: Band | null
  busy: boolean
  onSave: (schema: string | null) => void
  onCancel: () => void
}) {
  const [min, setMin] = useState(band?.min != null ? String(band.min) : '')
  const [max, setMax] = useState(band?.max != null ? String(band.max) : '')
  const [err, setErr] = useState<string | null>(null)

  const submit = () => {
    setErr(null)
    const hasMin = min.trim() !== ''
    const hasMax = max.trim() !== ''
    if (!hasMin && !hasMax) return onSave(null) // no bounds → remove the band
    const obj: Band = {}
    if (hasMin) {
      const n = Number(min)
      if (Number.isNaN(n)) return setErr('Minimum must be a number.')
      obj.min = n
    }
    if (hasMax) {
      const n = Number(max)
      if (Number.isNaN(n)) return setErr('Maximum must be a number.')
      obj.max = n
    }
    if (obj.min != null && obj.max != null && obj.min > obj.max) return setErr('Minimum can’t exceed maximum.')
    onSave(JSON.stringify(obj))
  }

  return (
    <div className="mt-2 flex flex-wrap items-end gap-2 rounded-xl border border-gray-100 bg-gray-50/60 p-3">
      <label className="block">
        <span className="mb-1 block text-[11px] font-medium text-gray-500">Min</span>
        <input type="number" value={min} onChange={(e) => setMin(e.target.value)} placeholder="—" className={cn(inputCls, 'w-24')} />
      </label>
      <label className="block">
        <span className="mb-1 block text-[11px] font-medium text-gray-500">Max</span>
        <input type="number" value={max} onChange={(e) => setMax(e.target.value)} placeholder="—" className={cn(inputCls, 'w-24')} />
      </label>
      <button
        type="button"
        onClick={submit}
        disabled={busy}
        className="inline-flex items-center gap-1 rounded-lg bg-lg-green px-2.5 py-2 text-xs font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-50"
      >
        <Check className="h-3.5 w-3.5" /> Save range
      </button>
      <button
        type="button"
        onClick={onCancel}
        className="inline-flex items-center gap-1 rounded-lg border border-gray-200 px-2.5 py-2 text-xs font-medium text-gray-500 hover:bg-gray-50"
      >
        <X className="h-3.5 w-3.5" /> Cancel
      </button>
      {err && <span className="w-full text-xs text-red-600">{err}</span>}
      <span className="w-full text-[11px] text-gray-400">Leave both blank to remove the range. Franchises and stores must stay within it.</span>
    </div>
  )
}

function InheritanceBadge({ scope }: { scope: SettingScopeType }) {
  const styles: Record<SettingScopeType, string> = {
    platform: 'bg-gray-100 text-gray-500',
    brand: 'bg-lg-green/10 text-lg-green',
    franchise: 'bg-blue-50 text-blue-600',
    store: 'bg-violet-50 text-violet-600',
  }
  const Icon = scope === 'platform' ? Globe2 : scope === 'store' ? StoreIcon : Building2
  return (
    <span className={cn('inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-medium', styles[scope])}>
      <Icon className="h-3 w-3" /> {SCOPE_LABEL[scope]}
    </span>
  )
}
