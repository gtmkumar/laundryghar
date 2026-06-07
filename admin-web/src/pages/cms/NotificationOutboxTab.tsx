import { useState } from 'react'
import { Loader2 } from 'lucide-react'
import { useNotificationOutboxInfinite, useRetryNotificationOutbox } from '@/hooks/useCms'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { DataTable, type Column } from '@/components/shared/DataTable'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import type { NotificationOutboxDto } from '@/types/api'
import { formatDateTime } from '@/lib/utils'

const OUTBOX_STATUSES = ['pending', 'sent', 'failed', 'cancelled', 'suppressed']

function StatusBadge({ status }: { status: string }) {
  const variantMap: Record<
    string,
    'default' | 'secondary' | 'success' | 'warning' | 'destructive'
  > = {
    pending: 'warning',
    sent: 'success',
    failed: 'destructive',
    cancelled: 'secondary',
    suppressed: 'secondary',
  }
  return (
    <Badge variant={variantMap[status] ?? 'secondary'} className="capitalize">
      {status}
    </Badge>
  )
}

export function NotificationOutboxTab() {
  const [statusFilter, setStatusFilter] = useState('all')

  const {
    data,
    isLoading,
    isError,
    error,
    refetch,
    hasNextPage,
    isFetchingNextPage,
    fetchNextPage,
  } = useNotificationOutboxInfinite(
    statusFilter === 'all' ? undefined : statusFilter,
  )

  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })
  const retryMutation = useRetryNotificationOutbox()

  const columns: Column<NotificationOutboxDto>[] = [
    {
      header: 'Channel',
      accessor: (r) => (
        <Badge variant="secondary" className="capitalize">
          {r.channel}
        </Badge>
      ),
    },
    { header: 'Template Code', accessor: 'templateCode', className: 'font-mono text-xs' },
    { header: 'Recipient', accessor: (r) => r.recipientPhone ?? r.recipientEmail ?? r.recipientType, className: 'text-xs' },
    {
      header: 'Attempts',
      accessor: (r) => (
        <span className="tabular-nums text-xs">
          {r.attempts}/{r.maxAttempts}
        </span>
      ),
      className: 'w-20',
    },
    { header: 'Scheduled', accessor: (r) => formatDateTime(r.scheduledAt), className: 'whitespace-nowrap text-xs' },
    {
      header: 'Sent',
      accessor: (r) =>
        r.sentAt ? (
          <span className="text-xs text-gray-500">{formatDateTime(r.sentAt)}</span>
        ) : (
          <span className="text-gray-300 text-xs">—</span>
        ),
      className: 'whitespace-nowrap',
    },
    { header: 'Status', accessor: (r) => <StatusBadge status={r.status} /> },
    {
      header: 'Error',
      accessor: (r) =>
        r.lastError ? (
          <span className="text-xs text-red-500 truncate max-w-[140px] block" title={r.lastError}>
            {r.lastError}
          </span>
        ) : null,
    },
    {
      header: '',
      accessor: (r) =>
        r.status === 'failed' ? (
          <Button
            variant="outline"
            size="sm"
            onClick={(e) => {
              e.stopPropagation()
              void retryMutation.mutateAsync(r.id)
            }}
            disabled={retryMutation.isPending}
            className="text-xs"
          >
            Retry
          </Button>
        ) : null,
      className: 'w-20',
    },
  ]

  const items = data?.pages.flatMap((p) => p.list) ?? []
  const total = data?.pages[0]?.totalCount

  return (
    <div>
      <div className="flex items-center gap-3 px-4 pt-3 pb-2">
        <span className="text-sm text-gray-500">Status:</span>
        <Select value={statusFilter} onValueChange={setStatusFilter}>
          <SelectTrigger className="w-40 h-8 text-sm">
            <SelectValue placeholder="All" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All</SelectItem>
            {OUTBOX_STATUSES.map((s) => (
              <SelectItem key={s} value={s}>
                <span className="capitalize">{s}</span>
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        {total !== undefined && (
          <span className="ml-auto text-sm text-gray-400">{total} item{total === 1 ? '' : 's'}</span>
        )}
      </div>

      {isLoading && <LoadingState message="Loading outbox..." />}
      {isError && <ErrorState error={error as Error} onRetry={() => void refetch()} />}
      {!isLoading && !isError && (
        <DataTable
          columns={columns}
          data={items}
          keyFn={(r) => r.id}
          emptyMessage="No outbox entries found."
        />
      )}

      <div ref={sentinelRef} className="h-1" />
      {isFetchingNextPage && (
        <div className="flex items-center justify-center py-4 text-gray-400">
          <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more…
        </div>
      )}
    </div>
  )
}
