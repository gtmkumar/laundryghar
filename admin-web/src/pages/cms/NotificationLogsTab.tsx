import { useState } from 'react'
import { useNotificationLogs, useWhatsAppLogs } from '@/hooks/useCms'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { Pagination } from '@/components/shared/Pagination'
import { DataTable, type Column } from '@/components/shared/DataTable'
import { Badge } from '@/components/ui/badge'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import type { NotificationLogDto, WhatsAppMessageLogDto } from '@/types/api'
import { formatDateTime } from '@/lib/utils'

type LogTab = 'delivery' | 'whatsapp'

const CHANNELS = ['sms', 'whatsapp', 'email', 'push', 'in_app', 'voice']
const WA_DIRECTIONS = ['outbound', 'inbound']

// ── Delivery Logs ─────────────────────────────────────────────────────────────

function DeliveryStatusBadge({ status }: { status: string }) {
  const variantMap: Record<
    string,
    'default' | 'secondary' | 'success' | 'warning' | 'destructive'
  > = {
    sent: 'default',
    delivered: 'success',
    read: 'success',
    clicked: 'success',
    failed: 'destructive',
    bounced: 'destructive',
  }
  return (
    <Badge variant={variantMap[status] ?? 'secondary'} className="capitalize">
      {status}
    </Badge>
  )
}

function DeliveryLogsSection() {
  const [page, setPage] = useState(1)
  const [channelFilter, setChannelFilter] = useState('all')

  const { data, isLoading, isError, error, refetch } = useNotificationLogs({
    page,
    pageSize: 20,
    channel: channelFilter === 'all' ? undefined : channelFilter,
  })

  const columns: Column<NotificationLogDto>[] = [
    {
      header: 'Channel',
      accessor: (r) => (
        <Badge variant="secondary" className="capitalize">
          {r.channel}
        </Badge>
      ),
    },
    { header: 'Template', accessor: (r) => r.templateCode ?? '—', className: 'font-mono text-xs' },
    { header: 'Recipient', accessor: (r) => r.recipientAddress ?? r.recipientType, className: 'text-xs' },
    { header: 'Provider', accessor: (r) => r.provider ?? '—', className: 'text-xs text-gray-500' },
    { header: 'Sent At', accessor: (r) => formatDateTime(r.sentAt), className: 'whitespace-nowrap text-xs' },
    {
      header: 'Delivered',
      accessor: (r) =>
        r.deliveredAt ? (
          <span className="text-xs text-gray-500">{formatDateTime(r.deliveredAt)}</span>
        ) : (
          <span className="text-gray-300 text-xs">—</span>
        ),
      className: 'whitespace-nowrap',
    },
    { header: 'Status', accessor: (r) => <DeliveryStatusBadge status={r.status} /> },
    {
      header: 'Failure',
      accessor: (r) =>
        r.failureMessage ? (
          <span
            className="text-xs text-red-500 truncate max-w-[120px] block"
            title={r.failureMessage}
          >
            {r.failureMessage}
          </span>
        ) : null,
    },
  ]

  return (
    <div>
      <div className="flex items-center gap-3 px-4 pt-3 pb-2">
        <span className="text-sm text-gray-500">Channel:</span>
        <Select
          value={channelFilter}
          onValueChange={(v) => {
            setChannelFilter(v)
            setPage(1)
          }}
        >
          <SelectTrigger className="w-40 h-8 text-sm">
            <SelectValue placeholder="All" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All channels</SelectItem>
            {CHANNELS.map((c) => (
              <SelectItem key={c} value={c}>
                <span className="capitalize">{c}</span>
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {isLoading && <LoadingState message="Loading notification logs..." />}
      {isError && <ErrorState error={error as Error} onRetry={() => void refetch()} />}
      {!isLoading && !isError && (
        <>
          <DataTable
            columns={columns}
            data={data?.list ?? []}
            keyFn={(r) => r.id}
            emptyMessage="No notification logs found."
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
    </div>
  )
}

// ── WhatsApp Logs ─────────────────────────────────────────────────────────────

function WhatsAppStatusBadge({ status }: { status: string | null }) {
  if (!status) return <span className="text-gray-300 text-xs">—</span>
  const variantMap: Record<
    string,
    'default' | 'secondary' | 'success' | 'warning' | 'destructive'
  > = {
    sent: 'default',
    delivered: 'success',
    read: 'success',
    failed: 'destructive',
  }
  return (
    <Badge variant={variantMap[status] ?? 'secondary'} className="capitalize">
      {status}
    </Badge>
  )
}

function WhatsAppLogsSection() {
  const [page, setPage] = useState(1)
  const [directionFilter, setDirectionFilter] = useState('all')

  const { data, isLoading, isError, error, refetch } = useWhatsAppLogs({
    page,
    pageSize: 20,
    direction: directionFilter === 'all' ? undefined : directionFilter,
  })

  const columns: Column<WhatsAppMessageLogDto>[] = [
    {
      header: 'Direction',
      accessor: (r) => (
        <Badge
          variant={r.direction === 'outbound' ? 'default' : 'secondary'}
          className="capitalize"
        >
          {r.direction}
        </Badge>
      ),
    },
    { header: 'Phone', accessor: 'phoneE164', className: 'font-mono text-xs' },
    { header: 'Template', accessor: (r) => r.templateName ?? '—', className: 'text-xs text-gray-500' },
    { header: 'Type', accessor: (r) => r.messageType ?? '—', className: 'text-xs capitalize' },
    {
      header: 'Body',
      accessor: (r) =>
        r.bodyText ? (
          <span
            className="text-xs truncate max-w-[160px] block text-gray-600"
            title={r.bodyText}
          >
            {r.bodyText}
          </span>
        ) : null,
    },
    { header: 'Provider', accessor: 'provider', className: 'text-xs text-gray-500' },
    { header: 'Sent At', accessor: (r) => formatDateTime(r.sentAt), className: 'whitespace-nowrap text-xs' },
    { header: 'Status', accessor: (r) => <WhatsAppStatusBadge status={r.status} /> },
    {
      header: 'Error',
      accessor: (r) =>
        r.errorMessage ? (
          <span
            className="text-xs text-red-500 truncate max-w-[100px] block"
            title={r.errorMessage}
          >
            {r.errorMessage}
          </span>
        ) : null,
    },
  ]

  return (
    <div>
      <div className="flex items-center gap-3 px-4 pt-3 pb-2">
        <span className="text-sm text-gray-500">Direction:</span>
        <Select
          value={directionFilter}
          onValueChange={(v) => {
            setDirectionFilter(v)
            setPage(1)
          }}
        >
          <SelectTrigger className="w-40 h-8 text-sm">
            <SelectValue placeholder="All" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All</SelectItem>
            {WA_DIRECTIONS.map((d) => (
              <SelectItem key={d} value={d}>
                <span className="capitalize">{d}</span>
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {isLoading && <LoadingState message="Loading WhatsApp logs..." />}
      {isError && <ErrorState error={error as Error} onRetry={() => void refetch()} />}
      {!isLoading && !isError && (
        <>
          <DataTable
            columns={columns}
            data={data?.list ?? []}
            keyFn={(r) => r.id}
            emptyMessage="No WhatsApp logs found."
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
    </div>
  )
}

// ── Combined Log Tab ───────────────────────────────────────────────────────────

export function NotificationLogsTab() {
  const [logTab, setLogTab] = useState<LogTab>('delivery')

  const logTabs: { id: LogTab; label: string }[] = [
    { id: 'delivery', label: 'Delivery Logs' },
    { id: 'whatsapp', label: 'WhatsApp Logs' },
  ]

  return (
    <div>
      <div className="flex gap-1 border-b border-gray-100 px-4 pt-3">
        {logTabs.map((t) => (
          <button
            key={t.id}
            onClick={() => setLogTab(t.id)}
            className={[
              'px-3 py-1.5 text-xs font-medium border-b-2 transition-colors',
              logTab === t.id
                ? 'border-blue-600 text-blue-600'
                : 'border-transparent text-gray-500 hover:text-gray-700',
            ].join(' ')}
          >
            {t.label}
          </button>
        ))}
      </div>

      {logTab === 'delivery' && <DeliveryLogsSection />}
      {logTab === 'whatsapp' && <WhatsAppLogsSection />}
    </div>
  )
}
