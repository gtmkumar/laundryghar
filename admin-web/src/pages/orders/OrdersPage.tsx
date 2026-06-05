import { useState } from 'react'
import { useOrders } from '@/hooks/useOrders'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { Pagination } from '@/components/shared/Pagination'
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
  const [page, setPage] = useState(1)
  const [statusFilter, setStatusFilter] = useState('all')

  const { data, isLoading, isError, error, refetch } = useOrders({
    page,
    pageSize: 20,
    status: statusFilter === 'all' ? undefined : statusFilter,
  })

  function handleStatusChange(s: string) {
    setStatusFilter(s)
    setPage(1)
  }

  return (
    <div>
      <PageHeader
        title="Orders"
        description="View and manage customer orders across all stores."
      />

      <OrderFilters status={statusFilter} onStatusChange={handleStatusChange} />

      <Card className="overflow-hidden">
        {isLoading && <LoadingState message="Loading orders..." />}
        {isError && <ErrorState error={error as Error} onRetry={() => void refetch()} />}
        {!isLoading && !isError && (
          <>
            <DataTable
              columns={orderColumns}
              data={data?.list ?? []}
              keyFn={(r) => r.id}
              emptyMessage="No orders match the selected filters."
            />
            <Pagination
              page={page}
              hasPrevious={data?.hasPreviousPage ?? false}
              hasNext={data?.hasNextPage ?? false}
              onPrevious={() => setPage((p) => Math.max(1, p - 1))}
              onNext={() => setPage((p) => p + 1)}
            />
          </>
        )}
      </Card>
    </div>
  )
}
