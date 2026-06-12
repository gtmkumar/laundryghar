import { useMemo, useState } from 'react'
import { Eye, Pencil, Loader2 } from 'lucide-react'
import { useAdminCustomersInfinite } from '@/hooks/useCatalog'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { usePermissions } from '@/hooks/usePermissions'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { ActionMenu, ActionMenuItem } from '@/components/ui/ActionMenu'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable, type FilterDef } from '@/components/shared/FilterableTable'
import { CustomerDetailDrawer, CustomerEditDrawer, customerName } from './CustomerDrawers'
import type { AdminCustomerDto } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

function StatusBadge({ status }: { status: string }) {
  const variant =
    status === 'active' ? 'success' : status === 'blocked' || status === 'inactive' ? 'secondary' : 'warning'
  return (
    <Badge variant={variant} className="capitalize">
      {status.replace(/_/g, ' ')}
    </Badge>
  )
}

/** Distinct dropdown options from a value column present in the data. */
function distinctOptions(rows: AdminCustomerDto[], read: (r: AdminCustomerDto) => string | null) {
  const seen = new Set<string>()
  for (const r of rows) {
    const v = read(r)
    if (v) seen.add(v)
  }
  return [...seen].sort().map((v) => ({ value: v, label: v.replace(/_/g, ' ') }))
}

const baseColumns: Column<AdminCustomerDto>[] = [
  { header: 'Code', accessor: 'customerCode', className: 'font-mono text-xs w-24', sortKey: 'code' },
  {
    header: 'Name',
    accessor: (r) => customerName(r),
    sortKey: 'name',
    sortAccessor: (r) => customerName(r),
  },
  { header: 'Phone', accessor: (r) => <span className="font-mono text-xs">{r.phoneE164}</span> },
  {
    header: 'Segment',
    accessor: (r) =>
      r.customerSegment ? (
        <Badge variant="secondary" className="capitalize">
          {r.customerSegment.replace(/_/g, ' ')}
        </Badge>
      ) : (
        <span className="text-gray-400">—</span>
      ),
    sortKey: 'segment',
    sortAccessor: (r) => r.customerSegment ?? '',
  },
  {
    header: 'Orders',
    accessor: (r) => <span className="tabular-nums">{r.lifetimeOrders}</span>,
    className: 'text-right',
    sortKey: 'orders',
    sortAccessor: (r) => r.lifetimeOrders,
  },
  {
    header: 'Spend',
    accessor: (r) => <span className="tabular-nums">{formatCurrency(r.lifetimeSpend)}</span>,
    className: 'text-right',
    sortKey: 'spend',
    sortAccessor: (r) => r.lifetimeSpend,
  },
  {
    header: 'Status',
    accessor: (r) => <StatusBadge status={r.status} />,
    sortKey: 'status',
    sortAccessor: (r) => r.status,
  },
  {
    header: 'Joined',
    accessor: (r) => formatDate(r.createdAt),
    className: 'text-right',
    sortKey: 'createdAt',
    sortAccessor: (r) => r.createdAt,
  },
]

export function CustomersPage() {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('customer.update')
  const {
    data,
    isLoading,
    isError,
    error,
    refetch,
    hasNextPage,
    isFetchingNextPage,
    fetchNextPage,
  } = useAdminCustomersInfinite()

  const customers = useMemo(() => data?.pages.flatMap((p) => p.list) ?? [], [data])
  // Grand total comes off any page (the backend returns the same totalCount on
  // every page) — never the loaded-slice length, which undercounts at scale.
  const total = data?.pages[0]?.totalCount

  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  const [viewing, setViewing] = useState<AdminCustomerDto | null>(null)
  const [editing, setEditing] = useState<AdminCustomerDto | null>(null)

  const columns: Column<AdminCustomerDto>[] = [
    ...baseColumns,
    {
      header: '',
      className: 'w-12 text-right',
      accessor: (r) => (
        <div onClick={(e) => e.stopPropagation()}>
          <ActionMenu label="Customer actions">
            {(close) => (
              <>
                <ActionMenuItem icon={Eye} onClick={() => { close(); setViewing(r) }}>
                  View
                </ActionMenuItem>
                {canManage && (
                  <ActionMenuItem icon={Pencil} onClick={() => { close(); setEditing(r) }}>
                    Edit
                  </ActionMenuItem>
                )}
              </>
            )}
          </ActionMenu>
        </div>
      ),
    },
  ]

  const filters: FilterDef<AdminCustomerDto>[] = [
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (c) => c.status,
      options: distinctOptions(customers, (c) => c.status),
    },
    {
      key: 'segment',
      allLabel: 'All segments',
      value: (c) => c.customerSegment ?? '',
      options: distinctOptions(customers, (c) => c.customerSegment),
    },
  ]

  return (
    <div>
      <PageHeader title="Customers" description="Browse and search customers across the brand." />
      <Card className="overflow-hidden">
        {isLoading ? (
          <LoadingState message="Loading customers..." />
        ) : isError ? (
          isForbiddenError(error) ? <ForbiddenState /> : <ErrorState error={error as Error} onRetry={() => void refetch()} />
        ) : (
          <FilterableTable
            columns={columns}
            data={customers}
            keyFn={(r) => r.id}
            onRowClick={(c) => setViewing(c)}
            unit="customer"
            totalCount={total}
            searchPlaceholder="Search code, name, phone, email…"
            searchAccessor={(c) =>
              `${c.customerCode} ${customerName(c)} ${c.phoneE164} ${c.email ?? ''}`
            }
            filters={filters}
            initialSort={{ key: 'createdAt', dir: 'desc' }}
            emptyMessage="No customers found."
            noMatchMessage="No customers match your filters."
            csvExport={{
              filename: `customers-${new Date().toISOString().slice(0, 10)}`,
              columns: [
                { header: 'Code', value: (r) => r.customerCode },
                { header: 'Name', value: (r) => customerName(r) },
                { header: 'Phone', value: (r) => r.phoneE164 },
                { header: 'Email', value: (r) => r.email ?? '' },
                { header: 'Segment', value: (r) => r.customerSegment ?? '' },
                { header: 'Orders', value: (r) => r.lifetimeOrders },
                { header: 'Spend', value: (r) => r.lifetimeSpend },
                { header: 'Status', value: (r) => r.status },
                { header: 'Joined', value: (r) => r.createdAt },
              ],
            }}
            footer={
              <>
                <div ref={sentinelRef} className="h-1" />
                {isFetchingNextPage && (
                  <div className="flex items-center justify-center py-4 text-sm text-gray-400">
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more customers…
                  </div>
                )}
              </>
            }
          />
        )}
      </Card>

      <CustomerDetailDrawer
        customer={viewing}
        onClose={() => setViewing(null)}
        canManage={canManage}
        onEdit={(c) => {
          setViewing(null)
          setEditing(c)
        }}
      />
      <CustomerEditDrawer customer={editing} onClose={() => setEditing(null)} />
    </div>
  )
}
