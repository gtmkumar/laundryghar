import { useState } from 'react'
import { AlertTriangle, Clock, RefreshCw } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { DataTable, type Column } from '@/components/shared/DataTable'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { useOpsQueues } from '@/hooks/useOrders'
import { formatDateTime } from '@/lib/utils'
import { cn } from '@/lib/utils'
import type { OpsOrderDto, OpsQueueBucket } from '@/types/api'
import { OrderDetailDrawer } from './OrderDetailDrawer'
import { statusLabel, statusBadgeVariant } from './orderStatus'

// ── Age coloring helpers ──────────────────────────────────────────────────────

/** Returns a CSS class based on how overdue the order is. */
function overdueColor(hours: number | null): string {
  if (hours === null) return ''
  if (hours < 4) return 'text-amber-600'
  return 'text-red-600 font-semibold'
}

function OverdueBadge({ hours }: { hours: number | null }) {
  if (hours === null) return null
  const h = Math.round(hours)
  return (
    <span className={cn('tabular-nums text-xs', overdueColor(hours))}>
      {h < 1 ? '<1h' : `${h}h`} late
    </span>
  )
}

function StuckBadge({ hours }: { hours: number | null }) {
  if (hours === null) return null
  const h = Math.round(hours)
  return (
    <span className="tabular-nums text-xs text-orange-600">
      {h}h idle
    </span>
  )
}

// ── Table columns ─────────────────────────────────────────────────────────────

function buildColumns(
  kind: 'dueToday' | 'overdue' | 'stuck',
): Column<OpsOrderDto>[] {
  return [
    {
      header: 'Order #',
      accessor: (r) => (
        <span className="font-mono text-xs font-medium text-blue-700">{r.orderNumber}</span>
      ),
      className: 'w-36',
    },
    {
      header: 'Customer',
      accessor: (r) => <span className="text-sm">{r.customerName || '—'}</span>,
    },
    {
      header: 'Status',
      accessor: (r) => (
        <Badge variant={statusBadgeVariant(r.status)} className="capitalize">
          {statusLabel(r.status)}
        </Badge>
      ),
    },
    {
      header: 'Promised',
      accessor: (r) =>
        r.promisedDeliveryAt ? (
          <span className="whitespace-nowrap text-xs text-gray-600">
            {formatDateTime(r.promisedDeliveryAt)}
          </span>
        ) : (
          <span className="text-xs text-gray-300">—</span>
        ),
      className: 'whitespace-nowrap',
    },
    ...(kind === 'overdue'
      ? [
          {
            header: 'Overdue',
            accessor: (r: OpsOrderDto) => <OverdueBadge hours={r.hoursOverdue} />,
            className: 'w-24 text-right',
          },
        ]
      : []),
    ...(kind === 'stuck'
      ? [
          {
            header: 'Idle',
            accessor: (r: OpsOrderDto) => <StuckBadge hours={r.hoursStuck} />,
            className: 'w-24 text-right',
          },
        ]
      : []),
  ]
}

// ── Single bucket section ─────────────────────────────────────────────────────

function BucketSection({
  title,
  icon: Icon,
  iconColor,
  bucket,
  kind,
  onRowClick,
}: {
  title: string
  icon: React.ElementType
  iconColor: string
  bucket: OpsQueueBucket
  kind: 'dueToday' | 'overdue' | 'stuck'
  onRowClick: (r: OpsOrderDto) => void
}) {
  const columns = buildColumns(kind)

  return (
    <section className="space-y-2">
      <div className="flex items-center gap-2">
        <Icon className={cn('h-4 w-4', iconColor)} />
        <h3 className="text-sm font-semibold text-gray-900">{title}</h3>
        <span
          className={cn(
            'inline-flex h-5 min-w-[1.25rem] items-center justify-center rounded-full px-1.5 text-xs font-bold tabular-nums',
            bucket.count === 0
              ? 'bg-gray-100 text-gray-400'
              : kind === 'overdue'
                ? 'bg-red-100 text-red-700'
                : kind === 'stuck'
                  ? 'bg-orange-100 text-orange-700'
                  : 'bg-amber-100 text-amber-700',
          )}
        >
          {bucket.count}
        </span>
      </div>

      {bucket.list.length === 0 ? (
        <p className="py-3 text-center text-xs text-gray-400">
          {bucket.count === 0 ? 'All clear.' : 'No items on this page.'}
        </p>
      ) : (
        <div className="overflow-hidden rounded-xl border border-gray-100">
          <DataTable
            columns={columns}
            data={bucket.list}
            keyFn={(r) => r.id}
            onRowClick={onRowClick}
          />
        </div>
      )}
    </section>
  )
}

// ── Tab chip selector ─────────────────────────────────────────────────────────

type OpsView = 'dueToday' | 'overdue' | 'stuck'

function OpsChips({
  view,
  onChange,
  dueTodayCount,
  overdueCount,
  stuckCount,
}: {
  view: OpsView
  onChange: (v: OpsView) => void
  dueTodayCount: number
  overdueCount: number
  stuckCount: number
}) {
  const chips: { key: OpsView; label: string; count: number }[] = [
    { key: 'dueToday', label: 'Due today', count: dueTodayCount },
    { key: 'overdue',  label: 'Overdue',   count: overdueCount },
    { key: 'stuck',    label: 'Stuck',     count: stuckCount },
  ]

  return (
    <div className="flex flex-wrap gap-2">
      {chips.map((c) => (
        <button
          key={c.key}
          type="button"
          onClick={() => onChange(c.key)}
          className={cn(
            'inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-medium transition-colors',
            view === c.key
              ? 'border-transparent bg-lg-green text-white'
              : 'border-gray-200 bg-white text-gray-600 hover:bg-gray-50',
          )}
        >
          {c.label}
          {c.count > 0 && (
            <span
              className={cn(
                'rounded-full px-1.5 py-0.5 text-xs font-bold',
                view === c.key ? 'bg-white/20' : 'bg-red-100 text-red-700',
              )}
            >
              {c.count}
            </span>
          )}
        </button>
      ))}
    </div>
  )
}

// ── Main component ────────────────────────────────────────────────────────────

export function OpsQueuesTab() {
  const [activeView, setActiveView] = useState<OpsView>('overdue')
  const [selectedId, setSelectedId] = useState<string | null>(null)

  // Refetch every 60s to keep counts fresh without hammering the backend.
  const { data, isLoading, isError, error, refetch } = useOpsQueues({}, 60_000)

  const handleRowClick = (r: OpsOrderDto) => setSelectedId(r.id)

  return (
    <>
      <div className="space-y-5">
        {/* Controls bar */}
        <div className="flex flex-wrap items-center justify-between gap-3">
          {data && (
            <OpsChips
              view={activeView}
              onChange={setActiveView}
              dueTodayCount={data.dueToday.count}
              overdueCount={data.overdue.count}
              stuckCount={data.stuck.count}
            />
          )}
          <button
            type="button"
            onClick={() => void refetch()}
            className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-2.5 py-1.5 text-xs font-medium text-gray-600 hover:bg-gray-50"
          >
            <RefreshCw className="h-3.5 w-3.5" /> Refresh
          </button>
        </div>

        {isLoading && <LoadingState message="Loading ops queues…" />}
        {isError && (isForbiddenError(error) ? <ForbiddenState /> : <ErrorState error={error as Error} onRetry={() => void refetch()} />)}

        {data && activeView === 'dueToday' && (
          <BucketSection
            title="Due today"
            icon={Clock}
            iconColor="text-amber-500"
            bucket={data.dueToday}
            kind="dueToday"
            onRowClick={handleRowClick}
          />
        )}
        {data && activeView === 'overdue' && (
          <BucketSection
            title="Overdue"
            icon={AlertTriangle}
            iconColor="text-red-500"
            bucket={data.overdue}
            kind="overdue"
            onRowClick={handleRowClick}
          />
        )}
        {data && activeView === 'stuck' && (
          <BucketSection
            title="Stuck (no status update)"
            icon={Clock}
            iconColor="text-orange-500"
            bucket={data.stuck}
            kind="stuck"
            onRowClick={handleRowClick}
          />
        )}
      </div>

      <OrderDetailDrawer orderId={selectedId} onClose={() => setSelectedId(null)} />
    </>
  )
}
