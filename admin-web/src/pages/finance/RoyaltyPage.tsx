import { useMemo, useState } from 'react'
import { Plus } from 'lucide-react'
import { useRoyaltyInvoices } from '@/hooks/useFinance'
import { usePermissions } from '@/hooks/usePermissions'
import { useFranchises } from '@/hooks/useTenancy'
import { useBrandStore } from '@/stores/brandStore'
import { GenerateRoyaltyDrawer, RoyaltyDetailDrawer } from './FinanceDrawers'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable, type FilterDef } from '@/components/shared/FilterableTable'
import type { RoyaltyInvoiceDto } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

// ── Status badge ──────────────────────────────────────────────────────────────

function RoyaltyStatusBadge({ status }: { status: string }) {
  const variant =
    status === 'paid'
      ? 'success'
      : status === 'issued' || status === 'sent' || status === 'viewed'
        ? 'default'
        : status === 'partial'
          ? 'warning'
          : status === 'overdue'
            ? 'destructive'
            : 'secondary' // draft / void / disputed
  return (
    <Badge variant={variant} className="capitalize">
      {status.replace(/_/g, ' ')}
    </Badge>
  )
}

// ── Columns ───────────────────────────────────────────────────────────────────

const STATUSES = ['draft', 'issued', 'sent', 'viewed', 'partial', 'paid', 'overdue', 'void', 'disputed']

function formatPeriod(start: string) {
  // "2026-01-01" → "Jan 2026"
  const d = new Date(start + 'T00:00:00')
  return d.toLocaleDateString('en-IN', { month: 'short', year: 'numeric' })
}

const baseColumns: Column<RoyaltyInvoiceDto>[] = [
  {
    header: 'Invoice no.',
    accessor: 'invoiceNumber',
    className: 'font-mono text-xs w-36',
    sortKey: 'invoice',
    sortAccessor: (r) => r.invoiceNumber,
  },
  {
    header: 'Period',
    accessor: (r) => formatPeriod(r.periodStart),
    sortKey: 'period',
    sortAccessor: (r) => r.periodStart,
  },
  {
    header: 'Franchise',
    accessor: (r) => r.franchiseId,
    sortKey: 'franchise',
    sortAccessor: (r) => r.franchiseId,
  },
  {
    header: 'Grand total',
    accessor: (r) => <span className="tabular-nums">{formatCurrency(r.grandTotal)}</span>,
    className: 'text-right',
    sortKey: 'total',
    sortAccessor: (r) => r.grandTotal,
  },
  {
    header: 'Due date',
    accessor: (r) => formatDate(r.dueDate),
    sortKey: 'due',
    sortAccessor: (r) => r.dueDate,
  },
  {
    header: 'Status',
    accessor: (r) => <RoyaltyStatusBadge status={r.status} />,
    sortKey: 'status',
    sortAccessor: (r) => r.status,
  },
]

// ── Page ──────────────────────────────────────────────────────────────────────

export function RoyaltyPage() {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('royalty.manage')

  const { activeBrandId } = useBrandStore()
  const franchisesQ = useFranchises({ brandId: activeBrandId ?? undefined, pageSize: 100 })
  const franchises = useMemo(() => franchisesQ.data?.list ?? [], [franchisesQ.data])

  const { data, isLoading, isError, error, refetch } = useRoyaltyInvoices({ pageSize: 100 })

  const invoices = useMemo(() => data?.list ?? [], [data])

  const [generateOpen, setGenerateOpen]       = useState(false)
  const [selectedInvoice, setSelectedInvoice] = useState<RoyaltyInvoiceDto | null>(null)

  // Build franchise name lookup for the table (franchiseId → legalName).
  const franchiseMap = useMemo(
    () => Object.fromEntries(franchises.map((f) => [f.id, `${f.legalName} (${f.code})`])),
    [franchises],
  )

  const columns: Column<RoyaltyInvoiceDto>[] = baseColumns.map((col) =>
    col.sortKey === 'franchise'
      ? {
          ...col,
          accessor: (r: RoyaltyInvoiceDto) =>
            franchiseMap[r.franchiseId] ?? (
              <span className="font-mono text-xs text-gray-400">{r.franchiseId.slice(0, 8)}…</span>
            ),
          sortAccessor: (r: RoyaltyInvoiceDto) => franchiseMap[r.franchiseId] ?? r.franchiseId,
        }
      : col,
  )

  const filters: FilterDef<RoyaltyInvoiceDto>[] = [
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (r) => r.status,
      options: STATUSES.map((s) => ({ value: s, label: s.replace(/_/g, ' ') })),
    },
    {
      key: 'franchise',
      allLabel: 'All franchises',
      value: (r) => r.franchiseId,
      options: franchises.map((f) => ({
        value: f.id,
        label: `${f.legalName} (${f.code})`,
      })),
    },
  ]

  return (
    <div>
      <PageHeader
        title="Royalty invoices"
        description="Monthly royalty billing for each franchise, auto-generated on the 1st."
        action={
          canManage ? (
            <button
              type="button"
              onClick={() => setGenerateOpen(true)}
              className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
            >
              <Plus className="h-4 w-4" /> Generate
            </button>
          ) : undefined
        }
      />

      <Card className="overflow-hidden">
        {isLoading ? (
          <LoadingState message="Loading royalty invoices…" />
        ) : isError ? (
          isForbiddenError(error) ? <ForbiddenState /> : <ErrorState error={error as Error} onRetry={() => void refetch()} />
        ) : (
          <FilterableTable
            columns={columns}
            data={invoices}
            keyFn={(r) => r.id}
            unit="invoice"
            totalCount={data?.totalCount}
            searchPlaceholder="Search invoice number…"
            searchAccessor={(r) => `${r.invoiceNumber} ${r.franchiseId}`}
            filters={filters}
            initialSort={{ key: 'period', dir: 'desc' }}
            emptyMessage="No royalty invoices found."
            noMatchMessage="No invoices match your filters."
            onRowClick={(r) => setSelectedInvoice(r)}
          />
        )}
      </Card>

      <GenerateRoyaltyDrawer open={generateOpen} onClose={() => setGenerateOpen(false)} />

      <RoyaltyDetailDrawer
        invoice={selectedInvoice}
        onClose={() => setSelectedInvoice(null)}
        canManage={canManage}
      />
    </div>
  )
}
