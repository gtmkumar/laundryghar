import { useMemo, useState } from 'react'
import { Plus } from 'lucide-react'
import { useCoupons } from '@/hooks/useCommerce'
import { usePermissions } from '@/hooks/usePermissions'
import { CouponEditDrawer, CouponViewDrawer, ArchiveCouponDrawer, COUPON_TYPES } from './CouponDrawers'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable, type FilterDef } from '@/components/shared/FilterableTable'
import type { CouponDto } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

function CouponStatusBadge({ status }: { status: string }) {
  const variant =
    status === 'active'
      ? 'success'
      : status === 'expired' || status === 'disabled'
        ? 'destructive'
        : 'secondary'
  return (
    <Badge variant={variant} className="capitalize">
      {status.replace(/_/g, ' ')}
    </Badge>
  )
}

function discountText(c: CouponDto): string {
  return c.couponType === 'percent' ? `${c.discountValue}%` : formatCurrency(c.discountValue)
}

function usageText(c: CouponDto): string {
  return c.maxTotalUses != null ? `${c.currentUsageCount} / ${c.maxTotalUses}` : `${c.currentUsageCount} / ∞`
}

export function CouponsPage() {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('coupons.manage')
  const { data, isLoading, isError, error, refetch } = useCoupons()

  const coupons = useMemo(() => data?.list ?? [], [data])
  const total = data?.totalCount

  const [editing, setEditing] = useState<CouponDto | null>(null)
  const [creating, setCreating] = useState(false)
  const [viewing, setViewing] = useState<CouponDto | null>(null)
  const [archiving, setArchiving] = useState<CouponDto | null>(null)

  const columns: Column<CouponDto>[] = [
    { header: 'Code', accessor: 'code', className: 'font-mono text-xs font-semibold', sortKey: 'code' },
    { header: 'Name', accessor: 'name', sortKey: 'name' },
    {
      header: 'Type',
      accessor: (c) => <span className="capitalize">{c.couponType === 'percent' ? 'Percentage' : 'Flat'}</span>,
      sortKey: 'type',
      sortAccessor: (c) => c.couponType,
    },
    {
      header: 'Discount',
      accessor: (c) => <span className="tabular-nums">{discountText(c)}</span>,
      className: 'text-right',
      sortKey: 'discount',
      sortAccessor: (c) => c.discountValue,
    },
    {
      header: 'Min order',
      accessor: (c) => <span className="tabular-nums">{formatCurrency(c.minOrderValue)}</span>,
      className: 'text-right',
      sortKey: 'minOrder',
      sortAccessor: (c) => c.minOrderValue,
    },
    {
      header: 'Usage',
      accessor: (c) => <span className="tabular-nums">{usageText(c)}</span>,
      className: 'text-right',
      sortKey: 'usage',
      sortAccessor: (c) => c.currentUsageCount,
    },
    {
      header: 'Validity',
      accessor: (c) =>
        c.validUntil ? `${formatDate(c.validFrom)} – ${formatDate(c.validUntil)}` : `${formatDate(c.validFrom)} →`,
      sortKey: 'validFrom',
      sortAccessor: (c) => c.validFrom,
    },
    {
      header: 'Status',
      accessor: (c) => <CouponStatusBadge status={c.status} />,
      sortKey: 'status',
      sortAccessor: (c) => c.status,
    },
  ]

  const filters: FilterDef<CouponDto>[] = [
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (c) => c.status,
      options: [...new Set(coupons.map((c) => c.status))].sort().map((s) => ({ value: s, label: s.replace(/_/g, ' ') })),
    },
    {
      key: 'type',
      allLabel: 'All types',
      value: (c) => c.couponType,
      options: COUPON_TYPES.map((t) => ({ value: t.value, label: t.label })),
    },
  ]

  return (
    <div>
      <PageHeader
        title="Coupons"
        description="Promo codes redeemable at checkout and POS order-create."
        action={
          canManage ? (
            <button
              type="button"
              onClick={() => setCreating(true)}
              className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
            >
              <Plus className="h-4 w-4" /> New coupon
            </button>
          ) : undefined
        }
      />
      <Card className="overflow-hidden">
        {isLoading ? (
          <LoadingState message="Loading coupons..." />
        ) : isError ? (
          isForbiddenError(error) ? <ForbiddenState /> : <ErrorState error={error as Error} onRetry={() => void refetch()} />
        ) : (
          <FilterableTable
            columns={columns}
            data={coupons}
            keyFn={(c) => c.id}
            unit="coupon"
            totalCount={total}
            searchPlaceholder="Search code or name…"
            searchAccessor={(c) => `${c.code} ${c.name} ${c.description ?? ''}`}
            filters={filters}
            initialSort={{ key: 'validFrom', dir: 'desc' }}
            onRowClick={(c) => setViewing(c)}
            emptyMessage="No coupons yet."
            noMatchMessage="No coupons match your filters."
            csvExport={{
              filename: `coupons-${new Date().toISOString().slice(0, 10)}`,
              columns: [
                { header: 'Code', value: (c) => c.code },
                { header: 'Name', value: (c) => c.name },
                { header: 'Type', value: (c) => c.couponType },
                { header: 'Discount value', value: (c) => c.discountValue },
                { header: 'Max discount', value: (c) => c.maxDiscountAmount ?? '' },
                { header: 'Min order', value: (c) => c.minOrderValue },
                { header: 'Used', value: (c) => c.currentUsageCount },
                { header: 'Max total uses', value: (c) => c.maxTotalUses ?? '' },
                { header: 'Valid from', value: (c) => c.validFrom },
                { header: 'Valid until', value: (c) => c.validUntil ?? '' },
                { header: 'Status', value: (c) => c.status },
              ],
            }}
          />
        )}
      </Card>

      <CouponEditDrawer open={creating} onClose={() => setCreating(false)} />
      <CouponEditDrawer open={!!editing} coupon={editing} onClose={() => setEditing(null)} />
      <CouponViewDrawer
        coupon={viewing}
        onClose={() => setViewing(null)}
        canManage={canManage}
        onEdit={(c) => { setViewing(null); setEditing(c) }}
        onArchive={(c) => { setViewing(null); setArchiving(c) }}
      />
      <ArchiveCouponDrawer coupon={archiving} onClose={() => setArchiving(null)} />
    </div>
  )
}
