import { useMemo, useState } from 'react'
import { Megaphone, Plus, Save, Archive } from 'lucide-react'
import { useCreatePromotion, useUpdatePromotion, useDeletePromotion } from '@/hooks/useCommerce'
import {
  FormDrawer,
  DrawerSection,
  DetailSection,
  DetailRow,
  Field,
  drawerInputCls,
} from '@/components/shared/FormDrawer'
import type { PromotionDto, CreatePromotionPayload, UpdatePromotionPayload } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

// Small co-located option constant for this drawer's selects; not worth its own module.
// eslint-disable-next-line react-refresh/only-export-components
export const PROMOTION_TYPES = [
  { value: 'discount', label: 'Discount' },
  { value: 'cashback', label: 'Cashback' },
  { value: 'banner', label: 'Banner / awareness' },
] as const

// Small co-located option constant for this drawer's selects; not worth its own module.
// eslint-disable-next-line react-refresh/only-export-components
export const TARGET_AUDIENCES = [
  { value: 'all', label: 'All customers' },
  { value: 'new_customers', label: 'New customers' },
  { value: 'segment', label: 'Specific segments' },
] as const

// Small co-located option constant for this drawer's selects; not worth its own module.
// eslint-disable-next-line react-refresh/only-export-components
export const REWARD_DISCOUNT_TYPES = [
  { value: 'percent', label: 'Percentage (%)' },
  { value: 'flat', label: 'Flat amount (₹)' },
] as const

/** The structured shape the order pipeline reads out of `rewardConfig` (a JSON string). */
interface RewardConfig {
  discount_type: 'percent' | 'flat'
  discount_value: number
  max_discount?: number
}

/** Parse a stored `rewardConfig` JSON string into the 3 structured form fields. */
function parseRewardConfig(json: string | null | undefined): {
  discountType: string
  discountValue: string
  maxDiscount: string
} {
  try {
    const cfg = JSON.parse(json || '{}') as Partial<RewardConfig>
    return {
      discountType: cfg.discount_type === 'flat' ? 'flat' : 'percent',
      discountValue: cfg.discount_value != null ? String(cfg.discount_value) : '',
      maxDiscount: cfg.max_discount != null ? String(cfg.max_discount) : '',
    }
  } catch {
    return { discountType: 'percent', discountValue: '', maxDiscount: '' }
  }
}

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
  promotionType: string
  targetAudience: string
  eligibleSegments: string
  // rewardConfig — structured
  discountType: string
  discountValue: string
  maxDiscount: string
  // rules — advanced raw JSON
  rules: string
  couponId: string
  bannerImageUrl: string
  deeplinkUrl: string
  validFrom: string
  validUntil: string
  totalBudget: string
}

function blankForm(): FormState {
  const today = new Date().toISOString().slice(0, 10)
  return {
    code: '',
    name: '',
    description: '',
    promotionType: 'discount',
    targetAudience: 'all',
    eligibleSegments: '',
    discountType: 'percent',
    discountValue: '',
    maxDiscount: '',
    rules: '{}',
    couponId: '',
    bannerImageUrl: '',
    deeplinkUrl: '',
    validFrom: today,
    validUntil: '',
    totalBudget: '',
  }
}

function fromPromotion(p: PromotionDto): FormState {
  const reward = parseRewardConfig(p.rewardConfig)
  return {
    code: p.code,
    name: p.name,
    description: p.description ?? '',
    promotionType: p.promotionType,
    targetAudience: p.targetAudience,
    eligibleSegments: (p.eligibleSegments ?? []).join(', '),
    discountType: reward.discountType,
    discountValue: reward.discountValue,
    maxDiscount: reward.maxDiscount,
    rules: p.rules?.trim() ? p.rules : '{}',
    couponId: p.couponId ?? '',
    bannerImageUrl: p.bannerImageUrl ?? '',
    deeplinkUrl: p.deeplinkUrl ?? '',
    validFrom: dateOnly(p.validFrom),
    validUntil: dateOnly(p.validUntil),
    totalBudget: p.totalBudget != null ? String(p.totalBudget) : '',
  }
}

// ── Create / Edit ─────────────────────────────────────────────────────────────

interface EditDrawerProps {
  open: boolean
  /** When set, the drawer is in edit mode for this promotion; otherwise create mode. */
  promotion?: PromotionDto | null
  onClose: () => void
}

export function PromotionEditDrawer({ open, promotion, onClose }: EditDrawerProps) {
  const isEdit = !!promotion
  const create = useCreatePromotion()
  const update = useUpdatePromotion()

  const [form, setForm] = useState<FormState>(blankForm())
  const [error, setError] = useState<string | null>(null)

  // Re-seed the form whenever the {open, promotion} pair changes (adjust-state-while-rendering).
  const seedSig = `${open}:${promotion?.id ?? ''}`
  const [seededSig, setSeededSig] = useState(seedSig)
  if (seedSig !== seededSig) {
    setSeededSig(seedSig)
    if (open) {
      setForm(promotion ? fromPromotion(promotion) : blankForm())
      setError(null)
    }
  }

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  if (!open) return null

  const submit = async () => {
    setError(null)
    const discountValue = Number(form.discountValue)
    const maxDiscount = form.maxDiscount.trim() === '' ? null : Number(form.maxDiscount)
    const totalBudget = form.totalBudget.trim() === '' ? null : Number(form.totalBudget)

    if (!isEdit && !form.code.trim()) return setError('Promotion code is required.')
    if (!form.name.trim()) return setError('Promotion name is required.')
    if (!(discountValue > 0)) return setError('Reward value must be greater than 0.')
    if (form.discountType === 'percent' && discountValue > 100)
      return setError('A percentage reward cannot exceed 100%.')
    if (maxDiscount !== null && (!Number.isFinite(maxDiscount) || maxDiscount < 0))
      return setError('Max discount cap must be 0 or greater.')
    if (totalBudget !== null && (!Number.isFinite(totalBudget) || totalBudget < 0))
      return setError('Total budget must be 0 or greater.')
    if (!form.validFrom) return setError('A start date is required.')
    if (form.validUntil && form.validUntil < form.validFrom)
      return setError('The end date must be on or after the start date.')

    // `rules` is a free advanced JSON string — validate it parses before sending.
    let rules = form.rules.trim() || '{}'
    try {
      JSON.parse(rules)
    } catch {
      return setError('Advanced rules must be valid JSON (default is {}).')
    }
    rules = JSON.stringify(JSON.parse(rules))

    // Serialize the 3 structured fields back into the rewardConfig JSON string.
    const reward: RewardConfig = {
      discount_type: form.discountType === 'flat' ? 'flat' : 'percent',
      discount_value: discountValue,
    }
    if (maxDiscount !== null) reward.max_discount = maxDiscount
    const rewardConfig = JSON.stringify(reward)

    const eligibleSegments =
      form.targetAudience === 'segment'
        ? form.eligibleSegments
            .split(',')
            .map((s) => s.trim())
            .filter(Boolean)
        : null
    if (form.targetAudience === 'segment' && (!eligibleSegments || eligibleSegments.length === 0))
      return setError('Add at least one eligible segment, or change the target audience.')

    const payload: CreatePromotionPayload | UpdatePromotionPayload = {
      code: form.code.trim().toUpperCase(),
      name: form.name.trim(),
      description: form.description.trim() || null,
      promotionType: form.promotionType,
      targetAudience: form.targetAudience,
      eligibleSegments,
      rules,
      rewardConfig,
      couponId: form.couponId.trim() || null,
      bannerImageUrl: form.bannerImageUrl.trim() || null,
      deeplinkUrl: form.deeplinkUrl.trim() || null,
      validFrom: toInstant(form.validFrom),
      validUntil: form.validUntil ? toInstant(form.validUntil) : null,
      totalBudget,
    }

    try {
      if (isEdit && promotion) {
        await update.mutateAsync({ id: promotion.id, payload })
      } else {
        await create.mutateAsync(payload)
      }
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save the promotion.')
    }
  }

  const submitting = create.isPending || update.isPending

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Megaphone}
      eyebrow="Commerce · Promotions"
      title={isEdit ? `Edit ${promotion!.code}` : 'New promotion'}
      width="md"
      error={error}
      onSubmit={() => void submit()}
      submitLabel={isEdit ? 'Save promotion' : 'Create promotion'}
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
              placeholder="DIWALI25"
            />
          </Field>
          <Field label="Name *">
            <input
              value={form.name}
              onChange={(e) => set('name', e.target.value)}
              className={drawerInputCls}
              placeholder="Diwali festive offer"
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
        <div className="grid grid-cols-2 gap-3">
          <Field label="Type *">
            <select
              value={form.promotionType}
              onChange={(e) => set('promotionType', e.target.value)}
              className={drawerInputCls}
            >
              {PROMOTION_TYPES.map((t) => (
                <option key={t.value} value={t.value}>{t.label}</option>
              ))}
            </select>
          </Field>
          <Field label="Linked coupon ID" hint="Optional — ties this promo to a coupon.">
            <input
              value={form.couponId}
              onChange={(e) => set('couponId', e.target.value)}
              className={`${drawerInputCls} font-mono`}
              placeholder="Optional"
            />
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="Audience">
        <Field label="Target audience *">
          <select
            value={form.targetAudience}
            onChange={(e) => set('targetAudience', e.target.value)}
            className={drawerInputCls}
          >
            {TARGET_AUDIENCES.map((a) => (
              <option key={a.value} value={a.value}>{a.label}</option>
            ))}
          </select>
        </Field>
        {form.targetAudience === 'segment' && (
          <Field label="Eligible segments *" hint="Comma-separated segment keys, e.g. vip, churned.">
            <input
              value={form.eligibleSegments}
              onChange={(e) => set('eligibleSegments', e.target.value)}
              className={drawerInputCls}
              placeholder="vip, churned"
            />
          </Field>
        )}
      </DrawerSection>

      <DrawerSection title="Reward">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Reward type *">
            <select
              value={form.discountType}
              onChange={(e) => set('discountType', e.target.value)}
              className={drawerInputCls}
            >
              {REWARD_DISCOUNT_TYPES.map((t) => (
                <option key={t.value} value={t.value}>{t.label}</option>
              ))}
            </select>
          </Field>
          <Field label={form.discountType === 'percent' ? 'Reward (%) *' : 'Reward (₹) *'}>
            <input
              type="number"
              min="0.01"
              step="0.01"
              value={form.discountValue}
              onChange={(e) => set('discountValue', e.target.value)}
              className={drawerInputCls}
              placeholder={form.discountType === 'percent' ? '25' : '100'}
            />
          </Field>
        </div>
        <Field label="Max discount cap (₹)" hint="Caps a percentage reward. Leave blank for none.">
          <input
            type="number"
            min="0"
            step="0.01"
            value={form.maxDiscount}
            onChange={(e) => set('maxDiscount', e.target.value)}
            className={drawerInputCls}
            placeholder="No cap"
          />
        </Field>
      </DrawerSection>

      <DrawerSection title="Budget & validity">
        <Field label="Total budget (₹)" hint="Optional spend cap across all redemptions.">
          <input
            type="number"
            min="0"
            step="0.01"
            value={form.totalBudget}
            onChange={(e) => set('totalBudget', e.target.value)}
            className={drawerInputCls}
            placeholder="No cap"
          />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Valid from *">
            <input type="date" value={form.validFrom} onChange={(e) => set('validFrom', e.target.value)} className={drawerInputCls} />
          </Field>
          <Field label="Valid until" hint="Leave blank for no expiry.">
            <input type="date" value={form.validUntil} onChange={(e) => set('validUntil', e.target.value)} className={drawerInputCls} />
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="Presentation">
        <Field label="Banner image URL">
          <input
            value={form.bannerImageUrl}
            onChange={(e) => set('bannerImageUrl', e.target.value)}
            className={drawerInputCls}
            placeholder="https://…"
          />
        </Field>
        <Field label="Deeplink URL">
          <input
            value={form.deeplinkUrl}
            onChange={(e) => set('deeplinkUrl', e.target.value)}
            className={drawerInputCls}
            placeholder="laundryghar://…"
          />
        </Field>
      </DrawerSection>

      <DrawerSection title="Advanced">
        <Field
          label="Rules (JSON)"
          hint="Free-form rules read by the order pipeline. Defaults to an empty object."
        >
          <textarea
            value={form.rules}
            onChange={(e) => set('rules', e.target.value)}
            rows={4}
            className={`${drawerInputCls} font-mono text-xs`}
            placeholder="{}"
          />
        </Field>
      </DrawerSection>
    </FormDrawer>
  )
}

// ── View (read-only detail) ─────────────────────────────────────────────────────

interface ViewDrawerProps {
  promotion: PromotionDto | null
  onClose: () => void
  onEdit?: (p: PromotionDto) => void
  onArchive?: (p: PromotionDto) => void
  canManage: boolean
}

export function PromotionViewDrawer({ promotion, onClose, onEdit, onArchive, canManage }: ViewDrawerProps) {
  const reward = useMemo(() => (promotion ? parseRewardConfig(promotion.rewardConfig) : null), [promotion])

  if (!promotion || !reward) return null

  const rewardLabel =
    reward.discountType === 'percent'
      ? `${reward.discountValue}%`
      : formatCurrency(Number(reward.discountValue) || 0)

  const budgetPct =
    promotion.totalBudget && promotion.totalBudget > 0
      ? Math.min(100, Math.round((promotion.spentBudget / promotion.totalBudget) * 100))
      : null

  return (
    <FormDrawer
      open={!!promotion}
      onClose={onClose}
      icon={Megaphone}
      eyebrow={<>Promotion · <span className="font-mono">{promotion.code}</span></>}
      title={promotion.name}
      width="md"
      footer={
        canManage ? (
          <div className="flex justify-between gap-2">
            <button
              type="button"
              onClick={() => onArchive?.(promotion)}
              className="inline-flex items-center gap-1.5 rounded-lg border border-red-200 px-4 py-2 text-sm font-medium text-red-600 hover:bg-red-50"
            >
              <Archive className="h-3.5 w-3.5" /> Archive
            </button>
            <button
              type="button"
              onClick={() => onEdit?.(promotion)}
              className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
            >
              <Save className="h-3.5 w-3.5" /> Edit
            </button>
          </div>
        ) : undefined
      }
    >
      <DrawerSection title="Performance">
        <div className="grid grid-cols-2 gap-3">
          <div className="rounded-xl bg-gray-50 p-4">
            <p className="text-xs text-gray-400">Redemptions</p>
            <p className="text-2xl font-bold tabular-nums text-gray-900">{promotion.redemptionsCount}</p>
          </div>
          <div className="rounded-xl bg-gray-50 p-4">
            <p className="text-xs text-gray-400">Spent</p>
            <p className="text-2xl font-bold tabular-nums text-gray-900">
              {formatCurrency(promotion.spentBudget)}
              {promotion.totalBudget != null && (
                <span className="text-base font-medium text-gray-400"> / {formatCurrency(promotion.totalBudget)}</span>
              )}
            </p>
            {budgetPct != null && (
              <div className="mt-2 h-2 w-full overflow-hidden rounded-full bg-gray-200">
                <div className="h-full rounded-full bg-lg-green" style={{ width: `${budgetPct}%` }} />
              </div>
            )}
          </div>
        </div>
      </DrawerSection>

      <DetailSection title="Reward">
        <DetailRow label="Type" value={<span className="capitalize">{reward.discountType === 'percent' ? 'Percentage' : 'Flat amount'}</span>} />
        <DetailRow label="Value" value={<span className="font-semibold">{rewardLabel}</span>} />
        <DetailRow label="Max discount cap" value={reward.maxDiscount ? formatCurrency(Number(reward.maxDiscount)) : '—'} />
        <DetailRow label="Linked coupon" value={promotion.couponId ? <span className="font-mono text-xs">{promotion.couponId}</span> : '—'} />
      </DetailSection>

      <DetailSection title="Audience">
        <DetailRow label="Type" value={<span className="capitalize">{promotion.promotionType}</span>} />
        <DetailRow label="Target audience" value={<span className="capitalize">{promotion.targetAudience.replace(/_/g, ' ')}</span>} />
        <DetailRow
          label="Eligible segments"
          value={promotion.eligibleSegments && promotion.eligibleSegments.length > 0 ? promotion.eligibleSegments.join(', ') : '—'}
        />
      </DetailSection>

      <DetailSection title="Validity">
        <DetailRow label="Status" value={<span className="capitalize">{promotion.status.replace(/_/g, ' ')}</span>} />
        <DetailRow label="Valid from" value={formatDate(promotion.validFrom)} />
        <DetailRow label="Valid until" value={promotion.validUntil ? formatDate(promotion.validUntil) : 'No expiry'} />
      </DetailSection>
    </FormDrawer>
  )
}

// ── Archive confirm ──────────────────────────────────────────────────────────

export function ArchivePromotionDrawer({ promotion, onClose }: { promotion: PromotionDto | null; onClose: () => void }) {
  const del = useDeletePromotion()
  const [error, setError] = useState<string | null>(null)

  // Clear the error whenever a new promotion is opened (adjust-state-while-rendering).
  const [seededId, setSeededId] = useState<string | null>(null)
  if (promotion && seededId !== promotion.id) {
    setSeededId(promotion.id)
    setError(null)
  }

  if (!promotion) return null

  const submit = async () => {
    setError(null)
    try {
      await del.mutateAsync(promotion.id)
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not archive the promotion.')
    }
  }

  return (
    <FormDrawer
      open={!!promotion}
      onClose={onClose}
      icon={Archive}
      eyebrow={<>Archive promotion · <span className="font-mono">{promotion.code}</span></>}
      title={promotion.name}
      width="sm"
      error={error}
      onSubmit={() => void submit()}
      submitLabel="Archive promotion"
      submittingLabel="Archiving…"
      submitIcon={Archive}
      submitting={del.isPending}
    >
      <p className="text-sm text-gray-600">
        Archiving removes this promotion from the active list and stops new redemptions. Existing redemptions are unaffected.
      </p>
    </FormDrawer>
  )
}
