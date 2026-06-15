import { useState } from 'react'
import { Package as PackageIcon, Plus, Save, Archive } from 'lucide-react'
import { useCreatePackage, useUpdatePackage, useDeletePackage } from '@/hooks/useCommerce'
import {
  FormDrawer,
  DrawerSection,
  DetailSection,
  DetailRow,
  Field,
  drawerInputCls,
} from '@/components/shared/FormDrawer'
import type { PackageDto, CreatePackagePayload, UpdatePackagePayload } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

/**
 * Must mirror the packages.tier DB check constraint:
 * silver | gold | diamond | platinum | custom. ("bronze" is NOT allowed — it
 * previously shipped here and produced a guaranteed 422 on create.)
 */
// Small co-located tier constant mirroring the DB check constraint; not worth its own module.
// eslint-disable-next-line react-refresh/only-export-components
export const TIERS = [
  { value: 'silver', label: 'Silver' },
  { value: 'gold', label: 'Gold' },
  { value: 'diamond', label: 'Diamond' },
  { value: 'platinum', label: 'Platinum' },
  { value: 'custom', label: 'Custom' },
] as const

const STATUSES = [
  { value: 'active', label: 'Active' },
  { value: 'inactive', label: 'Inactive' },
  { value: 'archived', label: 'Archived' },
] as const

function dateOnly(iso: string | null | undefined): string {
  return iso ? iso.slice(0, 10) : ''
}

/**
 * `name_localized` is a jsonb column: Postgres rejects a bare string. Serialize
 * the localized display name as a JSON object (matching the house convention
 * used for subscription plans / catalog: {"en": "..."}).
 */
function buildNameLocalized(en: string): string {
  return JSON.stringify(en.trim() ? { en: en.trim() } : {})
}

/** Read the English localized name back out of the stored jsonb string. */
function parseNameLocalized(raw: string | null | undefined): string {
  if (!raw) return ''
  try {
    const obj = JSON.parse(raw) as Record<string, unknown>
    return typeof obj.en === 'string' ? obj.en : ''
  } catch {
    // Legacy rows may hold a plain string.
    return raw
  }
}

function toInstant(date: string): string {
  return new Date(`${date}T00:00:00Z`).toISOString()
}

interface FormState {
  code: string
  name: string
  nameLocalized: string
  tier: string
  description: string
  price: string
  creditValue: string
  discountPercent: string
  creditMultiplier: string
  validityDays: string
  isUnlimitedValidity: boolean
  minimumOrderValue: string
  maxPurchasesPerCust: string
  displayOrder: string
  isFeatured: boolean
  termsAndConditions: string
  availableFrom: string
  availableTo: string
  status: string
}

function blankForm(): FormState {
  return {
    code: '',
    name: '',
    nameLocalized: '',
    tier: 'silver',
    description: '',
    price: '',
    creditValue: '',
    discountPercent: '0',
    creditMultiplier: '1',
    validityDays: '90',
    isUnlimitedValidity: false,
    minimumOrderValue: '',
    maxPurchasesPerCust: '',
    displayOrder: '0',
    isFeatured: false,
    termsAndConditions: '',
    availableFrom: '',
    availableTo: '',
    status: 'active',
  }
}

function fromPackage(p: PackageDto): FormState {
  return {
    code: p.code,
    name: p.name,
    nameLocalized: parseNameLocalized(p.nameLocalized),
    tier: p.tier,
    description: p.description ?? '',
    price: String(p.price),
    creditValue: String(p.creditValue),
    discountPercent: String(p.discountPercent),
    creditMultiplier: String(p.creditMultiplier),
    validityDays: p.validityDays != null ? String(p.validityDays) : '',
    isUnlimitedValidity: p.isUnlimitedValidity,
    minimumOrderValue: p.minimumOrderValue != null ? String(p.minimumOrderValue) : '',
    maxPurchasesPerCust: p.maxPurchasesPerCust != null ? String(p.maxPurchasesPerCust) : '',
    displayOrder: String(p.displayOrder),
    isFeatured: p.isFeatured,
    termsAndConditions: p.termsAndConditions ?? '',
    availableFrom: dateOnly(p.availableFrom),
    availableTo: dateOnly(p.availableTo),
    status: p.status,
  }
}

// ── Create / Edit ─────────────────────────────────────────────────────────────

interface EditDrawerProps {
  open: boolean
  pkg?: PackageDto | null
  onClose: () => void
}

export function PackageEditDrawer({ open, pkg, onClose }: EditDrawerProps) {
  const isEdit = !!pkg
  const create = useCreatePackage()
  const update = useUpdatePackage()

  const [form, setForm] = useState<FormState>(blankForm())
  const [error, setError] = useState<string | null>(null)

  // Re-seed the form whenever the {open, package} pair changes (adjust-state-while-rendering).
  const seedSig = `${open}:${pkg?.id ?? ''}`
  const [seededSig, setSeededSig] = useState(seedSig)
  if (seedSig !== seededSig) {
    setSeededSig(seedSig)
    if (open) {
      setForm(pkg ? fromPackage(pkg) : blankForm())
      setError(null)
    }
  }

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  if (!open) return null

  const submit = async () => {
    setError(null)
    const price = Number(form.price)
    const creditValue = Number(form.creditValue)
    const multiplier = Number(form.creditMultiplier)
    const discountPercent = form.discountPercent.trim() === '' ? 0 : Number(form.discountPercent)
    const displayOrder = form.displayOrder.trim() === '' ? 0 : Number(form.displayOrder)
    if (!isEdit && !form.code.trim()) return setError('Package code is required.')
    if (!form.name.trim()) return setError('Package name is required.')
    if (!form.nameLocalized.trim()) return setError('Localized name is required.')
    if (!isEdit && !form.tier) return setError('Tier is required.')
    if (!(price > 0)) return setError('Price must be greater than 0.')
    if (!(creditValue > 0)) return setError('Credit value must be greater than 0.')
    if (creditValue < price)
      return setError('Credit value must be at least the package price.')
    if (!(multiplier > 0)) return setError('Credit multiplier must be greater than 0.')
    if (!Number.isFinite(discountPercent) || discountPercent < 0 || discountPercent > 100)
      return setError('Discount % must be between 0 and 100.')
    if (!Number.isFinite(displayOrder) || displayOrder < 0)
      return setError('Display order must be 0 or greater.')
    if (form.availableTo && form.availableFrom && form.availableTo < form.availableFrom)
      return setError('Available-to must be on or after available-from.')

    const common = {
      name: form.name.trim(),
      nameLocalized: buildNameLocalized(form.nameLocalized),
      description: form.description.trim() || null,
      price,
      creditValue,
      discountPercent,
      creditMultiplier: multiplier,
      validityDays: form.isUnlimitedValidity ? null : form.validityDays ? Number(form.validityDays) : null,
      isUnlimitedValidity: form.isUnlimitedValidity,
      applicableServices: null,
      excludedServices: null,
      minimumOrderValue: form.minimumOrderValue ? Number(form.minimumOrderValue) : null,
      maxUsagePerOrder: null,
      maxPurchasesPerCust: form.maxPurchasesPerCust ? Number(form.maxPurchasesPerCust) : null,
      iconUrl: null,
      colorHex: null,
      displayOrder,
      isFeatured: form.isFeatured,
      termsAndConditions: form.termsAndConditions.trim() || null,
      availableFrom: form.availableFrom ? toInstant(form.availableFrom) : null,
      availableTo: form.availableTo ? toInstant(form.availableTo) : null,
    }

    try {
      if (isEdit && pkg) {
        const payload: UpdatePackagePayload = { ...common, status: form.status }
        await update.mutateAsync({ id: pkg.id, payload })
      } else {
        const payload: CreatePackagePayload = { ...common, code: form.code.trim(), tier: form.tier }
        await create.mutateAsync(payload)
      }
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save the package.')
    }
  }

  const submitting = create.isPending || update.isPending

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={PackageIcon}
      eyebrow="Commerce · Packages"
      title={isEdit ? `Edit ${pkg!.name}` : 'New package'}
      width="md"
      error={error}
      onSubmit={() => void submit()}
      submitLabel={isEdit ? 'Save package' : 'Create package'}
      submittingLabel="Saving…"
      submitIcon={isEdit ? Save : Plus}
      submitting={submitting}
    >
      <DrawerSection title="Identity">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Code *" hint={isEdit ? 'Code cannot be changed.' : undefined}>
            <input
              value={form.code}
              onChange={(e) => set('code', e.target.value)}
              disabled={isEdit}
              className={`${drawerInputCls} font-mono`}
              placeholder="PKG-SILVER"
            />
          </Field>
          <Field label="Tier *" hint={isEdit ? 'Tier cannot be changed.' : undefined}>
            <select
              value={form.tier}
              onChange={(e) => set('tier', e.target.value)}
              disabled={isEdit}
              className={drawerInputCls}
            >
              {TIERS.map((t) => (
                <option key={t.value} value={t.value}>{t.label}</option>
              ))}
            </select>
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Name *">
            <input value={form.name} onChange={(e) => set('name', e.target.value)} className={drawerInputCls} placeholder="Silver Package" />
          </Field>
          <Field label="Localized name *">
            <input value={form.nameLocalized} onChange={(e) => set('nameLocalized', e.target.value)} className={drawerInputCls} placeholder="Silver Package" />
          </Field>
        </div>
        <Field label="Description">
          <input value={form.description} onChange={(e) => set('description', e.target.value)} className={drawerInputCls} placeholder="Optional description" />
        </Field>
      </DrawerSection>

      <DrawerSection title="Pricing & credits">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Price (₹) *">
            <input type="number" min="0.01" step="0.01" value={form.price} onChange={(e) => set('price', e.target.value)} className={drawerInputCls} placeholder="999" />
          </Field>
          <Field label="Credit value (₹) *" hint="Wallet credit the customer receives.">
            <input type="number" min="0.01" step="0.01" value={form.creditValue} onChange={(e) => set('creditValue', e.target.value)} className={drawerInputCls} placeholder="1100" />
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Discount %">
            <input type="number" min="0" max="100" step="0.01" value={form.discountPercent} onChange={(e) => set('discountPercent', e.target.value)} className={drawerInputCls} />
          </Field>
          <Field label="Credit multiplier *" hint="e.g. 1.10 = 10% bonus credit.">
            <input type="number" min="0.01" step="0.01" value={form.creditMultiplier} onChange={(e) => set('creditMultiplier', e.target.value)} className={drawerInputCls} />
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="Validity & limits">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Validity (days)">
            <input
              type="number"
              min="1"
              step="1"
              value={form.validityDays}
              onChange={(e) => set('validityDays', e.target.value)}
              disabled={form.isUnlimitedValidity}
              className={drawerInputCls}
              placeholder="90"
            />
          </Field>
          <Field label="Minimum order value (₹)">
            <input type="number" min="0" step="0.01" value={form.minimumOrderValue} onChange={(e) => set('minimumOrderValue', e.target.value)} className={drawerInputCls} placeholder="None" />
          </Field>
        </div>
        <label className="flex items-center gap-2 text-sm text-gray-700">
          <input
            type="checkbox"
            checked={form.isUnlimitedValidity}
            onChange={(e) => set('isUnlimitedValidity', e.target.checked)}
            className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30"
          />
          Unlimited validity (never expires)
        </label>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Max purchases / customer" hint="Leave blank for unlimited.">
            <input type="number" min="1" step="1" value={form.maxPurchasesPerCust} onChange={(e) => set('maxPurchasesPerCust', e.target.value)} className={drawerInputCls} placeholder="Unlimited" />
          </Field>
          <Field label="Display order">
            <input type="number" min="0" step="1" value={form.displayOrder} onChange={(e) => set('displayOrder', e.target.value)} className={drawerInputCls} />
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="Availability">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Available from" hint="Leave blank for immediately.">
            <input type="date" value={form.availableFrom} onChange={(e) => set('availableFrom', e.target.value)} className={drawerInputCls} />
          </Field>
          <Field label="Available to" hint="Leave blank for no end.">
            <input type="date" value={form.availableTo} onChange={(e) => set('availableTo', e.target.value)} className={drawerInputCls} />
          </Field>
        </div>
        <label className="flex items-center gap-2 text-sm text-gray-700">
          <input
            type="checkbox"
            checked={form.isFeatured}
            onChange={(e) => set('isFeatured', e.target.checked)}
            className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30"
          />
          Featured package
        </label>
        <Field label="Terms & conditions">
          <textarea rows={2} value={form.termsAndConditions} onChange={(e) => set('termsAndConditions', e.target.value)} className={drawerInputCls} placeholder="Optional terms…" />
        </Field>
        {isEdit && (
          <Field label="Status">
            <select value={form.status} onChange={(e) => set('status', e.target.value)} className={drawerInputCls}>
              {STATUSES.map((s) => (
                <option key={s.value} value={s.value}>{s.label}</option>
              ))}
            </select>
          </Field>
        )}
      </DrawerSection>
    </FormDrawer>
  )
}

// ── View ──────────────────────────────────────────────────────────────────────

interface ViewDrawerProps {
  pkg: PackageDto | null
  onClose: () => void
  onEdit?: (p: PackageDto) => void
  onArchive?: (p: PackageDto) => void
  canManage: boolean
}

export function PackageViewDrawer({ pkg, onClose, onEdit, onArchive, canManage }: ViewDrawerProps) {
  if (!pkg) return null

  const validity = pkg.isUnlimitedValidity ? 'Never expires' : pkg.validityDays != null ? `${pkg.validityDays} days` : '—'

  return (
    <FormDrawer
      open={!!pkg}
      onClose={onClose}
      icon={PackageIcon}
      eyebrow={<>Package · <span className="font-mono">{pkg.code}</span></>}
      title={pkg.name}
      width="md"
      footer={
        canManage ? (
          <div className="flex justify-between gap-2">
            <button
              type="button"
              onClick={() => onArchive?.(pkg)}
              className="inline-flex items-center gap-1.5 rounded-lg border border-red-200 px-4 py-2 text-sm font-medium text-red-600 hover:bg-red-50"
            >
              <Archive className="h-3.5 w-3.5" /> Archive
            </button>
            <button
              type="button"
              onClick={() => onEdit?.(pkg)}
              className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
            >
              <Save className="h-3.5 w-3.5" /> Edit
            </button>
          </div>
        ) : undefined
      }
    >
      <DrawerSection title="Pricing">
        <div className="grid grid-cols-3 gap-3 rounded-xl bg-gray-50 p-4 text-sm">
          <div>
            <p className="text-xs text-gray-400">Price</p>
            <p className="font-semibold tabular-nums">{formatCurrency(pkg.price)}</p>
          </div>
          <div>
            <p className="text-xs text-gray-400">Credit value</p>
            <p className="font-semibold tabular-nums text-lg-green">{formatCurrency(pkg.creditValue)}</p>
          </div>
          <div>
            <p className="text-xs text-gray-400">Multiplier</p>
            <p className="font-semibold tabular-nums">{pkg.creditMultiplier}×</p>
          </div>
        </div>
      </DrawerSection>

      <DetailSection title="Details">
        <DetailRow label="Tier" value={<span className="capitalize">{pkg.tier}</span>} />
        <DetailRow label="Discount" value={`${pkg.discountPercent}%`} />
        <DetailRow label="Validity" value={validity} />
        <DetailRow label="Minimum order" value={pkg.minimumOrderValue != null ? formatCurrency(pkg.minimumOrderValue) : '—'} />
        <DetailRow label="Max purchases / customer" value={pkg.maxPurchasesPerCust != null ? pkg.maxPurchasesPerCust : 'Unlimited'} />
        <DetailRow label="Featured" value={pkg.isFeatured ? 'Yes' : 'No'} />
      </DetailSection>

      <DetailSection title="Availability">
        <DetailRow label="Status" value={<span className="capitalize">{pkg.status}</span>} />
        <DetailRow label="Available from" value={pkg.availableFrom ? formatDate(pkg.availableFrom) : 'Immediately'} />
        <DetailRow label="Available to" value={pkg.availableTo ? formatDate(pkg.availableTo) : 'No end'} />
      </DetailSection>

      {pkg.termsAndConditions && (
        <DrawerSection title="Terms & conditions">
          <p className="text-sm text-gray-600">{pkg.termsAndConditions}</p>
        </DrawerSection>
      )}
    </FormDrawer>
  )
}

// ── Archive confirm ──────────────────────────────────────────────────────────

export function ArchivePackageDrawer({ pkg, onClose }: { pkg: PackageDto | null; onClose: () => void }) {
  const del = useDeletePackage()
  const [error, setError] = useState<string | null>(null)

  // Clear the error whenever a new package is opened (adjust-state-while-rendering).
  const [seededId, setSeededId] = useState<string | null>(null)
  if (pkg && seededId !== pkg.id) {
    setSeededId(pkg.id)
    setError(null)
  }

  if (!pkg) return null

  const submit = async () => {
    setError(null)
    try {
      await del.mutateAsync(pkg.id)
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not archive the package.')
    }
  }

  return (
    <FormDrawer
      open={!!pkg}
      onClose={onClose}
      icon={Archive}
      eyebrow={<>Archive package · <span className="font-mono">{pkg.code}</span></>}
      title={pkg.name}
      width="sm"
      error={error}
      onSubmit={() => void submit()}
      submitLabel="Archive package"
      submittingLabel="Archiving…"
      submitIcon={Archive}
      submitting={del.isPending}
    >
      <p className="text-sm text-gray-600">
        Archiving removes this package from the active catalog so customers can no longer purchase it. Existing
        purchases are unaffected.
      </p>
    </FormDrawer>
  )
}
