import { useMemo, useState } from 'react'
import { ChevronRight } from 'lucide-react'
import { usePickupRequests } from '@/hooks/usePickups'
import { useAdminCustomers } from '@/hooks/useCatalog'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable, type FilterDef } from '@/components/shared/FilterableTable'
import { formatCurrency, formatDate, formatDateTime } from '@/lib/utils'
import type { PickupRequestDto } from '@/types/api'
import { PickupDetailDrawer } from './PickupDetailDrawer'
import {
  PickupStatusBadge,
  PaymentPrefBadge,
  PICKUP_STATUSES,
  PAYMENT_PREFERENCES,
  paymentPreferenceLabel,
  formatWindow,
  humanise,
} from './pickupShared'

export function PickupRequestsTab() {
  const { data, isLoading, isError, error, refetch } = usePickupRequests({ pageSize: 100 })

  // Enrich rows with customer display names (the DTO only carries customerId).
  // The customer list lives in the Catalog service; one brand-scoped page is
  // enough for the queue and mirrors the Tenancy franchise-name lookup pattern.
  const customersQ = useAdminCustomers({ pageSize: 100 })
  const customerName = useMemo(() => {
    const m = new Map<string, string>()
    for (const c of customersQ.data?.list ?? []) {
      const name = c.displayName?.trim() || [c.firstName, c.lastName].filter(Boolean).join(' ').trim()
      m.set(c.id, name || c.customerCode)
    }
    return m
  }, [customersQ.data])

  const requests = useMemo(() => data?.list ?? [], [data])
  const total = data?.totalCount

  const [selectedId, setSelectedId] = useState<string | null>(null)

  const columns: Column<PickupRequestDto>[] = [
    {
      header: 'Request #',
      accessor: (r) => (
        <span className="font-mono text-xs font-medium text-lg-green">{r.requestNumber}</span>
      ),
      className: 'w-40',
      sortKey: 'requestNumber',
      sortAccessor: (r) => r.requestNumber,
    },
    {
      header: 'Customer',
      accessor: (r) =>
        customerName.has(r.customerId) ? (
          <span className="text-gray-700">{customerName.get(r.customerId)}</span>
        ) : (
          <span className="font-mono text-[11px] text-gray-400">{r.customerId.slice(0, 8)}…</span>
        ),
      sortKey: 'customer',
      sortAccessor: (r) => customerName.get(r.customerId) ?? r.customerId,
    },
    {
      header: 'Pickup',
      accessor: (r) => (
        <div className="whitespace-nowrap">
          <span className="text-gray-700">{formatDate(r.pickupDate)}</span>
          <span className="block text-xs text-gray-400">
            {formatWindow(r.pickupWindowStart, r.pickupWindowEnd)}
          </span>
        </div>
      ),
      sortKey: 'pickupDate',
      sortAccessor: (r) => `${r.pickupDate} ${r.pickupWindowStart}`,
    },
    {
      header: 'Items',
      accessor: (r) => (
        <span className="tabular-nums">{r.estimatedItems ?? r.cartItems.length}</span>
      ),
      className: 'text-right w-16',
      sortKey: 'items',
      sortAccessor: (r) => r.estimatedItems ?? r.cartItems.length,
    },
    {
      header: 'Est. amount',
      accessor: (r) => (
        <span className="tabular-nums font-medium">
          {r.estimatedAmount != null ? formatCurrency(r.estimatedAmount) : '—'}
        </span>
      ),
      className: 'text-right whitespace-nowrap',
      sortKey: 'amount',
      sortAccessor: (r) => r.estimatedAmount ?? 0,
    },
    {
      header: 'Payment',
      accessor: (r) => <PaymentPrefBadge pref={r.paymentPreference} />,
      sortKey: 'payment',
      sortAccessor: (r) => r.paymentPreference,
    },
    {
      header: 'Status',
      accessor: (r) => <PickupStatusBadge status={r.status} />,
      sortKey: 'status',
      sortAccessor: (r) => r.status,
    },
    {
      header: 'Created',
      accessor: (r) => <span className="whitespace-nowrap">{formatDateTime(r.createdAt)}</span>,
      sortKey: 'createdAt',
      sortAccessor: (r) => r.createdAt,
    },
    {
      header: '',
      accessor: () => <ChevronRight className="h-4 w-4 text-gray-300" />,
      className: 'w-8 text-right',
    },
  ]

  const filters: FilterDef<PickupRequestDto>[] = [
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (r) => r.status,
      options: PICKUP_STATUSES.map((s) => ({ value: s, label: humanise(s) })),
    },
    {
      key: 'payment',
      allLabel: 'All payments',
      value: (r) => r.paymentPreference,
      options: PAYMENT_PREFERENCES,
    },
  ]

  if (isLoading) return <LoadingState message="Loading pickup requests..." />
  if (isError) return isForbiddenError(error) ? <ForbiddenState /> : <ErrorState error={error as Error} onRetry={() => void refetch()} />

  return (
    <>
      <FilterableTable
        columns={columns}
        data={requests}
        keyFn={(r) => r.id}
        onRowClick={(r) => setSelectedId(r.id)}
        unit="request"
        totalCount={total}
        searchPlaceholder="Search request #, customer…"
        searchAccessor={(r) =>
          `${r.requestNumber} ${customerName.get(r.customerId) ?? r.customerId} ${paymentPreferenceLabel(
            r.paymentPreference,
          )}`
        }
        filters={filters}
        initialSort={{ key: 'createdAt', dir: 'desc' }}
        emptyMessage="No pickup requests yet. New customer bookings will appear here."
        noMatchMessage="No pickup requests match your filters."
      />

      <PickupDetailDrawer
        pickupId={selectedId}
        open={selectedId !== null}
        onClose={() => setSelectedId(null)}
      />
    </>
  )
}
