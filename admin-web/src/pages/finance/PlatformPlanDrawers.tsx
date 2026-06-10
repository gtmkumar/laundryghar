import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Layers, Plus, Save, Archive, CheckCircle2, Building2, Ban } from 'lucide-react'
import {
  useCreatePlatformPlan,
  useUpdatePlatformPlan,
  useDeletePlatformPlan,
  useFranchiseSubscriptionActions,
} from '@/hooks/useFinance'
import { useFranchises } from '@/hooks/useTenancy'
import { useBrandStore } from '@/stores/brandStore'
import {
  FormDrawer,
  DrawerSection,
  Field,
  drawerInputCls,
  DetailSection,
  DetailRow,
} from '@/components/shared/FormDrawer'
import { FieldError } from '@/components/ui/FieldError'
import { nonNegativeMoney } from '@/lib/validation'
import { apiErrorMessage } from '@/lib/apiError'
import type {
  PlatformPlanDto,
  CreatePlatformPlanPayload,
  UpdatePlatformPlanPayload,
  FranchiseSubscriptionDto,
} from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

const TIERS = ['starter', 'growth', 'pro', 'enterprise', 'custom']
const BILLING_INTERVALS = ['monthly', 'quarterly', 'yearly']
const SUPPORT_LEVELS = ['community', 'email', 'priority', 'dedicated']
const PLAN_STATUSES = ['draft', 'active', 'retired']
const PAYMENT_METHODS = ['invoice', 'auto_debit']

/**
 * `features` is stored as a JSON-object STRING (validated server-side). We expose
 * a validated textarea: the operator types JSON, and we reject anything that
 * isn't a JSON object before submit, mirroring the backend rule.
 */
const jsonObjectString = z
  .string()
  .optional()
  .refine(
    (v) => {
      const raw = (v ?? '').trim()
      if (!raw) return true // blank → we'll send "{}"
      try {
        const parsed = JSON.parse(raw)
        return parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed)
      } catch {
        return false
      }
    },
    { message: 'Features must be a valid JSON object, e.g. {"priority_support": true}' },
  )

function normalizeFeatures(v: string | undefined): string {
  const raw = (v ?? '').trim()
  if (!raw) return '{}'
  return JSON.stringify(JSON.parse(raw))
}

function prettyFeatures(raw: string | null | undefined): string {
  if (!raw) return '{}'
  try {
    return JSON.stringify(JSON.parse(raw), null, 2)
  } catch {
    return raw
  }
}

// ── Platform plan create / edit ───────────────────────────────────────────────

const planSchema = z.object({
  code: z.string().min(1, 'Code is required.').max(50, 'Max 50 characters.'),
  name: z.string().min(1, 'Name is required.').max(100, 'Max 100 characters.'),
  description: z.string().optional(),
  tier: z.enum(['starter', 'growth', 'pro', 'enterprise', 'custom'] as const),
  billingInterval: z.enum(['monthly', 'quarterly', 'yearly'] as const),
  intervalCount: z.number({ error: 'Must be a number' }).int().gt(0, 'Must be at least 1'),
  price: nonNegativeMoney,
  setupFee: nonNegativeMoney,
  annualDiscountPercent: z.number({ error: 'Must be a number' }).gte(0).lte(100, 'Must be 0–100'),
  currencyCode: z.string().length(3, 'Use a 3-letter code, e.g. INR'),
  trialDays: z.number({ error: 'Must be a number' }).int().gte(0),
  maxStores: z.number().int().optional(),
  maxWarehouses: z.number().int().optional(),
  maxUsers: z.number().int().optional(),
  maxOrdersPerMonth: z.number().int().optional(),
  maxRiders: z.number().int().optional(),
  overagePerOrder: nonNegativeMoney,
  overagePerStore: nonNegativeMoney,
  overagePerUser: nonNegativeMoney,
  features: jsonObjectString,
  supportLevel: z.enum(['community', 'email', 'priority', 'dedicated'] as const),
  displayOrder: z.number({ error: 'Must be a number' }).int().gte(0),
  isPublic: z.boolean(),
  isFeatured: z.boolean(),
  status: z.enum(['draft', 'active', 'retired'] as const).optional(),
})

type PlanValues = z.infer<typeof planSchema>

const defaultPlanValues: PlanValues = {
  code: '',
  name: '',
  description: '',
  tier: 'starter',
  billingInterval: 'monthly',
  intervalCount: 1,
  price: 0,
  setupFee: 0,
  annualDiscountPercent: 0,
  currencyCode: 'INR',
  trialDays: 0,
  maxStores: undefined,
  maxWarehouses: undefined,
  maxUsers: undefined,
  maxOrdersPerMonth: undefined,
  maxRiders: undefined,
  overagePerOrder: 0,
  overagePerStore: 0,
  overagePerUser: 0,
  features: '{}',
  supportLevel: 'email',
  displayOrder: 100,
  isPublic: true,
  isFeatured: false,
  status: 'draft',
}

/** number | undefined → null for the API (blank = unlimited). */
function nz(v: number | undefined): number | null {
  return v === undefined || Number.isNaN(v) ? null : v
}

export function PlatformPlanDrawer({
  open,
  plan,
  onClose,
}: {
  open: boolean
  plan: PlatformPlanDto | null
  onClose: () => void
}) {
  const isEdit = !!plan
  const create = useCreatePlatformPlan()
  const update = useUpdatePlatformPlan()

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
      reset({
        code: plan.code,
        name: plan.name,
        description: plan.description ?? '',
        tier: (plan.tier as PlanValues['tier']) ?? 'starter',
        billingInterval: (plan.billingInterval as PlanValues['billingInterval']) ?? 'monthly',
        intervalCount: plan.intervalCount,
        price: plan.price,
        setupFee: plan.setupFee,
        annualDiscountPercent: plan.annualDiscountPercent,
        currencyCode: plan.currencyCode,
        trialDays: plan.trialDays,
        maxStores: plan.maxStores ?? undefined,
        maxWarehouses: plan.maxWarehouses ?? undefined,
        maxUsers: plan.maxUsers ?? undefined,
        maxOrdersPerMonth: plan.maxOrdersPerMonth ?? undefined,
        maxRiders: plan.maxRiders ?? undefined,
        overagePerOrder: plan.overagePerOrder,
        overagePerStore: plan.overagePerStore,
        overagePerUser: plan.overagePerUser,
        features: prettyFeatures(plan.features),
        supportLevel: (plan.supportLevel as PlanValues['supportLevel']) ?? 'email',
        displayOrder: plan.displayOrder,
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
    const features = normalizeFeatures(values.features)
    try {
      if (isEdit && plan) {
        const payload: UpdatePlatformPlanPayload = {
          name: values.name.trim(),
          description: values.description?.trim() || null,
          tier: values.tier,
          price: values.price,
          setupFee: values.setupFee,
          annualDiscountPercent: values.annualDiscountPercent,
          maxStores: nz(values.maxStores),
          maxWarehouses: nz(values.maxWarehouses),
          maxUsers: nz(values.maxUsers),
          maxOrdersPerMonth: nz(values.maxOrdersPerMonth),
          maxRiders: nz(values.maxRiders),
          overagePerOrder: values.overagePerOrder,
          overagePerStore: values.overagePerStore,
          overagePerUser: values.overagePerUser,
          features,
          supportLevel: values.supportLevel,
          isPublic: values.isPublic,
          isFeatured: values.isFeatured,
          displayOrder: values.displayOrder,
          status: values.status ?? plan.status,
        }
        await update.mutateAsync({ id: plan.id, payload })
      } else {
        const payload: CreatePlatformPlanPayload = {
          brandId: null,
          code: values.code.trim(),
          name: values.name.trim(),
          description: values.description?.trim() || null,
          tier: values.tier,
          billingInterval: values.billingInterval,
          intervalCount: values.intervalCount,
          price: values.price,
          setupFee: values.setupFee,
          annualDiscountPercent: values.annualDiscountPercent,
          currencyCode: values.currencyCode.trim().toUpperCase(),
          trialDays: values.trialDays,
          maxStores: nz(values.maxStores),
          maxWarehouses: nz(values.maxWarehouses),
          maxUsers: nz(values.maxUsers),
          maxOrdersPerMonth: nz(values.maxOrdersPerMonth),
          maxRiders: nz(values.maxRiders),
          overagePerOrder: values.overagePerOrder,
          overagePerStore: values.overagePerStore,
          overagePerUser: values.overagePerUser,
          features,
          supportLevel: values.supportLevel,
          isPublic: values.isPublic,
          isFeatured: values.isFeatured,
          displayOrder: values.displayOrder,
        }
        await create.mutateAsync(payload)
      }
      onClose()
    } catch (e) {
      setError('root', { message: apiErrorMessage(e, 'Could not save the platform plan.') })
    }
  })

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Layers}
      eyebrow={isEdit ? <>Edit plan · <span className="font-mono">{plan?.code}</span></> : 'SaaS · Platform plans'}
      title={isEdit ? plan?.name ?? 'Edit plan' : 'New platform plan'}
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
          <Field label="Code *" hint={isEdit ? 'Locked after creation.' : 'Unique, e.g. GROWTH_M'}>
            <input
              {...register('code')}
              aria-invalid={!!errors.code}
              aria-required="true"
              aria-describedby={errors.code ? 'pp-code-error' : undefined}
              className={drawerInputCls}
              placeholder="GROWTH_M"
              disabled={isEdit}
            />
            <FieldError id="pp-code-error" message={errors.code?.message} />
          </Field>
          <Field label="Name *">
            <input
              {...register('name')}
              aria-invalid={!!errors.name}
              aria-required="true"
              aria-describedby={errors.name ? 'pp-name-error' : undefined}
              className={drawerInputCls}
              placeholder="Growth"
            />
            <FieldError id="pp-name-error" message={errors.name?.message} />
          </Field>
        </div>
        <Field label="Description">
          <textarea {...register('description')} rows={2} className={drawerInputCls} />
        </Field>
      </DrawerSection>

      <DrawerSection title="Billing">
        <div className="grid grid-cols-3 gap-3">
          <Field label="Tier">
            <select {...register('tier')} className={drawerInputCls}>
              {TIERS.map((t) => <option key={t} value={t} className="capitalize">{t}</option>)}
            </select>
          </Field>
          <Field label="Billing interval" hint={isEdit ? 'Locked.' : undefined}>
            <select {...register('billingInterval')} className={drawerInputCls} disabled={isEdit}>
              {BILLING_INTERVALS.map((b) => <option key={b} value={b} className="capitalize">{b}</option>)}
            </select>
          </Field>
          <Field label="Interval count *">
            <input
              {...register('intervalCount', { valueAsNumber: true })}
              type="number"
              min="1"
              aria-invalid={!!errors.intervalCount}
              aria-describedby={errors.intervalCount ? 'pp-interval-error' : undefined}
              className={drawerInputCls}
              disabled={isEdit}
            />
            <FieldError id="pp-interval-error" message={errors.intervalCount?.message} />
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
              aria-describedby={errors.price ? 'pp-price-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="pp-price-error" message={errors.price?.message} />
          </Field>
          <Field label="Setup fee (₹)">
            <input
              {...register('setupFee', { valueAsNumber: true })}
              type="number"
              min="0"
              step="0.01"
              aria-invalid={!!errors.setupFee}
              aria-describedby={errors.setupFee ? 'pp-setup-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="pp-setup-error" message={errors.setupFee?.message} />
          </Field>
          <Field label="Currency *">
            <input
              {...register('currencyCode')}
              aria-invalid={!!errors.currencyCode}
              aria-required="true"
              aria-describedby={errors.currencyCode ? 'pp-currency-error' : undefined}
              className={drawerInputCls}
              maxLength={3}
              placeholder="INR"
              disabled={isEdit}
            />
            <FieldError id="pp-currency-error" message={errors.currencyCode?.message} />
          </Field>
        </div>
        <div className="grid grid-cols-3 gap-3">
          <Field label="Annual discount %">
            <input
              {...register('annualDiscountPercent', { valueAsNumber: true })}
              type="number"
              min="0"
              max="100"
              step="0.01"
              aria-invalid={!!errors.annualDiscountPercent}
              aria-describedby={errors.annualDiscountPercent ? 'pp-anndisc-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="pp-anndisc-error" message={errors.annualDiscountPercent?.message} />
          </Field>
          <Field label="Trial days">
            <input
              {...register('trialDays', { valueAsNumber: true })}
              type="number"
              min="0"
              className={drawerInputCls}
              disabled={isEdit}
            />
          </Field>
          <Field label="Display order">
            <input
              {...register('displayOrder', { valueAsNumber: true })}
              type="number"
              min="0"
              aria-invalid={!!errors.displayOrder}
              aria-describedby={errors.displayOrder ? 'pp-order-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="pp-order-error" message={errors.displayOrder?.message} />
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="Limits (blank = unlimited)">
        <div className="grid grid-cols-3 gap-3">
          <Field label="Max stores">
            <input {...register('maxStores', { valueAsNumber: true })} type="number" min="0" className={drawerInputCls} />
          </Field>
          <Field label="Max warehouses">
            <input {...register('maxWarehouses', { valueAsNumber: true })} type="number" min="0" className={drawerInputCls} />
          </Field>
          <Field label="Max users">
            <input {...register('maxUsers', { valueAsNumber: true })} type="number" min="0" className={drawerInputCls} />
          </Field>
          <Field label="Max orders / mo">
            <input {...register('maxOrdersPerMonth', { valueAsNumber: true })} type="number" min="0" className={drawerInputCls} />
          </Field>
          <Field label="Max riders">
            <input {...register('maxRiders', { valueAsNumber: true })} type="number" min="0" className={drawerInputCls} />
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="Overage rates (₹)">
        <div className="grid grid-cols-3 gap-3">
          <Field label="Per order">
            <input {...register('overagePerOrder', { valueAsNumber: true })} type="number" min="0" step="0.01" aria-invalid={!!errors.overagePerOrder} className={drawerInputCls} />
            <FieldError message={errors.overagePerOrder?.message} />
          </Field>
          <Field label="Per store">
            <input {...register('overagePerStore', { valueAsNumber: true })} type="number" min="0" step="0.01" aria-invalid={!!errors.overagePerStore} className={drawerInputCls} />
            <FieldError message={errors.overagePerStore?.message} />
          </Field>
          <Field label="Per user">
            <input {...register('overagePerUser', { valueAsNumber: true })} type="number" min="0" step="0.01" aria-invalid={!!errors.overagePerUser} className={drawerInputCls} />
            <FieldError message={errors.overagePerUser?.message} />
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="Features & support">
        <Field label="Support level">
          <select {...register('supportLevel')} className={drawerInputCls}>
            {SUPPORT_LEVELS.map((s) => <option key={s} value={s} className="capitalize">{s}</option>)}
          </select>
        </Field>
        <Field label="Features (JSON object)" hint='e.g. {"priority_support": true, "api_access": false}'>
          <textarea
            {...register('features')}
            rows={4}
            aria-invalid={!!errors.features}
            aria-describedby={errors.features ? 'pp-features-error' : undefined}
            className={`${drawerInputCls} font-mono text-xs`}
            placeholder='{}'
          />
          <FieldError id="pp-features-error" message={errors.features?.message} />
        </Field>
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
            Public
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

// ── Platform plan detail (view + publish / archive) ─────────────────────────────

export function PlatformPlanDetailDrawer({
  plan,
  onClose,
  onEdit,
  canManage,
}: {
  plan: PlatformPlanDto | null
  onClose: () => void
  onEdit: (p: PlatformPlanDto) => void
  canManage: boolean
}) {
  const update = useUpdatePlatformPlan()
  const del = useDeletePlatformPlan()
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (plan) setError(null)
  }, [plan])

  if (!plan) return null

  const setStatus = async (status: string) => {
    setError(null)
    try {
      await update.mutateAsync({
        id: plan.id,
        payload: {
          name: plan.name,
          description: plan.description,
          tier: plan.tier,
          price: plan.price,
          setupFee: plan.setupFee,
          annualDiscountPercent: plan.annualDiscountPercent,
          maxStores: plan.maxStores,
          maxWarehouses: plan.maxWarehouses,
          maxUsers: plan.maxUsers,
          maxOrdersPerMonth: plan.maxOrdersPerMonth,
          maxRiders: plan.maxRiders,
          overagePerOrder: plan.overagePerOrder,
          overagePerStore: plan.overagePerStore,
          overagePerUser: plan.overagePerUser,
          features: plan.features,
          supportLevel: plan.supportLevel,
          isPublic: plan.isPublic,
          isFeatured: plan.isFeatured,
          displayOrder: plan.displayOrder,
          status,
        },
      })
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

  const busy = update.isPending || del.isPending
  const limit = (v: number | null) => (v === null ? 'Unlimited' : v.toLocaleString('en-IN'))

  return (
    <FormDrawer
      open={!!plan}
      onClose={onClose}
      icon={Layers}
      eyebrow={<>Platform plan · <span className="font-mono">{plan.code}</span></>}
      title={plan.name}
      width="md"
      footer={null}
    >
      <DrawerSection>
        <div className="flex flex-wrap items-center gap-2">
          <PlatformPlanStatusBadge status={plan.status} />
          <span className="rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium capitalize text-gray-600">{plan.tier}</span>
          {plan.isFeatured && <span className="rounded-full bg-amber-100 px-2.5 py-0.5 text-xs font-medium text-amber-700">Featured</span>}
          {!plan.isPublic && <span className="rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-500">Private</span>}
        </div>
      </DrawerSection>

      <DetailSection title="Billing">
        <DetailRow
          label="Price"
          value={`${formatCurrency(plan.price)} / ${plan.intervalCount > 1 ? `${plan.intervalCount} ` : ''}${plan.billingInterval}`}
        />
        <DetailRow label="Setup fee" value={formatCurrency(plan.setupFee)} />
        <DetailRow label="Annual discount" value={`${plan.annualDiscountPercent}%`} />
        <DetailRow label="Trial" value={plan.trialDays > 0 ? `${plan.trialDays} days` : 'None'} />
        <DetailRow label="Support" value={<span className="capitalize">{plan.supportLevel}</span>} />
      </DetailSection>

      <DetailSection title="Limits">
        <DetailRow label="Stores" value={limit(plan.maxStores)} />
        <DetailRow label="Warehouses" value={limit(plan.maxWarehouses)} />
        <DetailRow label="Users" value={limit(plan.maxUsers)} />
        <DetailRow label="Orders / mo" value={limit(plan.maxOrdersPerMonth)} />
        <DetailRow label="Riders" value={limit(plan.maxRiders)} />
      </DetailSection>

      <DetailSection title="Overage (₹)">
        <DetailRow label="Per order" value={formatCurrency(plan.overagePerOrder)} />
        <DetailRow label="Per store" value={formatCurrency(plan.overagePerStore)} />
        <DetailRow label="Per user" value={formatCurrency(plan.overagePerUser)} />
      </DetailSection>

      <DetailSection title="Features">
        <pre className="overflow-x-auto rounded-lg bg-gray-50 p-3 font-mono text-xs text-gray-600">
          {prettyFeatures(plan.features)}
        </pre>
      </DetailSection>

      {error && <p className="text-xs text-red-600" role="alert">{error}</p>}

      {canManage && (
        <div className="space-y-2 pt-1">
          <button
            type="button"
            onClick={() => onEdit(plan)}
            className="w-full rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Edit plan
          </button>
          {plan.status !== 'active' && (
            <button
              type="button"
              onClick={() => void setStatus('active')}
              disabled={busy}
              className="inline-flex w-full items-center justify-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
            >
              <CheckCircle2 className="h-3.5 w-3.5" /> Publish (activate)
            </button>
          )}
          {plan.status !== 'retired' && (
            <button
              type="button"
              onClick={() => void setStatus('retired')}
              disabled={busy}
              className="inline-flex w-full items-center justify-center gap-1.5 rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50 disabled:opacity-60"
            >
              <Archive className="h-3.5 w-3.5" /> Archive (retire)
            </button>
          )}
          <button
            type="button"
            onClick={() => void handleDelete()}
            disabled={busy}
            className="w-full rounded-lg border border-red-200 px-4 py-2 text-sm font-medium text-red-600 hover:bg-red-50 disabled:opacity-60"
          >
            Delete plan
          </button>
        </div>
      )}
    </FormDrawer>
  )
}

// ── Assign franchise plan ───────────────────────────────────────────────────────

export function AssignFranchisePlanDrawer({
  open,
  plans,
  onClose,
}: {
  open: boolean
  plans: PlatformPlanDto[]
  onClose: () => void
}) {
  const { activeBrandId } = useBrandStore()
  const franchisesQ = useFranchises({ brandId: activeBrandId ?? undefined, pageSize: 100 })
  const franchises = useMemo(() => franchisesQ.data?.list ?? [], [franchisesQ.data])
  const { assign } = useFranchiseSubscriptionActions()

  const [franchiseId, setFranchiseId] = useState('')
  const [platformPlanId, setPlatformPlanId] = useState('')
  const [paymentMethod, setPaymentMethod] = useState('invoice')
  const [autoRenew, setAutoRenew] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setFranchiseId('')
      setPlatformPlanId('')
      setPaymentMethod('invoice')
      setAutoRenew(true)
      setError(null)
    }
  }, [open])

  if (!open) return null

  const activePlans = plans.filter((p) => p.status === 'active')

  const submit = async () => {
    setError(null)
    if (!franchiseId) return setError('Pick a franchise.')
    if (!platformPlanId) return setError('Pick a platform plan.')
    try {
      await assign.mutateAsync({ franchiseId, platformPlanId, paymentMethod, autoRenew })
      onClose()
    } catch (e) {
      setError(apiErrorMessage(e, 'Could not assign the plan.'))
    }
  }

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Building2}
      eyebrow="SaaS · Franchise subscriptions"
      title="Assign a plan"
      width="sm"
      error={error}
      onSubmit={() => void submit()}
      submitLabel="Assign plan"
      submittingLabel="Assigning…"
      submitIcon={Plus}
      submitting={assign.isPending}
    >
      <DrawerSection title="Franchise & plan">
        <Field label="Franchise *">
          <select
            value={franchiseId}
            onChange={(e) => setFranchiseId(e.target.value)}
            className={drawerInputCls}
            disabled={franchisesQ.isLoading}
          >
            <option value="">{franchisesQ.isLoading ? 'Loading…' : 'Select a franchise…'}</option>
            {franchises.map((f) => (
              <option key={f.id} value={f.id}>{f.legalName} ({f.code})</option>
            ))}
          </select>
        </Field>
        <Field label="Platform plan *" hint="Only active plans can be assigned.">
          <select value={platformPlanId} onChange={(e) => setPlatformPlanId(e.target.value)} className={drawerInputCls}>
            <option value="">Select a plan…</option>
            {activePlans.map((p) => (
              <option key={p.id} value={p.id}>{p.name} — {formatCurrency(p.price)}/{p.billingInterval}</option>
            ))}
          </select>
        </Field>
      </DrawerSection>

      <DrawerSection title="Billing">
        <Field label="Payment method">
          <select value={paymentMethod} onChange={(e) => setPaymentMethod(e.target.value)} className={drawerInputCls}>
            {PAYMENT_METHODS.map((m) => (
              <option key={m} value={m} className="capitalize">{m.replace(/_/g, ' ')}</option>
            ))}
          </select>
        </Field>
        <label className="flex items-center gap-2 text-sm text-gray-700">
          <input
            type="checkbox"
            checked={autoRenew}
            onChange={(e) => setAutoRenew(e.target.checked)}
            className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30"
          />
          Auto-renew at period end
        </label>
      </DrawerSection>
    </FormDrawer>
  )
}

// ── Franchise subscription detail (view + cancel) ───────────────────────────────

export function FranchiseSubscriptionDetailDrawer({
  subscription,
  franchiseLabel,
  planLabel,
  onClose,
  canManage,
}: {
  subscription: FranchiseSubscriptionDto | null
  franchiseLabel: string
  planLabel: string
  onClose: () => void
  canManage: boolean
}) {
  const { cancel } = useFranchiseSubscriptionActions()
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState(false)

  useEffect(() => {
    if (subscription) {
      setReason('')
      setError(null)
      setConfirming(false)
    }
  }, [subscription])

  if (!subscription) return null
  const s = subscription

  const handleCancel = async () => {
    setError(null)
    try {
      await cancel.mutateAsync({ id: s.id, reason: reason.trim() || null })
      onClose()
    } catch (e) {
      setError(apiErrorMessage(e, 'Could not cancel the subscription.'))
    }
  }

  const canCancel =
    canManage && !['cancelled', 'expired'].includes(s.status)

  return (
    <FormDrawer
      open={!!s}
      onClose={onClose}
      icon={Building2}
      eyebrow={<>Subscription · <span className="font-mono">{s.subscriptionNumber}</span></>}
      title={franchiseLabel}
      width="md"
      footer={null}
    >
      <DrawerSection>
        <FranchiseSubscriptionStatusBadge status={s.status} />
      </DrawerSection>

      <DetailSection title="Plan & billing">
        <DetailRow label="Plan" value={planLabel} />
        <DetailRow label="Price" value={`${formatCurrency(s.priceSnapshot)} / ${s.billingInterval}`} />
        <DetailRow label="Auto-renew" value={s.autoRenew ? 'Yes' : 'No'} />
        <DetailRow label="Cycles billed" value={s.totalCyclesBilled} />
        <DetailRow label="Dunning attempts" value={s.dunningAttempts} />
      </DetailSection>

      <DetailSection title="Period">
        <DetailRow label="Current start" value={s.currentPeriodStart ? formatDate(s.currentPeriodStart) : '—'} />
        <DetailRow label="Current end" value={s.currentPeriodEnd ? formatDate(s.currentPeriodEnd) : '—'} />
        <DetailRow label="Next billing" value={s.nextBillingAt ? formatDate(s.nextBillingAt) : '—'} />
        <DetailRow label="Suspended at" value={s.suspendedAt ? formatDate(s.suspendedAt) : '—'} />
      </DetailSection>

      {error && <p className="text-xs text-red-600" role="alert">{error}</p>}

      {canCancel && (
        <div className="space-y-2 pt-1">
          {!confirming ? (
            <button
              type="button"
              onClick={() => setConfirming(true)}
              className="inline-flex w-full items-center justify-center gap-1.5 rounded-lg border border-red-200 px-4 py-2 text-sm font-medium text-red-600 hover:bg-red-50"
            >
              <Ban className="h-3.5 w-3.5" /> Cancel subscription
            </button>
          ) : (
            <DrawerSection title="Cancel subscription">
              <Field label="Reason (optional)">
                <input value={reason} onChange={(e) => setReason(e.target.value)} className={drawerInputCls} placeholder="e.g. franchise closing" />
              </Field>
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={() => setConfirming(false)}
                  className="flex-1 rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50"
                >
                  Keep
                </button>
                <button
                  type="button"
                  onClick={() => void handleCancel()}
                  disabled={cancel.isPending}
                  className="inline-flex flex-1 items-center justify-center gap-1.5 rounded-lg bg-red-600 px-4 py-2 text-sm font-semibold text-white hover:bg-red-700 disabled:opacity-60"
                >
                  <Ban className="h-3.5 w-3.5" /> {cancel.isPending ? 'Cancelling…' : 'Confirm cancel'}
                </button>
              </div>
            </DrawerSection>
          )}
        </div>
      )}
    </FormDrawer>
  )
}

// ── Badges ──────────────────────────────────────────────────────────────────────

export function PlatformPlanStatusBadge({ status }: { status: string }) {
  const cls =
    status === 'active'
      ? 'bg-green-100 text-green-700'
      : status === 'retired'
        ? 'bg-gray-100 text-gray-500'
        : 'bg-blue-100 text-blue-700' // draft
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium capitalize ${cls}`}>
      {status}
    </span>
  )
}

export function FranchiseSubscriptionStatusBadge({ status }: { status: string }) {
  const cls =
    status === 'active'
      ? 'bg-green-100 text-green-700'
      : status === 'trialing'
        ? 'bg-blue-100 text-blue-700'
        : status === 'past_due'
          ? 'bg-red-100 text-red-700'
          : status === 'suspended'
            ? 'bg-red-100 text-red-700'
            : status === 'pending'
              ? 'bg-gray-100 text-gray-600'
              : 'bg-gray-100 text-gray-500' // cancelled / expired
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium capitalize ${cls}`}>
      {status.replace(/_/g, ' ')}
    </span>
  )
}

export const FRANCHISE_SUB_STATUSES = [
  'pending',
  'trialing',
  'active',
  'past_due',
  'suspended',
  'cancelled',
  'expired',
]
