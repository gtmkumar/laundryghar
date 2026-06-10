import { useState } from 'react'
import { Loader2 } from 'lucide-react'
import {
  useAppBannersInfinite,
  useCreateAppBanner,
  useUpdateAppBanner,
  useDeleteAppBanner,
} from '@/hooks/useCms'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { usePermissions } from '@/hooks/usePermissions'
import { usePromotions, useCoupons } from '@/hooks/useCommerce'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { DataTable, type Column } from '@/components/shared/DataTable'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import type {
  AppBannerDto,
  CreateAppBannerRequest,
  UpdateAppBannerRequest,
} from '@/types/api'
import { formatDate } from '@/lib/utils'

const APP_TYPES = ['customer', 'rider', 'staff', 'pos']
const PLACEMENTS = [
  'home_top',
  'home_middle',
  'home_bottom',
  'services_top',
  'cart_top',
  'order_success',
  'profile',
]
const STATUSES = ['active', 'inactive', 'archived']

// ── Delete Confirm ────────────────────────────────────────────────────────────

interface DeleteConfirmProps {
  item: AppBannerDto
  onConfirm: () => void
  onCancel: () => void
  isPending: boolean
}

function DeleteConfirm({ item, onConfirm, onCancel, isPending }: DeleteConfirmProps) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="bg-white rounded-lg shadow-xl p-6 max-w-sm w-full mx-4">
        <h3 className="text-base font-semibold text-gray-900 mb-2">Archive banner?</h3>
        <p className="text-sm text-gray-500 mb-6">
          <span className="font-medium text-gray-700">{item.titleLocalized}</span> will be archived.
        </p>
        <div className="flex justify-end gap-2">
          <Button variant="outline" size="sm" onClick={onCancel} disabled={isPending}>
            Cancel
          </Button>
          <Button variant="destructive" size="sm" onClick={onConfirm} disabled={isPending}>
            {isPending ? 'Archiving…' : 'Archive'}
          </Button>
        </div>
      </div>
    </div>
  )
}

// ── Form Modal ────────────────────────────────────────────────────────────────

type FormFields = {
  appType: string
  placement: string
  title: string
  titleLocalized: string
  subtitle: string
  subtitleLocalized: string
  imageUrl: string
  imageDarkUrl: string
  ctaText: string
  ctaDeeplink: string
  externalUrl: string
  backgroundColor: string
  displayOrder: string
  isActive: boolean
  targetAudience: string
  minAppVersion: string
  status: string
  promotionId: string
  couponId: string
}

function defaultFields(b?: AppBannerDto | null): FormFields {
  return {
    appType: b?.appType ?? 'customer',
    placement: b?.placement ?? 'home_top',
    title: b?.title ?? '',
    titleLocalized: b?.titleLocalized ?? '',
    subtitle: b?.subtitle ?? '',
    subtitleLocalized: b?.subtitleLocalized ?? '',
    imageUrl: b?.imageUrl ?? '',
    imageDarkUrl: b?.imageDarkUrl ?? '',
    ctaText: b?.ctaText ?? '',
    ctaDeeplink: b?.ctaDeeplink ?? '',
    externalUrl: b?.externalUrl ?? '',
    backgroundColor: b?.backgroundColor ?? '',
    displayOrder: String(b?.displayOrder ?? 0),
    isActive: b?.isActive ?? true,
    targetAudience: b?.targetAudience ?? '',
    minAppVersion: b?.minAppVersion ?? '',
    status: b?.status ?? 'active',
    promotionId: b?.promotionId ?? '',
    couponId: b?.couponId ?? '',
  }
}

function FormField({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1">
      <Label className="text-xs text-gray-600">{label}</Label>
      {children}
    </div>
  )
}

interface FormModalProps {
  initial?: AppBannerDto | null
  onClose: () => void
}

function FormModal({ initial, onClose }: FormModalProps) {
  const isEdit = Boolean(initial)
  const [fields, setFields] = useState<FormFields>(() => defaultFields(initial))
  const [error, setError] = useState<string | null>(null)

  const createMutation = useCreateAppBanner()
  const updateMutation = useUpdateAppBanner(initial?.id ?? '')
  const isPending = createMutation.isPending || updateMutation.isPending

  const { data: promotionsData } = usePromotions()
  const { data: couponsData } = useCoupons()
  const promotions = promotionsData?.list ?? []
  const coupons = couponsData?.list ?? []

  function set(key: keyof FormFields, value: string | boolean) {
    setFields((f) => ({ ...f, [key]: value }))
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)

    const base = {
      appType: fields.appType,
      placement: fields.placement,
      title: fields.title || null,
      titleLocalized: fields.titleLocalized,
      subtitle: fields.subtitle || null,
      subtitleLocalized: fields.subtitleLocalized,
      imageUrl: fields.imageUrl,
      imageDarkUrl: fields.imageDarkUrl || null,
      ctaText: fields.ctaText || null,
      ctaDeeplink: fields.ctaDeeplink || null,
      externalUrl: fields.externalUrl || null,
      promotionId: fields.promotionId || null,
      couponId: fields.couponId || null,
      backgroundColor: fields.backgroundColor || null,
      displayOrder: parseInt(fields.displayOrder, 10) || 0,
      isActive: fields.isActive,
      targetAudience: fields.targetAudience || null,
      minAppVersion: fields.minAppVersion || null,
    }

    try {
      if (isEdit && initial) {
        const payload: UpdateAppBannerRequest = { ...base, status: fields.status }
        await updateMutation.mutateAsync(payload)
      } else {
        const payload: CreateAppBannerRequest = base
        await createMutation.mutateAsync(payload)
      }
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'An error occurred')
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 overflow-y-auto py-8">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-xl mx-4">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-base font-semibold text-gray-900">
            {isEdit ? 'Edit App Banner' : 'New App Banner'}
          </h2>
        </div>
        <form onSubmit={(e) => void handleSubmit(e)}>
          <div className="px-6 py-4 space-y-4 max-h-[65vh] overflow-y-auto">
            <div className="grid grid-cols-2 gap-4">
              <FormField label="App Type *">
                <select
                  className="flex h-9 w-full rounded-md border border-gray-300 bg-white px-3 py-1 text-sm"
                  value={fields.appType}
                  onChange={(e) => set('appType', e.target.value)}
                  required
                >
                  {APP_TYPES.map((t) => (
                    <option key={t} value={t}>
                      {t}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Placement *">
                <select
                  className="flex h-9 w-full rounded-md border border-gray-300 bg-white px-3 py-1 text-sm"
                  value={fields.placement}
                  onChange={(e) => set('placement', e.target.value)}
                  required
                >
                  {PLACEMENTS.map((p) => (
                    <option key={p} value={p}>
                      {p}
                    </option>
                  ))}
                </select>
              </FormField>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <FormField label="Title">
                <Input
                  value={fields.title}
                  onChange={(e) => set('title', e.target.value)}
                />
              </FormField>
              <FormField label="Title (Localized) *">
                <Input
                  value={fields.titleLocalized}
                  onChange={(e) => set('titleLocalized', e.target.value)}
                  required
                />
              </FormField>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <FormField label="Subtitle">
                <Input
                  value={fields.subtitle}
                  onChange={(e) => set('subtitle', e.target.value)}
                />
              </FormField>
              <FormField label="Subtitle (Localized) *">
                <Input
                  value={fields.subtitleLocalized}
                  onChange={(e) => set('subtitleLocalized', e.target.value)}
                  required
                />
              </FormField>
            </div>

            <FormField label="Image URL *">
              <Input
                value={fields.imageUrl}
                onChange={(e) => set('imageUrl', e.target.value)}
                required
              />
            </FormField>

            <FormField label="Image URL (Dark)">
              <Input
                value={fields.imageDarkUrl}
                onChange={(e) => set('imageDarkUrl', e.target.value)}
              />
            </FormField>

            <div className="grid grid-cols-2 gap-4">
              <FormField label="CTA Text">
                <Input
                  value={fields.ctaText}
                  onChange={(e) => set('ctaText', e.target.value)}
                />
              </FormField>
              <FormField label="CTA Deeplink">
                <Input
                  value={fields.ctaDeeplink}
                  onChange={(e) => set('ctaDeeplink', e.target.value)}
                />
              </FormField>
            </div>

            <FormField label="External URL">
              <Input
                value={fields.externalUrl}
                onChange={(e) => set('externalUrl', e.target.value)}
              />
            </FormField>

            <div className="grid grid-cols-3 gap-4">
              <FormField label="Background Color">
                <Input
                  value={fields.backgroundColor}
                  onChange={(e) => set('backgroundColor', e.target.value)}
                  placeholder="#FFFFFF"
                />
              </FormField>
              <FormField label="Display Order">
                <Input
                  type="number"
                  value={fields.displayOrder}
                  onChange={(e) => set('displayOrder', e.target.value)}
                  min={0}
                />
              </FormField>
              <FormField label="Min App Version">
                <Input
                  value={fields.minAppVersion}
                  onChange={(e) => set('minAppVersion', e.target.value)}
                  placeholder="1.0.0"
                />
              </FormField>
            </div>

            {/* ── Offer linking (optional) ── */}
            <div className="grid grid-cols-2 gap-4">
              <FormField label="Link Promotion">
                <select
                  className="flex h-9 w-full rounded-md border border-gray-300 bg-white px-3 py-1 text-sm"
                  value={fields.promotionId}
                  onChange={(e) => set('promotionId', e.target.value)}
                >
                  <option value="">— None —</option>
                  {promotions.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name} ({p.code})
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Link Coupon">
                <select
                  className="flex h-9 w-full rounded-md border border-gray-300 bg-white px-3 py-1 text-sm"
                  value={fields.couponId}
                  onChange={(e) => set('couponId', e.target.value)}
                >
                  <option value="">— None —</option>
                  {coupons.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name} ({c.code})
                    </option>
                  ))}
                </select>
              </FormField>
            </div>

            <FormField label="Target Audience">
              <Input
                value={fields.targetAudience}
                onChange={(e) => set('targetAudience', e.target.value)}
                placeholder="all | premium | new_user"
              />
            </FormField>

            {isEdit && (
              <FormField label="Status">
                <select
                  className="flex h-9 w-full rounded-md border border-gray-300 bg-white px-3 py-1 text-sm"
                  value={fields.status}
                  onChange={(e) => set('status', e.target.value)}
                >
                  {STATUSES.map((s) => (
                    <option key={s} value={s}>
                      {s}
                    </option>
                  ))}
                </select>
              </FormField>
            )}

            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={fields.isActive}
                onChange={(e) => set('isActive', e.target.checked)}
                className="rounded"
              />
              Active
            </label>

            {error && (
              <p className="text-sm text-red-600 rounded bg-red-50 px-3 py-2">{error}</p>
            )}
          </div>

          <div className="px-6 py-4 border-t border-gray-200 flex justify-end gap-2">
            <Button type="button" variant="outline" size="sm" onClick={onClose} disabled={isPending}>
              Cancel
            </Button>
            <Button type="submit" size="sm" disabled={isPending}>
              {isPending ? 'Saving…' : isEdit ? 'Save changes' : 'Create'}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ── Tab ───────────────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const variant =
    status === 'active' ? 'success' : status === 'archived' ? 'destructive' : 'secondary'
  return (
    <Badge variant={variant} className="capitalize">
      {status}
    </Badge>
  )
}

export function AppBannersTab() {
  // Backend gate: every banner mutation requires permission:cms.banner.manage.
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('cms.banner.manage')
  const [showForm, setShowForm] = useState(false)
  const [editTarget, setEditTarget] = useState<AppBannerDto | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<AppBannerDto | null>(null)

  const {
    data,
    isLoading,
    isError,
    error,
    refetch,
    hasNextPage,
    isFetchingNextPage,
    fetchNextPage,
  } = useAppBannersInfinite()
  const deleteMutation = useDeleteAppBanner()

  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  // Preload offer lists so the table can resolve names
  const { data: promotionsData } = usePromotions()
  const { data: couponsData } = useCoupons()
  const promoMap = Object.fromEntries(
    (promotionsData?.list ?? []).map((p) => [p.id, `${p.name} (${p.code})`]),
  )
  const couponMap = Object.fromEntries(
    (couponsData?.list ?? []).map((c) => [c.id, `${c.name} (${c.code})`]),
  )

  function handleEdit(row: AppBannerDto) {
    setEditTarget(row)
    setShowForm(true)
  }

  function handleCloseForm() {
    setShowForm(false)
    setEditTarget(null)
  }

  async function handleDelete() {
    if (!deleteTarget) return
    await deleteMutation.mutateAsync(deleteTarget.id)
    setDeleteTarget(null)
  }

  const columns: Column<AppBannerDto>[] = [
    {
      header: 'App Type',
      accessor: (r) => (
        <Badge variant="secondary" className="capitalize">
          {r.appType}
        </Badge>
      ),
    },
    {
      header: 'Placement',
      accessor: (r) => <span className="text-xs font-mono">{r.placement}</span>,
    },
    { header: 'Title', accessor: (r) => r.titleLocalized },
    { header: 'Order', accessor: (r) => String(r.displayOrder), className: 'w-16 tabular-nums' },
    {
      header: 'Impressions',
      accessor: (r) => (
        <span className="tabular-nums text-gray-600">{r.impressionsCount.toLocaleString()}</span>
      ),
    },
    {
      header: 'Clicks',
      accessor: (r) => (
        <span className="tabular-nums text-gray-600">{r.clicksCount.toLocaleString()}</span>
      ),
    },
    {
      header: 'Linked Offer',
      accessor: (r) => {
        const promo = r.promotionId ? promoMap[r.promotionId] : null
        const coupon = r.couponId ? couponMap[r.couponId] : null
        if (!promo && !coupon) return <span className="text-gray-300">—</span>
        return (
          <div className="flex flex-col gap-0.5">
            {promo && (
              <span className="text-xs bg-blue-50 text-blue-700 rounded px-1.5 py-0.5 truncate max-w-[140px]" title={promo}>
                P: {promo}
              </span>
            )}
            {coupon && (
              <span className="text-xs bg-purple-50 text-purple-700 rounded px-1.5 py-0.5 truncate max-w-[140px]" title={coupon}>
                C: {coupon}
              </span>
            )}
          </div>
        )
      },
      className: 'w-48',
    },
    { header: 'Status', accessor: (r) => <StatusBadge status={r.status} /> },
    { header: 'Updated', accessor: (r) => formatDate(r.updatedAt) },
    ...(canManage
      ? [{
          header: '',
          accessor: (r: AppBannerDto) => (
            <div className="flex gap-1 justify-end">
              <Button
                variant="ghost"
                size="sm"
                onClick={(e) => {
                  e.stopPropagation()
                  handleEdit(r)
                }}
              >
                Edit
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onClick={(e) => {
                  e.stopPropagation()
                  setDeleteTarget(r)
                }}
                className="text-red-600 hover:text-red-700 hover:bg-red-50"
              >
                Archive
              </Button>
            </div>
          ),
          className: 'w-36',
        } satisfies Column<AppBannerDto>]
      : []),
  ]

  if (isLoading) return <LoadingState message="Loading app banners..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  const items = data?.pages.flatMap((p) => p.list) ?? []
  const total = data?.pages[0]?.totalCount

  return (
    <div>
      <div className="flex items-center justify-between px-4 pt-3 pb-2">
        {total !== undefined && (
          <p className="text-sm text-gray-500">{total} banner{total === 1 ? '' : 's'}</p>
        )}
        {canManage && (
          <Button size="sm" onClick={() => setShowForm(true)} className="ml-auto">
            + New Banner
          </Button>
        )}
      </div>

      <DataTable
        columns={columns}
        data={items}
        keyFn={(r) => r.id}
        emptyMessage="No app banners found."
      />
      <div ref={sentinelRef} className="h-1" />
      {isFetchingNextPage && (
        <div className="flex items-center justify-center py-4 text-gray-400">
          <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more…
        </div>
      )}

      {showForm && <FormModal initial={editTarget} onClose={handleCloseForm} />}

      {deleteTarget && (
        <DeleteConfirm
          item={deleteTarget}
          onConfirm={() => void handleDelete()}
          onCancel={() => setDeleteTarget(null)}
          isPending={deleteMutation.isPending}
        />
      )}
    </div>
  )
}
