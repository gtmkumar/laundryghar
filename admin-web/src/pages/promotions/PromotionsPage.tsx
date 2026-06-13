import { useMemo, useState } from 'react'
import { Plus } from 'lucide-react'
import { usePromotions } from '@/hooks/useCommerce'
import { usePermissions } from '@/hooks/usePermissions'
import {
  PromotionEditDrawer,
  PromotionViewDrawer,
  ArchivePromotionDrawer,
  PROMOTION_TYPES,
  TARGET_AUDIENCES,
} from './PromotionDrawers'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable, type FilterDef } from '@/components/shared/FilterableTable'
import type { PromotionDto } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

function PromotionStatusBadge({ status }: { status: string }) {
  const variant =
    status === 'active'
      ? 'success'
      : status === 'expired' || status === 'disabled' || status === 'archived'
        ? 'destructive'
        : 'secondary'
  return (
    <Badge variant={variant} className="capitalize">
      {status.replace(/_/g, ' ')}
    </Badge>
  )
}

/** Parse the reward value out of the stored rewardConfig JSON for a compact display. */
function rewardText(p: PromotionDto): string {
  try {
    const cfg = JSON.parse(p.rewardConfig || '{}') as { discount_type?: string; discount_value?: number }
    if (cfg.discount_value == null) return '—'
    return cfg.discount_type === 'flat' ? formatCurrency(cfg.discount_value) : `${cfg.discount_value}%`
  } catch {
    return '—'
  }
}

function budgetText(p: PromotionDto): string {
  if (p.totalBudget == null) return `${formatCurrency(p.spentBudget)} / ∞`
  return `${formatCurrency(p.spentBudget)} / ${formatCurrency(p.totalBudget)}`
}

const AUDIENCE_LABELS: Record<string, string> = Object.fromEntries(
  TARGET_AUDIENCES.map((a) => [a.value, a.label]),
)

export function PromotionsPage() {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('promotions.manage')
  const { data, isLoading, isError, error, refetch } = usePromotions()

  const promotions = useMemo(() => data?.list ?? [], [data])
  const total = data?.totalCount

  const [editing, setEditing] = useState<PromotionDto | null>(null)
  const [creating, setCreating] = useState(false)
  const [viewing, setViewing] = useState<PromotionDto | null>(null)
  const [archiving, setArchiving] = useState<PromotionDto | null>(null)

  const columns: Column<PromotionDto>[] = [
    { header: 'Code', accessor: 'code', className: 'font-mono text-xs font-semibold', sortKey: 'code' },
    { header: 'Name', accessor: 'name', sortKey: 'name' },
    {
      header: 'Type',
      accessor: (p) => <span className="capitalize">{p.promotionType.replace(/_/g, ' ')}</span>,
      sortKey: 'type',
      sortAccessor: (p) => p.promotionType,
    },
    {
      header: 'Audience',
      accessor: (p) => <span>{AUDIENCE_LABELS[p.targetAudience] ?? p.targetAudience.replace(/_/g, ' ')}</span>,
      sortKey: 'audience',
      sortAccessor: (p) => p.targetAudience,
    },
    {
      header: 'Reward',
      accessor: (p) => <span className="tabular-nums">{rewardText(p)}</span>,
      className: 'text-right',
    },
    {
      header: 'Budget / spent',
      accessor: (p) => <span className="tabular-nums">{budgetText(p)}</span>,
      className: 'text-right',
      sortKey: 'spent',
      sortAccessor: (p) => p.spentBudget,
    },
    {
      header: 'Redemptions',
      accessor: (p) => <span className="tabular-nums">{p.redemptionsCount}</span>,
      className: 'text-right',
      sortKey: 'redemptions',
      sortAccessor: (p) => p.redemptionsCount,
    },
    {
      header: 'Validity',
      accessor: (p) =>
        p.validUntil ? `${formatDate(p.validFrom)} – ${formatDate(p.validUntil)}` : `${formatDate(p.validFrom)} →`,
      sortKey: 'validFrom',
      sortAccessor: (p) => p.validFrom,
    },
    {
      header: 'Status',
      accessor: (p) => <PromotionStatusBadge status={p.status} />,
      sortKey: 'status',
      sortAccessor: (p) => p.status,
    },
  ]

  const filters: FilterDef<PromotionDto>[] = [
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (p) => p.status,
      options: [...new Set(promotions.map((p) => p.status))]
        .sort()
        .map((s) => ({ value: s, label: s.replace(/_/g, ' ') })),
    },
    {
      key: 'type',
      allLabel: 'All types',
      value: (p) => p.promotionType,
      options: PROMOTION_TYPES.map((t) => ({ value: t.value, label: t.label })),
    },
    {
      key: 'audience',
      allLabel: 'All audiences',
      value: (p) => p.targetAudience,
      options: TARGET_AUDIENCES.map((a) => ({ value: a.value, label: a.label })),
    },
  ]

  return (
    <div>
      <PageHeader
        title="Promotions"
        description="Marketing campaigns and rewards applied in the order pipeline."
        action={
          canManage ? (
            <button
              type="button"
              onClick={() => setCreating(true)}
              className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
            >
              <Plus className="h-4 w-4" /> New promotion
            </button>
          ) : undefined
        }
      />
      <Card className="overflow-hidden">
        {isLoading ? (
          <LoadingState message="Loading promotions..." />
        ) : isError ? (
          isForbiddenError(error) ? (
            <ForbiddenState />
          ) : (
            <ErrorState error={error as Error} onRetry={() => void refetch()} />
          )
        ) : (
          <FilterableTable
            columns={columns}
            data={promotions}
            keyFn={(p) => p.id}
            unit="promotion"
            totalCount={total}
            searchPlaceholder="Search code or name…"
            searchAccessor={(p) => `${p.code} ${p.name} ${p.description ?? ''}`}
            filters={filters}
            initialSort={{ key: 'validFrom', dir: 'desc' }}
            onRowClick={(p) => setViewing(p)}
            emptyMessage="No promotions yet."
            noMatchMessage="No promotions match your filters."
            csvExport={{
              filename: `promotions-${new Date().toISOString().slice(0, 10)}`,
              columns: [
                { header: 'Code', value: (p) => p.code },
                { header: 'Name', value: (p) => p.name },
                { header: 'Type', value: (p) => p.promotionType },
                { header: 'Audience', value: (p) => p.targetAudience },
                { header: 'Reward', value: (p) => rewardText(p) },
                { header: 'Total budget', value: (p) => p.totalBudget ?? '' },
                { header: 'Spent', value: (p) => p.spentBudget },
                { header: 'Redemptions', value: (p) => p.redemptionsCount },
                { header: 'Valid from', value: (p) => p.validFrom },
                { header: 'Valid until', value: (p) => p.validUntil ?? '' },
                { header: 'Status', value: (p) => p.status },
              ],
            }}
          />
        )}
      </Card>

      <PromotionEditDrawer open={creating} onClose={() => setCreating(false)} />
      <PromotionEditDrawer open={!!editing} promotion={editing} onClose={() => setEditing(null)} />
      <PromotionViewDrawer
        promotion={viewing}
        onClose={() => setViewing(null)}
        canManage={canManage}
        onEdit={(p) => {
          setViewing(null)
          setEditing(p)
        }}
        onArchive={(p) => {
          setViewing(null)
          setArchiving(p)
        }}
      />
      <ArchivePromotionDrawer promotion={archiving} onClose={() => setArchiving(null)} />
    </div>
  )
}
