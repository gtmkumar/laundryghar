import { useMemo, useState } from 'react'
import { Plus } from 'lucide-react'
import { useCashBooks } from '@/hooks/useFinance'
import { useStores } from '@/hooks/useTenancy'
import { useBrandStore } from '@/stores/brandStore'
import { usePermissions } from '@/hooks/usePermissions'
import { OpenCashBookDrawer } from './FinanceDrawers'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable, type FilterDef } from '@/components/shared/FilterableTable'
import type { CashBookSummaryDto } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

const SHIFTS = ['morning', 'afternoon', 'evening', 'night', 'full_day']

function CashBookStatusBadge({ status }: { status: string }) {
  const variant =
    status === 'finalized' || status === 'reviewed' || status === 'closed'
      ? 'success'
      : status === 'disputed'
        ? 'destructive'
        : status === 'open' || status === 'closing'
          ? 'warning'
          : 'secondary'
  return (
    <Badge variant={variant} className="capitalize">
      {status.replace(/_/g, ' ')}
    </Badge>
  )
}

function distinctOptions(rows: CashBookSummaryDto[], read: (r: CashBookSummaryDto) => string | null) {
  const seen = new Set<string>()
  for (const r of rows) {
    const v = read(r)
    if (v) seen.add(v)
  }
  return [...seen].sort().map((v) => ({ value: v, label: v.replace(/_/g, ' ') }))
}

function money(n: number | null) {
  return n == null ? <span className="text-gray-400">—</span> : <span className="tabular-nums">{formatCurrency(n)}</span>
}

export function CashBookPage() {
  const { activeBrandId } = useBrandStore()
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('cashbook.manage')
  const [openDrawer, setOpenDrawer] = useState(false)
  const { data, isLoading, isError, error, refetch } = useCashBooks({ pageSize: 100 })

  const storesQ = useStores({ brandId: activeBrandId ?? undefined, pageSize: 100 })
  const storeName = useMemo(() => {
    const m = new Map<string, string>()
    for (const s of storesQ.data?.list ?? []) m.set(s.id, s.name)
    return m
  }, [storesQ.data])

  const books = useMemo(() => data?.list ?? [], [data])
  const total = data?.totalCount

  const columns: Column<CashBookSummaryDto>[] = [
    {
      header: 'Date',
      accessor: (r) => formatDate(r.bookDate),
      sortKey: 'date',
      sortAccessor: (r) => r.bookDate,
    },
    {
      header: 'Store',
      accessor: (r) => (
        <span className={storeName.has(r.storeId) ? '' : 'text-gray-400'}>
          {storeName.get(r.storeId) ?? '—'}
        </span>
      ),
      sortKey: 'store',
      sortAccessor: (r) => storeName.get(r.storeId) ?? '',
    },
    {
      header: 'Shift',
      accessor: (r) => <span className="capitalize">{r.shiftLabel.replace(/_/g, ' ')}</span>,
      sortKey: 'shift',
      sortAccessor: (r) => r.shiftLabel,
    },
    { header: 'Opening', accessor: (r) => money(r.openingBalance), className: 'text-right', sortKey: 'opening', sortAccessor: (r) => r.openingBalance },
    { header: 'Inflow', accessor: (r) => money(r.cashInflow), className: 'text-right', sortKey: 'inflow', sortAccessor: (r) => r.cashInflow },
    { header: 'Outflow', accessor: (r) => money(r.cashOutflow), className: 'text-right', sortKey: 'outflow', sortAccessor: (r) => r.cashOutflow },
    { header: 'Closing', accessor: (r) => money(r.closingBalance), className: 'text-right', sortKey: 'closing', sortAccessor: (r) => r.closingBalance ?? -Infinity },
    {
      header: 'Variance',
      accessor: (r) =>
        r.variance == null ? (
          <span className="text-gray-400">—</span>
        ) : (
          <span className={`tabular-nums ${r.variance < 0 ? 'text-red-600' : r.variance > 0 ? 'text-amber-600' : ''}`}>
            {formatCurrency(r.variance)}
          </span>
        ),
      className: 'text-right',
      sortKey: 'variance',
      sortAccessor: (r) => r.variance ?? 0,
    },
    {
      header: 'Status',
      accessor: (r) => <CashBookStatusBadge status={r.status} />,
      sortKey: 'status',
      sortAccessor: (r) => r.status,
    },
  ]

  const filters: FilterDef<CashBookSummaryDto>[] = [
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (b) => b.status,
      options: distinctOptions(books, (b) => b.status),
    },
    {
      key: 'shift',
      allLabel: 'All shifts',
      value: (b) => b.shiftLabel,
      options: SHIFTS.map((s) => ({ value: s, label: s.replace(/_/g, ' ') })),
    },
  ]

  return (
    <div>
      <PageHeader
        title="Cash book"
        description="Daily cash reconciliation per store and shift."
        action={
          canManage ? (
            <button
              type="button"
              onClick={() => setOpenDrawer(true)}
              className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
            >
              <Plus className="h-4 w-4" /> Open cash book
            </button>
          ) : undefined
        }
      />
      <Card className="overflow-hidden">
        {isLoading ? (
          <LoadingState message="Loading cash books..." />
        ) : isError ? (
          <ErrorState error={error as Error} onRetry={() => void refetch()} />
        ) : (
          <FilterableTable
            columns={columns}
            data={books}
            keyFn={(r) => r.id}
            unit="cash book"
            totalCount={total}
            searchPlaceholder="Search store or shift…"
            searchAccessor={(b) => `${storeName.get(b.storeId) ?? ''} ${b.shiftLabel} ${b.status}`}
            filters={filters}
            initialSort={{ key: 'date', dir: 'desc' }}
            emptyMessage="No cash books found."
            noMatchMessage="No cash books match your filters."
          />
        )}
      </Card>

      <OpenCashBookDrawer open={openDrawer} onClose={() => setOpenDrawer(false)} />
    </div>
  )
}
