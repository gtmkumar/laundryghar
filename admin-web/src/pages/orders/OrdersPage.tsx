import { useState } from 'react'
import { Loader2 } from 'lucide-react'
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
import type { OrderDto } from '@/types/api'
import { formatCurrency, formatDateTime } from '@/lib/utils'

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

// ── Page ──────────────────────────────────────────────────────────────────────

export function OrdersPage() {
  const [statusFilter, setStatusFilter] = useState('all')

  const { data, isLoading, isError, error, refetch, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useOrdersInfinite({
      status: statusFilter === 'all' ? undefined : statusFilter,
    })

  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  const orders = data?.pages.flatMap((p) => p.list) ?? []
  const total = data?.pages[0]?.totalCount

  return (
    <div>
      <PageHeader
        title="Orders"
        description={total !== undefined ? `${total} order${total === 1 ? '' : 's'} · View and manage customer orders across all stores.` : 'View and manage customer orders across all stores.'}
      />

      <OrderFilters status={statusFilter} onStatusChange={setStatusFilter} />

      <Card className="overflow-hidden">
        {isLoading && <LoadingState message="Loading orders..." />}
        {isError && <ErrorState error={error as Error} onRetry={() => void refetch()} />}
        {!isLoading && !isError && (
          <DataTable
            columns={orderColumns}
            data={orders}
            keyFn={(r) => r.id}
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
    </div>
  )
}
