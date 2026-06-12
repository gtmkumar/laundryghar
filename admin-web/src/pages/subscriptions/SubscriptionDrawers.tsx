import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Tag, Plus, Save, Archive, CheckCircle2, Ban, User as UserIcon } from 'lucide-react'
import {
  useCreateSubscriptionPlan,
  useUpdateSubscriptionPlan,
  usePatchSubscriptionPlanStatus,
  useDeleteSubscriptionPlan,
  usePatchCustomerSubscriptionStatus,
} from '@/hooks/useSubscriptions'
import { usePermissions } from '@/hooks/usePermissions'
import type { CustomerSubscriptionStatus } from '@/api/subscriptions'
import { Pause, Play, XCircle } from 'lucide-react'
import {
  FormDrawer,
  DrawerSection,
  Field,
  drawerInputCls,
  DetailSection,
  DetailRow,
} from '@/components/shared/FormDrawer'
import { FieldError } from '@/components/ui/FieldError'
import { ConfirmDialog, useConfirm } from '@/components/shared/ConfirmDialog'
import { nonNegativeMoney } from '@/lib/validation'
import { apiErrorMessage } from '@/lib/apiError'
import type {
  SubscriptionPlanDto,
  CustomerSubscriptionDto,
  CreateSubscriptionPlanPayload,
  UpdateSubscriptionPlanPayload,
} from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

const TIERS = ['basic', 'standard', 'premium', 'custom']
const BILLING_INTERVALS = ['weekly', 'monthly', 'quarterly', 'half_yearly', 'yearly']
const QUOTA_TYPES = ['credit', 'order_count', 'weight_kg', 'unlimited']
const PLAN_STATUSES = ['draft', 'active', 'paused', 'retired']

/**
 * The backend stores `name_localized` as a JSON-object STRING and validates it
 * (422 otherwise). We never expose raw JSON: the operator edits a friendly
 * per-locale `en` / `hi` pair which we serialize to `{"en":…,"hi":…}` on submit
 * and parse back on edit.
 */
function buildNameLocalized(en: string, hi: string): string {
  const obj: Record<string, string> = {}
  if (en.trim()) obj.en = en.trim()
  if (hi.trim()) obj.hi = hi.trim()
  return JSON.stringify(obj)
}

function parseNameLocalized(raw: string | null | undefined): { en: string; hi: string } {
  if (!raw) return { en: '', hi: '' }
  try {
    const obj = JSON.parse(raw) as Record<string, unknown>
    return {
      en: typeof obj.en === 'string' ? obj.en : '',
      hi: typeof obj.hi === 'string' ? obj.hi : '',
    }
  } catch {
    return { en: '', hi: '' }
  }
}

// ── Plan create / edit ──────────────────────────────────────────────────────────

const planSchema = z.object({
  code: z.string().min(1, 'Code is required.').max(50, 'Max 50 characters.'),
  name: z.string().min(1, 'Name is required.').max(100, 'Max 100 characters.'),
  nameEn: z.string().min(1, 'English name is required.'),
  nameHi: z.string().optional(),
  description: z.string().optional(),
  tier: z.enum(['basic', 'standard', 'premium', 'custom'] as const),
  billingInterval: z.enum(['weekly', 'monthly', 'quarterly', 'half_yearly', 'yearly'] as const),
  intervalCount: z.number({ error: 'Must be a number' }).int().gt(0, 'Must be at least 1'),
  price: nonNegativeMoney,
  setupFee: nonNegativeMoney,
  currencyCode: z.string().length(3, 'Use a 3-letter code, e.g. INR'),
  trialDays: z.number({ error: 'Must be a number' }).int().gte(0, 'Must be 0 or greater'),
  quotaType: z.enum(['credit', 'order_count', 'weight_kg', 'unlimited'] as const),
  quotaValue: z.number().optional(),
  overageDiscountPercent: z
    .number({ error: 'Must be a number' })
    .gte(0, 'Must be 0 or greater')
    .lte(100, 'Must be 100 or less'),
  displayOrder: z.number({ error: 'Must be a number' }).int().gte(0),
  pickupIncluded: z.boolean(),
  deliveryIncluded: z.boolean(),
  expressIncluded: z.boolean(),
  isPublic: z.boolean(),
  isFeatured: z.boolean(),
  // Edit-only: status is only meaningful on update.
  status: z.enum(['draft', 'active', 'paused', 'retired'] as const).optional(),
})

type PlanValues = z.infer<typeof planSchema>

const defaultPlanValues: PlanValues = {
  code: '',
  name: '',
  nameEn: '',
  nameHi: '',
  description: '',
  tier: 'standard',
  billingInterval: 'monthly',
  intervalCount: 1,
  price: 0,
  setupFee: 0,
  currencyCode: 'INR',
  trialDays: 0,
  quotaType: 'credit',
  quotaValue: undefined,
  overageDiscountPercent: 0,
  displayOrder: 100,
  pickupIncluded: true,
  deliveryIncluded: true,
  expressIncluded: false,
  isPublic: true,
  isFeatured: false,
  status: 'draft',
}

export function SubscriptionPlanDrawer({
  open,
  plan,
  onClose,
}: {
  open: boolean
  /** null = create; a plan = edit. */
  plan: SubscriptionPlanDto | null
  onClose: () => void
}) {
  const isEdit = !!plan
  const create = useCreateSubscriptionPlan()
  const update = useUpdateSubscriptionPlan()

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<PlanValues>({
    resolver: zodResolver(planSchema),
    defaultValues: defaultPlanValues,
  })

  useEffect(() => {
    if (!open) return
    if (plan) {
      const { en, hi } = parseNameLocalized(plan.nameLocalized)
      reset({
        code: plan.code,
        name: plan.name,
        nameEn: en,
        nameHi: hi,
        description: plan.description ?? '',
        tier: (plan.tier as PlanValues['tier']) ?? 'standard',
        billingInterval: (plan.billingInterval as PlanValues['billingInterval']) ?? 'monthly',
        intervalCount: plan.intervalCount,
        price: plan.price,
        setupFee: plan.setupFee,
        currencyCode: plan.currencyCode,
        trialDays: plan.trialDays,
        quotaType: (plan.quotaType as PlanValues['quotaType']) ?? 'credit',
        quotaValue: plan.quotaValue ?? undefined,
        overageDiscountPercent: plan.overageDiscountPercent,
        displayOrder: plan.displayOrder,
        pickupIncluded: plan.pickupIncluded,
        deliveryIncluded: plan.deliveryIncluded,
        expressIncluded: plan.expressIncluded,
        isPublic: plan.isPublic,
        isFeatured: plan.isFeatured,
        status: (plan.status as PlanValues['status']) ?? 'draft',
      })
    } else {
      reset(defaultPlanValues)
    }
  }, [open, plan, reset])

  if (!open) return null

  const submit = handleSubmit(async (values) => {
    const nameLocalized = buildNameLocalized(values.nameEn, values.nameHi ?? '')
    try {
      if (isEdit && plan) {
        const payload: UpdateSubscriptionPlanPayload = {
          name: values.name.trim(),
          nameLocalized,
          description: values.description?.trim() || null,
          tier: values.tier,
          price: values.price,
          setupFee: values.setupFee,
          quotaType: values.quotaType,
          quotaValue: values.quotaValue ?? null,
          rolloverUnused: plan.rolloverUnused,
          maxRollover: plan.maxRollover,
          overageDiscountPercent: values.overageDiscountPercent,
          applicableServices: plan.applicableServices,
          excludedServices: plan.excludedServices,
          pickupIncluded: values.pickupIncluded,
          deliveryIncluded: values.deliveryIncluded,
          expressIncluded: values.expressIncluded,
          maxActiveSubscribers: plan.maxActiveSubscribers,
          gateway: plan.gateway,
          gatewayPlanId: plan.gatewayPlanId,
          termsAndConditions: plan.termsAndConditions,
          iconUrl: plan.iconUrl,
          colorHex: plan.colorHex,
          displayOrder: values.displayOrder,
          isPublic: values.isPublic,
          isFeatured: values.isFeatured,
          status: values.status ?? plan.status,
          availableFrom: plan.availableFrom,
          availableTo: plan.availableTo,
        }
        await update.mutateAsync({ id: plan.id, payload })
      } else {
        const payload: CreateSubscriptionPlanPayload = {
          code: values.code.trim(),
          name: values.name.trim(),
          nameLocalized,
          description: values.description?.trim() || null,
          tier: values.tier,
          billingInterval: values.billingInterval,
          intervalCount: values.intervalCount,
          price: values.price,
          setupFee: values.setupFee,
          currencyCode: values.currencyCode.trim().toUpperCase(),
          trialDays: values.trialDays,
          quotaType: values.quotaType,
          quotaValue: values.quotaValue ?? null,
          rolloverUnused: false,
          maxRollover: null,
          overageDiscountPercent: values.overageDiscountPercent,
          applicableServices: null,
          excludedServices: null,
          pickupIncluded: values.pickupIncluded,
          deliveryIncluded: values.deliveryIncluded,
          expressIncluded: values.expressIncluded,
          maxActiveSubscribers: null,
          displayOrder: values.displayOrder,
          isPublic: values.isPublic,
          isFeatured: values.isFeatured,
        }
        await create.mutateAsync(payload)
      }
      onClose()
    } catch (e) {
      setError('root', { message: apiErrorMessage(e, 'Could not save the plan.') })
    }
  })

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Tag}
      eyebrow={isEdit ? <>Edit plan · <span className="font-mono">{plan?.code}</span></> : 'Subscriptions'}
      title={isEdit ? plan?.name ?? 'Edit plan' : 'New subscription plan'}
      width="md"
      error={errors.root?.message}
      onSubmit={() => void submit()}
      submitLabel={isEdit ? 'Save changes' : 'Create plan'}
      submittingLabel="Saving…"
      submitIcon={isEdit ? Save : Plus}
      submitting={isSubmitting || create.isPending || update.isPending}
    >
      <DrawerSection title="Identity">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Code *" hint={isEdit ? 'Code cannot be changed after creation.' : 'Unique per brand, e.g. BASIC_M'}>
            <input
              {...register('code')}
              aria-invalid={!!errors.code}
              aria-required="true"
              aria-describedby={errors.code ? 'plan-code-error' : undefined}
              className={drawerInputCls}
              placeholder="BASIC_M"
              disabled={isEdit}
            />
            <FieldError id="plan-code-error" message={errors.code?.message} />
          </Field>
          <Field label="Internal name *">
            <input
              {...register('name')}
              aria-invalid={!!errors.name}
              aria-required="true"
              aria-describedby={errors.name ? 'plan-name-error' : undefined}
              className={drawerInputCls}
              placeholder="Basic Monthly"
            />
            <FieldError id="plan-name-error" message={errors.name?.message} />
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Display name (English) *" hint="Shown to customers.">
            <input
              {...register('nameEn')}
              aria-invalid={!!errors.nameEn}
              aria-required="true"
              aria-describedby={errors.nameEn ? 'plan-nameen-error' : undefined}
              className={drawerInputCls}
              placeholder="Basic"
            />
            <FieldError id="plan-nameen-error" message={errors.nameEn?.message} />
          </Field>
          <Field label="Display name (Hindi)" hint="Optional localized label.">
            <input
              {...register('nameHi')}
              className={drawerInputCls}
              placeholder="बेसिक"
            />
          </Field>
        </div>
        <Field label="Description">
          <textarea
            {...register('description')}
            rows={2}
            className={drawerInputCls}
            placeholder="Short summary shown on the plan card…"
          />
        </Field>
      </DrawerSection>

      <DrawerSection title="Billing">
        <div className="grid grid-cols-3 gap-3">
          <Field label="Tier">
            <select {...register('tier')} className={drawerInputCls}>
              {TIERS.map((t) => <option key={t} value={t} className="capitalize">{t}</option>)}
            </select>
          </Field>
          <Field label="Billing interval" hint={isEdit ? 'Locked after creation.' : undefined}>
            <select {...register('billingInterval')} className={drawerInputCls} disabled={isEdit}>
              {BILLING_INTERVALS.map((b) => (
                <option key={b} value={b} className="capitalize">{b.replace(/_/g, ' ')}</option>
              ))}
            </select>
          </Field>
          <Field label="Interval count *">
            <input
              {...register('intervalCount', { valueAsNumber: true })}
              type="number"
              min="1"
              aria-invalid={!!errors.intervalCount}
              aria-describedby={errors.intervalCount ? 'plan-intervalcount-error' : undefined}
              className={drawerInputCls}
              disabled={isEdit}
            />
            <FieldError id="plan-intervalcount-error" message={errors.intervalCount?.message} />
          </Field>
        </div>
        <div className="grid grid-cols-3 gap-3">
          <Field label="Price (₹) *">
            <input
              {...register('price', { valueAsNumber: true })}
              type="number"
              min="0"
              step="0.01"
              aria-invalid={!!errors.price}
              aria-required="true"
              aria-describedby={errors.price ? 'plan-price-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="plan-price-error" message={errors.price?.message} />
          </Field>
          <Field label="Setup fee (₹)">
            <input
              {...register('setupFee', { valueAsNumber: true })}
              type="number"
              min="0"
              step="0.01"
              aria-invalid={!!errors.setupFee}
              aria-describedby={errors.setupFee ? 'plan-setupfee-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="plan-setupfee-error" message={errors.setupFee?.message} />
          </Field>
          <Field label="Currency *">
            <input
              {...register('currencyCode')}
              aria-invalid={!!errors.currencyCode}
              aria-required="true"
              aria-describedby={errors.currencyCode ? 'plan-currency-error' : undefined}
              className={drawerInputCls}
              maxLength={3}
              placeholder="INR"
              disabled={isEdit}
            />
            <FieldError id="plan-currency-error" message={errors.currencyCode?.message} />
          </Field>
        </div>
        <div className="grid grid-cols-3 gap-3">
          <Field label="Trial days">
            <input
              {...register('trialDays', { valueAsNumber: true })}
              type="number"
              min="0"
              aria-invalid={!!errors.trialDays}
              aria-describedby={errors.trialDays ? 'plan-trialdays-error' : undefined}
              className={drawerInputCls}
              disabled={isEdit}
            />
            <FieldError id="plan-trialdays-error" message={errors.trialDays?.message} />
          </Field>
          <Field label="Overage discount %">
            <input
              {...register('overageDiscountPercent', { valueAsNumber: true })}
              type="number"
              min="0"
              max="100"
              step="0.01"
              aria-invalid={!!errors.overageDiscountPercent}
              aria-describedby={errors.overageDiscountPercent ? 'plan-overage-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="plan-overage-error" message={errors.overageDiscountPercent?.message} />
          </Field>
          <Field label="Display order">
            <input
              {...register('displayOrder', { valueAsNumber: true })}
              type="number"
              min="0"
              aria-invalid={!!errors.displayOrder}
              aria-describedby={errors.displayOrder ? 'plan-order-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="plan-order-error" message={errors.displayOrder?.message} />
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="Quota & inclusions">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Quota type" hint={isEdit ? undefined : 'How usage is metered.'}>
            <select {...register('quotaType')} className={drawerInputCls}>
              {QUOTA_TYPES.map((q) => (
                <option key={q} value={q} className="capitalize">{q.replace(/_/g, ' ')}</option>
              ))}
            </select>
          </Field>
          <Field label="Quota value" hint="Blank for unlimited.">
            <input
              {...register('quotaValue', { valueAsNumber: true })}
              type="number"
              min="0"
              step="0.01"
              className={drawerInputCls}
              placeholder="e.g. 10"
            />
          </Field>
        </div>
        <div className="flex flex-wrap gap-4 pt-1">
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input type="checkbox" {...register('pickupIncluded')} className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30" />
            Pickup included
          </label>
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input type="checkbox" {...register('deliveryIncluded')} className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30" />
            Delivery included
          </label>
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input type="checkbox" {...register('expressIncluded')} className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30" />
            Express included
          </label>
        </div>
      </DrawerSection>

      <DrawerSection title="Visibility">
        {isEdit && (
          <Field label="Status">
            <select {...register('status')} className={drawerInputCls}>
              {PLAN_STATUSES.map((s) => <option key={s} value={s} className="capitalize">{s}</option>)}
            </select>
          </Field>
        )}
        <div className="flex flex-wrap gap-4 pt-1">
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input type="checkbox" {...register('isPublic')} className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30" />
            Public (customer-facing)
          </label>
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input type="checkbox" {...register('isFeatured')} className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30" />
            Featured
          </label>
        </div>
      </DrawerSection>
    </FormDrawer>
  )
}

// ── Plan detail (view + publish / archive actions) ──────────────────────────────

export function SubscriptionPlanDetailDrawer({
  plan,
  onClose,
  onEdit,
  canManage,
}: {
  plan: SubscriptionPlanDto | null
  onClose: () => void
  onEdit: (p: SubscriptionPlanDto) => void
  canManage: boolean
}) {
  const patchStatus = usePatchSubscriptionPlanStatus()
  const del = useDeleteSubscriptionPlan()
  const [error, setError] = useState<string | null>(null)
  const gate = useConfirm()

  useEffect(() => {
    if (plan) setError(null)
  }, [plan])

  if (!plan) return null

  const { en, hi } = parseNameLocalized(plan.nameLocalized)

  // Status-only PATCH: sends just { status } so a concurrent edit to
  // price/quota/features isn't clobbered by re-PUTting this stale DTO (WEB-6).
  const setStatus = async (status: string) => {
    setError(null)
    try {
      await patchStatus.mutateAsync({ id: plan.id, status })
      onClose()
    } catch (e) {
      setError(apiErrorMessage(e, 'Could not update plan status.'))
    }
  }

  const handleDelete = async () => {
    setError(null)
    try {
      await del.mutateAsync(plan.id)
      onClose()
    } catch (e) {
      setError(apiErrorMessage(e, 'Could not delete plan.'))
    }
  }

  const busy = patchStatus.isPending || del.isPending

  return (
    <FormDrawer
      open={!!plan}
      onClose={onClose}
      icon={Tag}
      eyebrow={<>Plan · <span className="font-mono">{plan.code}</span></>}
      title={plan.name}
      width="md"
      footer={null}
    >
      <DrawerSection>
        <div className="flex flex-wrap items-center gap-2">
          <PlanStatusBadge status={plan.status} />
          <span className="rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium capitalize text-gray-600">
            {plan.tier}
          </span>
          {plan.isFeatured && (
            <span className="rounded-full bg-amber-100 px-2.5 py-0.5 text-xs font-medium text-amber-700">Featured</span>
          )}
          {!plan.isPublic && (
            <span className="rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-500">Private</span>
          )}
        </div>
      </DrawerSection>

      <DetailSection title="Plan">
        <DetailRow label="Display name (en)" value={en || '—'} />
        <DetailRow label="Display name (hi)" value={hi || '—'} />
        <DetailRow
          label="Price"
          value={`${formatCurrency(plan.price)} / ${plan.intervalCount > 1 ? `${plan.intervalCount} ` : ''}${plan.billingInterval.replace(/_/g, ' ')}`}
        />
        <DetailRow label="Setup fee" value={formatCurrency(plan.setupFee)} />
        <DetailRow label="Trial" value={plan.trialDays > 0 ? `${plan.trialDays} days` : 'None'} />
        <DetailRow
          label="Quota"
          value={
            plan.quotaType === 'unlimited'
              ? 'Unlimited'
              : `${plan.quotaValue ?? '—'} ${plan.quotaType.replace(/_/g, ' ')}`
          }
        />
        <DetailRow label="Subscribers" value={`${plan.currentSubscriberCount}${plan.maxActiveSubscribers ? ` / ${plan.maxActiveSubscribers}` : ''}`} />
      </DetailSection>

      <DetailSection title="Inclusions">
        <DetailRow label="Pickup" value={plan.pickupIncluded ? 'Included' : 'No'} />
        <DetailRow label="Delivery" value={plan.deliveryIncluded ? 'Included' : 'No'} />
        <DetailRow label="Express" value={plan.expressIncluded ? 'Included' : 'No'} />
      </DetailSection>

      {plan.description && (
        <DetailSection title="Description">
          <p className="px-3 py-2 text-sm text-gray-600">{plan.description}</p>
        </DetailSection>
      )}

      {error && (
        <p className="text-xs text-red-600" role="alert">{error}</p>
      )}

      {canManage && (
        <div className="space-y-2 pt-1">
          <button
            type="button"
            onClick={() => onEdit(plan)}
            className="w-full rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Edit plan
          </button>
          {(plan.status === 'draft' || plan.status === 'paused' || plan.status === 'retired') && (
            <button
              type="button"
              onClick={() =>
                gate.confirm({
                  title: 'Publish plan?',
                  description: `“${plan.name}” will become active and visible to customers if it is public.`,
                  confirmLabel: 'Publish',
                  tone: 'warning',
                  onConfirm: () => setStatus('active'),
                })
              }
              disabled={busy}
              className="inline-flex w-full items-center justify-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
            >
              <CheckCircle2 className="h-3.5 w-3.5" /> Publish (activate)
            </button>
          )}
          {plan.status === 'active' && (
            <button
              type="button"
              onClick={() =>
                gate.confirm({
                  title: 'Pause plan?',
                  description: `“${plan.name}” will stop accepting new subscribers until republished.`,
                  confirmLabel: 'Pause',
                  tone: 'warning',
                  onConfirm: () => setStatus('paused'),
                })
              }
              disabled={busy}
              className="inline-flex w-full items-center justify-center gap-1.5 rounded-lg border border-amber-200 bg-amber-50 px-4 py-2 text-sm font-semibold text-amber-700 hover:bg-amber-100 disabled:opacity-60"
            >
              <Ban className="h-3.5 w-3.5" /> Pause
            </button>
          )}
          {plan.status !== 'retired' && (
            <button
              type="button"
              onClick={() =>
                gate.confirm({
                  title: 'Archive plan?',
                  description: `“${plan.name}” will be retired and removed from the customer-facing catalog.`,
                  confirmLabel: 'Archive',
                  tone: 'warning',
                  onConfirm: () => setStatus('retired'),
                })
              }
              disabled={busy}
              className="inline-flex w-full items-center justify-center gap-1.5 rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50 disabled:opacity-60"
            >
              <Archive className="h-3.5 w-3.5" /> Archive (retire)
            </button>
          )}
          {plan.currentSubscriberCount === 0 && (
            <button
              type="button"
              onClick={() =>
                gate.confirm({
                  title: 'Delete plan?',
                  description: `“${plan.name}” (${plan.code}) will be permanently deleted. This cannot be undone.`,
                  confirmLabel: 'Delete plan',
                  tone: 'danger',
                  onConfirm: () => handleDelete(),
                })
              }
              disabled={busy}
              className="w-full rounded-lg border border-red-200 px-4 py-2 text-sm font-medium text-red-600 hover:bg-red-50 disabled:opacity-60"
            >
              Delete plan
            </button>
          )}
        </div>
      )}
      <ConfirmDialog {...gate.dialogProps} />
    </FormDrawer>
  )
}

// ── Customer subscription detail (read-only) ────────────────────────────────────

export function CustomerSubscriptionDetailDrawer({
  subscription,
  onClose,
}: {
  subscription: CustomerSubscriptionDto | null
  onClose: () => void
}) {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('subscription.manage')
  const patchStatus = usePatchCustomerSubscriptionStatus()
  const gate = useConfirm()
  const [error, setError] = useState<string | null>(null)

  // Reset any prior action error whenever a different subscription opens.
  useEffect(() => {
    setError(null)
  }, [subscription?.id])

  if (!subscription) return null
  const s = subscription

  // Status-conditional actions. Backend PATCH accepts active|suspended|cancelled|paused
  // and rejects stale writes via expectedUpdatedAt (we send the row's last-seen updatedAt).
  // NOTE: "Retry billing" for dunning is intentionally omitted — there is no
  // backend retry/recharge endpoint yet (only the status PATCH exists).
  const runAction = (status: CustomerSubscriptionStatus, label: string, tone: 'danger' | 'warning' | 'default') => {
    gate.confirm({
      title: `${label}?`,
      description: `This will set subscription ${s.subscriptionNumber} to "${status}".`,
      confirmLabel: label,
      tone,
      onConfirm: async () => {
        setError(null)
        try {
          await patchStatus.mutateAsync({ id: s.id, status, expectedUpdatedAt: s.updatedAt })
          onClose()
        } catch (e) {
          // Surface the 409 stale-write / validation message inline; rethrow so the
          // confirm dialog stays open for the operator to read and retry.
          setError(apiErrorMessage(e, `Could not ${label.toLowerCase()} this subscription.`))
          throw e
        }
      },
    })
  }

  const isActive = s.status === 'active'
  const isPastDue = s.status === 'past_due'
  const isPaused = s.status === 'paused' || s.status === 'suspended'
  const isTerminal = s.status === 'cancelled' || s.status === 'expired'

  const actions: { key: string; label: string; icon: React.ElementType; onClick: () => void; cls: string }[] = []
  if (canManage && !isTerminal) {
    if (isActive || isPastDue) {
      actions.push({
        key: 'cancel',
        label: 'Cancel',
        icon: XCircle,
        onClick: () => runAction('cancelled', 'Cancel', 'danger'),
        cls: 'border-red-200 text-red-600 hover:bg-red-50',
      })
    }
    if (isActive) {
      actions.push({
        key: 'pause',
        label: 'Pause',
        icon: Pause,
        onClick: () => runAction('paused', 'Pause', 'warning'),
        cls: 'border-amber-200 text-amber-700 hover:bg-amber-50',
      })
    }
    if (isPaused) {
      actions.push({
        key: 'resume',
        label: 'Resume',
        icon: Play,
        onClick: () => runAction('active', 'Resume', 'default'),
        cls: 'border-lg-green/30 text-lg-green hover:bg-lg-green/5',
      })
    }
  }

  const footer =
    actions.length === 0 ? null : (
      <div className="flex flex-wrap justify-end gap-2">
        {actions.map((a) => (
          <button
            key={a.key}
            type="button"
            onClick={a.onClick}
            disabled={patchStatus.isPending}
            className={`inline-flex items-center gap-1.5 rounded-lg border px-4 py-2 text-sm font-semibold disabled:opacity-60 ${a.cls}`}
          >
            <a.icon className="h-4 w-4" /> {a.label}
          </button>
        ))}
      </div>
    )

  return (
    <FormDrawer
      open={!!s}
      onClose={onClose}
      icon={UserIcon}
      eyebrow={<>Subscription · <span className="font-mono">{s.subscriptionNumber}</span></>}
      title={`${formatCurrency(s.priceSnapshot)} / ${s.billingInterval.replace(/_/g, ' ')}`}
      width="md"
      error={error}
      footer={footer}
    >
      <DrawerSection>
        <SubscriptionStatusBadge status={s.status} />
      </DrawerSection>

      <DetailSection title="Billing">
        <DetailRow label="Price" value={formatCurrency(s.priceSnapshot)} />
        <DetailRow
          label="Interval"
          value={`${s.intervalCount > 1 ? `${s.intervalCount} ` : ''}${s.billingInterval.replace(/_/g, ' ')}`}
        />
        <DetailRow label="Auto-renew" value={s.autoRenew ? 'Yes' : 'No'} />
        <DetailRow label="Cancel at period end" value={s.cancelAtPeriodEnd ? 'Yes' : 'No'} />
        <DetailRow label="Cycles billed" value={s.totalCyclesBilled} />
        <DetailRow label="Dunning attempts" value={s.dunningAttempts} />
      </DetailSection>

      <DetailSection title="Period">
        <DetailRow label="Current start" value={s.currentPeriodStart ? formatDate(s.currentPeriodStart) : '—'} />
        <DetailRow label="Current end" value={s.currentPeriodEnd ? formatDate(s.currentPeriodEnd) : '—'} />
        <DetailRow label="Next billing" value={s.nextBillingAt ? formatDate(s.nextBillingAt) : '—'} />
        <DetailRow label="Cancelled at" value={s.cancelledAt ? formatDate(s.cancelledAt) : '—'} />
      </DetailSection>

      <DetailSection title="Quota">
        <DetailRow
          label="Type"
          value={<span className="capitalize">{s.quotaType.replace(/_/g, ' ')}</span>}
        />
        <DetailRow label="Quota value" value={s.quotaValue ?? '—'} />
        <DetailRow label="Credits remaining" value={s.creditsRemaining} />
      </DetailSection>

      <DetailSection title="Record">
        <DetailRow label="Customer ID" value={<span className="font-mono text-xs">{s.customerId.slice(0, 8)}…</span>} />
        <DetailRow label="Plan ID" value={<span className="font-mono text-xs">{s.planId.slice(0, 8)}…</span>} />
        <DetailRow label="Created" value={formatDate(s.createdAt)} />
        <DetailRow label="Updated" value={formatDate(s.updatedAt)} />
      </DetailSection>

      <ConfirmDialog {...gate.dialogProps} />
    </FormDrawer>
  )
}

// ── Badges ──────────────────────────────────────────────────────────────────────

export function PlanStatusBadge({ status }: { status: string }) {
  const cls =
    status === 'active'
      ? 'bg-green-100 text-green-700'
      : status === 'paused'
        ? 'bg-amber-100 text-amber-700'
        : status === 'retired'
          ? 'bg-gray-100 text-gray-500'
          : 'bg-blue-100 text-blue-700' // draft
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium capitalize ${cls}`}>
      {status}
    </span>
  )
}

export function SubscriptionStatusBadge({ status }: { status: string }) {
  const cls =
    status === 'active'
      ? 'bg-green-100 text-green-700'
      : status === 'trialing'
        ? 'bg-blue-100 text-blue-700'
        : status === 'past_due'
          ? 'bg-red-100 text-red-700'
          : status === 'paused'
            ? 'bg-amber-100 text-amber-700'
            : status === 'pending'
              ? 'bg-gray-100 text-gray-600'
              : 'bg-gray-100 text-gray-500' // cancelled / expired
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium capitalize ${cls}`}>
      {status.replace(/_/g, ' ')}
    </span>
  )
}

export const SUBSCRIPTION_STATUSES = [
  'pending',
  'trialing',
  'active',
  'past_due',
  'paused',
  'cancelled',
  'expired',
]
