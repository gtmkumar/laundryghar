import { useEffect, useMemo, useState } from 'react'
import { Ticket, Plus, Save, Archive } from 'lucide-react'
import { useCreateCoupon, useUpdateCoupon, useDeleteCoupon } from '@/hooks/useCommerce'
import {
  FormDrawer,
  DrawerSection,
  DetailSection,
  DetailRow,
  Field,
  drawerInputCls,
} from '@/components/shared/FormDrawer'
import type { CouponDto, CreateCouponPayload, UpdateCouponPayload } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

export const COUPON_TYPES = [
  { value: 'percent', label: 'Percentage (%)' },
  { value: 'flat', label: 'Flat amount (₹)' },
] as const

export const ELIGIBILITY = [
  { value: 'all', label: 'All customers' },
  { value: 'new', label: 'New customers only' },
  { value: 'existing', label: 'Existing customers' },
] as const

const STATUSES = [
  { value: 'active', label: 'Active' },
  { value: 'inactive', label: 'Inactive' },
  { value: 'expired', label: 'Expired' },
] as const

/** yyyy-MM-dd from an ISO timestamp (for <input type=date> binding). */
function dateOnly(iso: string | null | undefined): string {
  if (!iso) return ''
  return iso.slice(0, 10)
}

/** Normalize a yyyy-MM-dd date input to an ISO instant the backend accepts. */
function toInstant(date: string): string {
  return new Date(`${date}T00:00:00Z`).toISOString()
}

interface FormState {
  code: string
  name: string
  description: string
  couponType: string
  discountValue: string
  maxDiscountAmount: string
  minOrderValue: string
  customerEligibility: string
  isFirstOrderOnly: boolean
  isSingleUsePerCust: boolean
  maxTotalUses: string
  maxUsesPerCustomer: string
  isStackable: boolean
  isPublic: boolean
  isAutoApply: boolean
  validFrom: string
  validUntil: string
  status: string
}

function blankForm(): FormState {
  const today = new Date().toISOString().slice(0, 10)
  return {
    code: '',
    name: '',
    description: '',
    couponType: 'percent',
    discountValue: '',
    maxDiscountAmount: '',
    minOrderValue: '0',
    customerEligibility: 'all',
    isFirstOrderOnly: false,
    isSingleUsePerCust: false,
    maxTotalUses: '',
    maxUsesPerCustomer: '1',
    isStackable: false,
    isPublic: true,
    isAutoApply: false,
    validFrom: today,
    validUntil: '',
    status: 'active',
  }
}

function fromCoupon(c: CouponDto): FormState {
  return {
    code: c.code,
    name: c.name,
    description: c.description ?? '',
    couponType: c.couponType,
    discountValue: String(c.discountValue),
    maxDiscountAmount: c.maxDiscountAmount != null ? String(c.maxDiscountAmount) : '',
    minOrderValue: String(c.minOrderValue),
    customerEligibility: c.customerEligibility,
    isFirstOrderOnly: c.isFirstOrderOnly,
    isSingleUsePerCust: c.isSingleUsePerCust,
    maxTotalUses: c.maxTotalUses != null ? String(c.maxTotalUses) : '',
    maxUsesPerCustomer: String(c.maxUsesPerCustomer),
    isStackable: c.isStackable,
    isPublic: c.isPublic,
    isAutoApply: c.isAutoApply,
    validFrom: dateOnly(c.validFrom),
    validUntil: dateOnly(c.validUntil),
    status: c.status,
  }
}

// ── Create / Edit ─────────────────────────────────────────────────────────────

interface EditDrawerProps {
  open: boolean
  /** When set, the drawer is in edit mode for this coupon; otherwise create mode. */
  coupon?: CouponDto | null
  onClose: () => void
}

export function CouponEditDrawer({ open, coupon, onClose }: EditDrawerProps) {
  const isEdit = !!coupon
  const create = useCreateCoupon()
  const update = useUpdateCoupon()

  const [form, setForm] = useState<FormState>(blankForm())
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setForm(coupon ? fromCoupon(coupon) : blankForm())
      setError(null)
    }
  }, [open, coupon])

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  if (!open) return null

  const submit = async () => {
    setError(null)
    const discount = Number(form.discountValue)
    const maxPerCust = Number(form.maxUsesPerCustomer)
    const minOrderValue = form.minOrderValue.trim() === '' ? 0 : Number(form.minOrderValue)
    const maxDiscount = form.maxDiscountAmount.trim() === '' ? null : Number(form.maxDiscountAmount)
    const maxTotalUses = form.maxTotalUses.trim() === '' ? null : Number(form.maxTotalUses)
    if (!isEdit && !form.code.trim()) return setError('Coupon code is required.')
    if (!form.name.trim()) return setError('Coupon name is required.')
    if (!(discount > 0)) return setError('Discount value must be greater than 0.')
    if (form.couponType === 'percent' && discount > 100)
      return setError('A percentage discount cannot exceed 100%.')
    if (!Number.isFinite(minOrderValue) || minOrderValue < 0)
      return setError('Minimum order value must be 0 or greater.')
    if (maxDiscount !== null && (!Number.isFinite(maxDiscount) || maxDiscount < 0))
      return setError('Max discount cap must be 0 or greater.')
    if (maxTotalUses !== null && (!Number.isInteger(maxTotalUses) || maxTotalUses < 1))
      return setError('Total uses must be a whole number of 1 or more.')
    if (maxTotalUses !== null && isEdit && coupon && maxTotalUses < coupon.currentUsageCount)
      return setError(
        `Total uses can't be below the ${coupon.currentUsageCount} already redeemed.`,
      )
    if (!(maxPerCust > 0)) return setError('Max uses per customer must be at least 1.')
    if (!form.validFrom) return setError('A start date is required.')
    if (form.validUntil && form.validUntil < form.validFrom)
      return setError('The end date must be on or after the start date.')

    const common = {
      name: form.name.trim(),
      description: form.description.trim() || null,
      discountValue: discount,
      maxDiscountAmount: maxDiscount,
      minOrderValue,
      applicableServices: null,
      applicableStores: null,
      applicableFranchises: null,
      customerEligibility: form.customerEligibility,
      eligibleCustomerIds: null,
      eligibleSegments: null,
      isFirstOrderOnly: form.isFirstOrderOnly,
      isSingleUsePerCust: form.isSingleUsePerCust,
      maxTotalUses,
      maxUsesPerCustomer: maxPerCust,
      isStackable: form.isStackable,
      isPublic: form.isPublic,
      isAutoApply: form.isAutoApply,
      validFrom: toInstant(form.validFrom),
      validUntil: form.validUntil ? toInstant(form.validUntil) : null,
    }

    try {
      if (isEdit && coupon) {
        const payload: UpdateCouponPayload = { ...common, status: form.status }
        await update.mutateAsync({ id: coupon.id, payload })
      } else {
        const payload: CreateCouponPayload = {
          ...common,
          code: form.code.trim().toUpperCase(),
          couponType: form.couponType,
        }
        await create.mutateAsync(payload)
      }
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save the coupon.')
    }
  }

  const submitting = create.isPending || update.isPending

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Ticket}
      eyebrow="Commerce · Coupons"
      title={isEdit ? `Edit ${coupon!.code}` : 'New coupon'}
      width="md"
      error={error}
      onSubmit={() => void submit()}
      submitLabel={isEdit ? 'Save coupon' : 'Create coupon'}
      submittingLabel="Saving…"
      submitIcon={isEdit ? Save : Plus}
      submitting={submitting}
    >
      <DrawerSection title="Identity">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Code *" hint={isEdit ? 'Code cannot be changed after creation.' : 'Stored in uppercase.'}>
            <input
              value={form.code}
              onChange={(e) => set('code', e.target.value.toUpperCase())}
              disabled={isEdit}
              className={`${drawerInputCls} font-mono uppercase`}
              placeholder="WELCOME50"
            />
          </Field>
          <Field label="Name *">
            <input
              value={form.name}
              onChange={(e) => set('name', e.target.value)}
              className={drawerInputCls}
              placeholder="Welcome offer"
            />
          </Field>
        </div>
        <Field label="Description">
          <input
            value={form.description}
            onChange={(e) => set('description', e.target.value)}
            className={drawerInputCls}
            placeholder="Optional customer-facing description"
          />
        </Field>
      </DrawerSection>

      <DrawerSection title="Discount">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Type *">
            <select
              value={form.couponType}
              onChange={(e) => set('couponType', e.target.value)}
              disabled={isEdit}
              className={drawerInputCls}
            >
              {COUPON_TYPES.map((t) => (
                <option key={t.value} value={t.value}>{t.label}</option>
              ))}
            </select>
          </Field>
          <Field label={form.couponType === 'percent' ? 'Discount (%) *' : 'Discount (₹) *'}>
            <input
              type="number"
              min="0.01"
              step="0.01"
              value={form.discountValue}
              onChange={(e) => set('discountValue', e.target.value)}
              className={drawerInputCls}
              placeholder={form.couponType === 'percent' ? '50' : '100'}
            />
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Max discount cap (₹)" hint="Caps a percentage discount. Leave blank for none.">
            <input
              type="number"
              min="0"
              step="0.01"
              value={form.maxDiscountAmount}
              onChange={(e) => set('maxDiscountAmount', e.target.value)}
              className={drawerInputCls}
              placeholder="No cap"
            />
          </Field>
          <Field label="Minimum order value (₹)">
            <input
              type="number"
              min="0"
              step="0.01"
              value={form.minOrderValue}
              onChange={(e) => set('minOrderValue', e.target.value)}
              className={drawerInputCls}
            />
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="Usage limits">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Total uses (all customers)" hint="Leave blank for unlimited.">
            <input
              type="number"
              min="1"
              step="1"
              value={form.maxTotalUses}
              onChange={(e) => set('maxTotalUses', e.target.value)}
              className={drawerInputCls}
              placeholder="Unlimited"
            />
          </Field>
          <Field label="Uses per customer *">
            <input
              type="number"
              min="1"
              step="1"
              value={form.maxUsesPerCustomer}
              onChange={(e) => set('maxUsesPerCustomer', e.target.value)}
              className={drawerInputCls}
            />
          </Field>
        </div>
        <Field label="Customer eligibility">
          <select
            value={form.customerEligibility}
            onChange={(e) => set('customerEligibility', e.target.value)}
            className={drawerInputCls}
          >
            {ELIGIBILITY.map((o) => (
              <option key={o.value} value={o.value}>{o.label}</option>
            ))}
          </select>
        </Field>
        <div className="space-y-2">
          <Toggle label="First order only" checked={form.isFirstOrderOnly} onChange={(v) => set('isFirstOrderOnly', v)} />
          <Toggle label="Single use per customer" checked={form.isSingleUsePerCust} onChange={(v) => set('isSingleUsePerCust', v)} />
          <Toggle label="Stackable with other offers" checked={form.isStackable} onChange={(v) => set('isStackable', v)} />
          <Toggle label="Publicly listed" checked={form.isPublic} onChange={(v) => set('isPublic', v)} />
          <Toggle label="Auto-apply at checkout" checked={form.isAutoApply} onChange={(v) => set('isAutoApply', v)} />
        </div>
      </DrawerSection>

      <DrawerSection title="Validity">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Valid from *">
            <input type="date" value={form.validFrom} onChange={(e) => set('validFrom', e.target.value)} className={drawerInputCls} />
          </Field>
          <Field label="Valid until" hint="Leave blank for no expiry.">
            <input type="date" value={form.validUntil} onChange={(e) => set('validUntil', e.target.value)} className={drawerInputCls} />
          </Field>
        </div>
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

function Toggle({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className="flex items-center gap-2 text-sm text-gray-700">
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
        className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30"
      />
      {label}
    </label>
  )
}

// ── View (read-only detail + usage) ─────────────────────────────────────────────

interface ViewDrawerProps {
  coupon: CouponDto | null
  onClose: () => void
  onEdit?: (c: CouponDto) => void
  onArchive?: (c: CouponDto) => void
  canManage: boolean
}

export function CouponViewDrawer({ coupon, onClose, onEdit, onArchive, canManage }: ViewDrawerProps) {
  const discountLabel = useMemo(() => {
    if (!coupon) return ''
    return coupon.couponType === 'percent'
      ? `${coupon.discountValue}%`
      : formatCurrency(coupon.discountValue)
  }, [coupon])

  if (!coupon) return null

  const usagePct =
    coupon.maxTotalUses && coupon.maxTotalUses > 0
      ? Math.min(100, Math.round((coupon.currentUsageCount / coupon.maxTotalUses) * 100))
      : null

  return (
    <FormDrawer
      open={!!coupon}
      onClose={onClose}
      icon={Ticket}
      eyebrow={<>Coupon · <span className="font-mono">{coupon.code}</span></>}
      title={coupon.name}
      width="md"
      footer={
        canManage ? (
          <div className="flex justify-between gap-2">
            <button
              type="button"
              onClick={() => onArchive?.(coupon)}
              className="inline-flex items-center gap-1.5 rounded-lg border border-red-200 px-4 py-2 text-sm font-medium text-red-600 hover:bg-red-50"
            >
              <Archive className="h-3.5 w-3.5" /> Archive
            </button>
            <button
              type="button"
              onClick={() => onEdit?.(coupon)}
              className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
            >
              <Save className="h-3.5 w-3.5" /> Edit
            </button>
          </div>
        ) : undefined
      }
    >
      {/* Usage — surfaced prominently (coupons are redeemable at POS now). */}
      <DrawerSection title="Usage">
        <div className="rounded-xl bg-gray-50 p-4">
          <div className="flex items-end justify-between">
            <div>
              <p className="text-xs text-gray-400">Times redeemed</p>
              <p className="text-2xl font-bold tabular-nums text-gray-900">
                {coupon.currentUsageCount}
                {coupon.maxTotalUses != null && (
                  <span className="text-base font-medium text-gray-400"> / {coupon.maxTotalUses}</span>
                )}
              </p>
            </div>
            <p className="text-xs text-gray-400">
              {coupon.maxTotalUses == null ? 'Unlimited total uses' : `${usagePct}% used`}
            </p>
          </div>
          {usagePct != null && (
            <div className="mt-2 h-2 w-full overflow-hidden rounded-full bg-gray-200">
              <div className="h-full rounded-full bg-lg-green" style={{ width: `${usagePct}%` }} />
            </div>
          )}
        </div>
      </DrawerSection>

      <DetailSection title="Discount">
        <DetailRow label="Type" value={<span className="capitalize">{coupon.couponType === 'percent' ? 'Percentage' : 'Flat amount'}</span>} />
        <DetailRow label="Value" value={<span className="font-semibold">{discountLabel}</span>} />
        <DetailRow label="Max discount cap" value={coupon.maxDiscountAmount != null ? formatCurrency(coupon.maxDiscountAmount) : '—'} />
        <DetailRow label="Minimum order" value={formatCurrency(coupon.minOrderValue)} />
      </DetailSection>

      <DetailSection title="Limits & eligibility">
        <DetailRow label="Uses per customer" value={coupon.maxUsesPerCustomer} />
        <DetailRow label="Customer eligibility" value={<span className="capitalize">{coupon.customerEligibility}</span>} />
        <DetailRow label="First order only" value={coupon.isFirstOrderOnly ? 'Yes' : 'No'} />
        <DetailRow label="Stackable" value={coupon.isStackable ? 'Yes' : 'No'} />
        <DetailRow label="Auto-apply" value={coupon.isAutoApply ? 'Yes' : 'No'} />
        <DetailRow label="Public" value={coupon.isPublic ? 'Yes' : 'No'} />
      </DetailSection>

      <DetailSection title="Validity">
        <DetailRow label="Status" value={<span className="capitalize">{coupon.status}</span>} />
        <DetailRow label="Valid from" value={formatDate(coupon.validFrom)} />
        <DetailRow label="Valid until" value={coupon.validUntil ? formatDate(coupon.validUntil) : 'No expiry'} />
      </DetailSection>
    </FormDrawer>
  )
}

// ── Archive confirm ──────────────────────────────────────────────────────────

export function ArchiveCouponDrawer({ coupon, onClose }: { coupon: CouponDto | null; onClose: () => void }) {
  const del = useDeleteCoupon()
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (coupon) setError(null)
  }, [coupon])

  if (!coupon) return null

  const submit = async () => {
    setError(null)
    try {
      await del.mutateAsync(coupon.id)
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not archive the coupon.')
    }
  }

  return (
    <FormDrawer
      open={!!coupon}
      onClose={onClose}
      icon={Archive}
      eyebrow={<>Archive coupon · <span className="font-mono">{coupon.code}</span></>}
      title={coupon.name}
      width="sm"
      error={error}
      onSubmit={() => void submit()}
      submitLabel="Archive coupon"
      submittingLabel="Archiving…"
      submitIcon={Archive}
      submitting={del.isPending}
    >
      <p className="text-sm text-gray-600">
        Archiving removes this coupon from the active list and stops new redemptions. Existing redemptions are unaffected.
      </p>
    </FormDrawer>
  )
}
