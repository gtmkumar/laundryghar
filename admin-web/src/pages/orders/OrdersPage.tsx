import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Loader2, ChevronRight } from 'lucide-react'
import { useOrdersInfinite } from '@/hooks/useOrders'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { DataTable, type Column } from '@/components/shared/DataTable'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { cn } from '@/lib/utils'
import type { OrderDto } from '@/types/api'
import { formatCurrency, formatDateTime } from '@/lib/utils'
import { OrderDetailDrawer } from './OrderDetailDrawer'
import { PickupRequestsTab } from './PickupRequestsTab'
import { OpsQueuesTab } from './OpsQueuesTab'

// ── Helpers ───────────────────────────────────────────────────────────────────

function OrderStatusBadge({ status }: { status: string }) {
  const variantMap: Record<string, 'default' | 'secondary' | 'success' | 'warning' | 'destructive'> = {
    pending: 'warning',
    confirmed: 'default',
    processing: 'default',
    ready: 'success',
    delivered: 'success',
    cancelled: 'destructive',
  }
  return (
    <Badge variant={variantMap[status] ?? 'secondary'} className="capitalize">
      {status}
    </Badge>
  )
}

function PaymentBadge({ status }: { status: string }) {
  return (
    <Badge
      variant={status === 'paid' ? 'success' : status === 'pending' ? 'warning' : 'secondary'}
      className="capitalize"
    >
      {status}
    </Badge>
  )
}

// ── Table columns ─────────────────────────────────────────────────────────────

const orderColumns: Column<OrderDto>[] = [
  {
    header: 'Order #',
    accessor: (r) => (
      <span className="font-mono text-xs font-medium text-blue-700">{r.orderNumber}</span>
    ),
    className: 'w-36',
  },
  { header: 'Placed At', accessor: (r) => formatDateTime(r.placedAt), className: 'whitespace-nowrap' },
  {
    header: 'Channel',
    accessor: (r) => (
      <span className="capitalize text-gray-600">{r.channel}</span>
    ),
  },
  {
    header: 'Items',
    accessor: (r) => <span className="tabular-nums">{r.totalItems}</span>,
    className: 'text-right w-16',
  },
  {
    header: 'Total',
    accessor: (r) => (
      <span className="tabular-nums font-medium">
        {formatCurrency(r.grandTotal, r.currencyCode)}
      </span>
    ),
    className: 'text-right whitespace-nowrap',
  },
  {
    header: 'Status',
    accessor: (r) => <OrderStatusBadge status={r.status} />,
  },
  {
    header: 'Payment',
    accessor: (r) => <PaymentBadge status={r.paymentStatus} />,
  },
  {
    header: 'Express',
    accessor: (r) =>
      r.isExpress ? <Badge variant="warning">Express</Badge> : null,
  },
  {
    header: '',
    accessor: () => <ChevronRight className="h-4 w-4 text-gray-300" />,
    className: 'w-8 text-right',
  },
]

// ── Filters bar ───────────────────────────────────────────────────────────────

const ORDER_STATUSES = [
  'pending',
  'confirmed',
  'in_pickup',
  'picked_up',
  'at_warehouse',
  'processing',
  'ready',
  'out_for_delivery',
  'delivered',
  'cancelled',
  'refunded',
]

interface FiltersProps {
  status: string
  onStatusChange: (s: string) => void
}

function OrderFilters({ status, onStatusChange }: FiltersProps) {
  return (
    <div className="flex items-center gap-3 mb-4">
      <span className="text-sm text-gray-500">Filter by status:</span>
      <Select value={status} onValueChange={onStatusChange}>
        <SelectTrigger className="w-44 h-8 text-sm">
          <SelectValue placeholder="All statuses" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">All statuses</SelectItem>
          {ORDER_STATUSES.map((s) => (
            <SelectItem key={s} value={s}>
              <span className="capitalize">{s.replace(/_/g, ' ')}</span>
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  )
}

// ── Orders tab (the original list) ─────────────────────────────────────────────

function OrdersTab() {
  const [statusFilter, setStatusFilter] = useState('all')
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useOrdersInfinite({
      status: statusFilter === 'all' ? undefined : statusFilter,
    })

  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  const orders = data?.pages.flatMap((p) => p.list) ?? []

  return (
    <>
      <OrderFilters status={statusFilter} onStatusChange={setStatusFilter} />

      <Card className="overflow-hidden">
        {isLoading && <LoadingState message="Loading orders..." />}
        {isError && <ErrorState error={error as Error} onRetry={() => void refetch()} />}
        {!isLoading && !isError && (
          <DataTable
            columns={orderColumns}
            data={orders}
            keyFn={(r) => r.id}
            onRowClick={(r) => setSelectedId(r.id)}
            emptyMessage="No orders match the selected filters."
          />
        )}
      </Card>

      <div ref={sentinelRef} className="h-1" />
      {isFetchingNextPage && (
        <div className="flex items-center justify-center py-4 text-gray-400">
          <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more orders…
        </div>
      )}

      <OrderDetailDrawer orderId={selectedId} onClose={() => setSelectedId(null)} />
    </>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

type OrdersView = 'orders' | 'pickups' | 'ops'

export function OrdersPage() {
  // Tab synced to ?tab= for deep-linking (mirrors the Riders page ?view= pattern).
  const [searchParams, setSearchParams] = useSearchParams()
  const tabParam = searchParams.get('tab')
  const view: OrdersView =
    tabParam === 'pickups' ? 'pickups' : tabParam === 'ops' ? 'ops' : 'orders'
  const setView = (v: OrdersView) => {
    const next = new URLSearchParams(searchParams)
    if (v === 'orders') next.delete('tab')
    else next.set('tab', v)
    setSearchParams(next, { replace: true })
  }

  const tabs: { key: OrdersView; label: string }[] = [
    { key: 'orders',  label: 'Orders' },
    { key: 'pickups', label: 'Pickup requests' },
    { key: 'ops',     label: 'Ops queues' },
  ]

  return (
    <div>
      <PageHeader
        title="Orders"
        description="View and manage customer orders, pickup requests, and overdue ops queues."
      />

      {/* Tab bar — Orders list | Pickup requests | Ops queues */}
      <div className="mb-5 flex w-fit items-center gap-1 rounded-xl border border-gray-200 bg-white p-1">
        {tabs.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setView(t.key)}
            className={cn(
              'rounded-lg px-3.5 py-1.5 text-sm font-medium transition-colors',
              view === t.key ? 'bg-lg-green text-white' : 'text-gray-600 hover:bg-gray-50',
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      {view === 'pickups' ? (
        <Card className="overflow-hidden">
          <PickupRequestsTab />
        </Card>
      ) : view === 'ops' ? (
        <div className="space-y-4">
          <OpsQueuesTab />
        </div>
      ) : (
        <OrdersTab />
      )}
    </div>
  )
}
