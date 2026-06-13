import { useMemo, useState } from 'react'
import { useSupportTickets } from '@/hooks/useSupport'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Card } from '@/components/ui/card'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable } from '@/components/shared/FilterableTable'
import { cn, formatDateTime } from '@/lib/utils'
import type { SupportTicketDto, SupportTicketStatus } from '@/types/api'
import { TicketDetailDrawer } from './TicketDetailDrawer'
import {
  RequesterBadge,
  StatusBadge,
  PriorityBadge,
} from './supportShared'

// The backend returns one status bucket at a time, so the status filter drives
// the query (not an in-memory FilterableTable filter). 'all' omits the param.
type StatusTab = SupportTicketStatus | 'all'

const STATUS_TABS: { key: StatusTab; label: string }[] = [
  { key: 'open', label: 'Open' },
  { key: 'in_progress', label: 'In progress' },
  { key: 'resolved', label: 'Resolved' },
  { key: 'closed', label: 'Closed' },
  { key: 'all', label: 'All' },
]

export function SupportInboxPage() {
  const [statusTab, setStatusTab] = useState<StatusTab>('open')
  const status = statusTab === 'all' ? undefined : statusTab
  const { data, isLoading, isError, error, refetch } = useSupportTickets(status)
  const tickets = useMemo(() => data ?? [], [data])

  const [selected, setSelected] = useState<SupportTicketDto | null>(null)
  const [drawerOpen, setDrawerOpen] = useState(false)

  const openTicket = (t: SupportTicketDto) => {
    setSelected(t)
    setDrawerOpen(true)
  }

  const columns: Column<SupportTicketDto>[] = [
    {
      header: 'Ticket',
      accessor: (t) => <span className="font-medium text-gray-900">{t.ticketNumber}</span>,
      sortKey: 'ticket',
      sortAccessor: (t) => t.ticketNumber,
    },
    {
      header: 'Requester',
      accessor: (t) => (
        <div className="flex items-center gap-2">
          <span className="text-gray-900">{t.requesterName ?? '—'}</span>
          <RequesterBadge type={t.requesterType} />
        </div>
      ),
      sortKey: 'requester',
      sortAccessor: (t) => t.requesterName ?? '',
    },
    {
      header: 'Subject',
      accessor: (t) => <span className="line-clamp-1 max-w-xs text-gray-700">{t.subject}</span>,
      sortKey: 'subject',
      sortAccessor: (t) => t.subject,
    },
    {
      header: 'Category',
      accessor: (t) => <span className="capitalize text-gray-600">{t.category}</span>,
      sortKey: 'category',
      sortAccessor: (t) => t.category,
    },
    {
      header: 'Priority',
      accessor: (t) => <PriorityBadge priority={t.priority} />,
      sortKey: 'priority',
      sortAccessor: (t) => t.priority,
    },
    {
      header: 'Status',
      accessor: (t) => <StatusBadge status={t.status} />,
      sortKey: 'status',
      sortAccessor: (t) => t.status,
    },
    {
      header: 'Last message',
      accessor: (t) => formatDateTime(t.lastMessageAt),
      sortKey: 'lastMessage',
      sortAccessor: (t) => t.lastMessageAt,
    },
  ]

  return (
    <div>
      <PageHeader
        title="Support inbox"
        description="Customer and rider support tickets — reply, prioritise, and resolve."
      />

      {/* Status tabs — drive the query (server returns one bucket at a time). */}
      <div className="mb-4 flex w-fit items-center gap-1 rounded-xl border border-gray-200 bg-white p-1">
        {STATUS_TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setStatusTab(t.key)}
            className={cn(
              'rounded-lg px-3.5 py-1.5 text-sm font-medium transition-colors',
              statusTab === t.key ? 'bg-lg-green text-white' : 'text-gray-600 hover:bg-gray-50',
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      <Card className="overflow-hidden">
        {isLoading ? (
          <LoadingState message="Loading tickets..." />
        ) : isError ? (
          isForbiddenError(error) ? (
            <ForbiddenState />
          ) : (
            <ErrorState error={error as Error} onRetry={() => void refetch()} />
          )
        ) : (
          <FilterableTable
            columns={columns}
            data={tickets}
            keyFn={(t) => t.id}
            onRowClick={openTicket}
            unit="ticket"
            searchPlaceholder="Search ticket #, requester, or subject…"
            searchAccessor={(t) =>
              `${t.ticketNumber} ${t.requesterName ?? ''} ${t.subject} ${t.category}`
            }
            initialSort={{ key: 'lastMessage', dir: 'desc' }}
            csvExport={{
              filename: `support-tickets-${statusTab}`,
              columns: [
                { header: 'Ticket', value: (t) => t.ticketNumber },
                { header: 'Requester', value: (t) => t.requesterName ?? '' },
                { header: 'Requester type', value: (t) => t.requesterType },
                { header: 'Subject', value: (t) => t.subject },
                { header: 'Category', value: (t) => t.category },
                { header: 'Priority', value: (t) => t.priority },
                { header: 'Status', value: (t) => t.status },
                { header: 'Last message', value: (t) => t.lastMessageAt },
                { header: 'Created', value: (t) => t.createdAt },
              ],
            }}
            emptyMessage={emptyMessageFor(statusTab)}
            noMatchMessage="No tickets match your search."
          />
        )}
      </Card>

      <TicketDetailDrawer
        ticket={selected}
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
      />
    </div>
  )
}

function emptyMessageFor(status: StatusTab): string {
  switch (status) {
    case 'open':
      return 'No open tickets — the inbox is clear.'
    case 'in_progress':
      return 'No tickets are currently in progress.'
    case 'resolved':
      return 'No resolved tickets.'
    case 'closed':
      return 'No closed tickets.'
    case 'all':
      return 'No support tickets yet.'
  }
}
