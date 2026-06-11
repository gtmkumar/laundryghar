import { useMemo, useState } from 'react'
import { Plus, Check, Ban, Banknote } from 'lucide-react'
import { useExpenses, useExpenseAction } from '@/hooks/useFinance'
import { usePermissions } from '@/hooks/usePermissions'
import { AddExpenseDrawer, RejectExpenseDrawer } from './FinanceDrawers'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { ActionMenu, ActionMenuItem } from '@/components/ui/ActionMenu'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable, type FilterDef } from '@/components/shared/FilterableTable'
import { ConfirmDialog, useConfirm } from '@/components/shared/ConfirmDialog'
import { showToast } from '@/stores/toastStore'
import type { ExpenseDto } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

const PAYMENT_MODES = ['cash', 'upi', 'card', 'bank_transfer', 'cheque', 'credit']

function ExpenseStatusBadge({ status }: { status: string }) {
  const variant =
    status === 'paid' || status === 'reconciled' || status === 'approved'
      ? 'success'
      : status === 'rejected' || status === 'disputed'
        ? 'destructive'
        : status === 'draft'
          ? 'secondary'
          : 'warning'
  return (
    <Badge variant={variant} className="capitalize">
      {status.replace(/_/g, ' ')}
    </Badge>
  )
}

function distinctOptions(rows: ExpenseDto[], read: (r: ExpenseDto) => string | null) {
  const seen = new Set<string>()
  for (const r of rows) {
    const v = read(r)
    if (v) seen.add(v)
  }
  return [...seen].sort().map((v) => ({ value: v, label: v.replace(/_/g, ' ') }))
}

const baseColumns: Column<ExpenseDto>[] = [
  { header: 'Number', accessor: 'expenseNumber', className: 'font-mono text-xs w-28', sortKey: 'number' },
  {
    header: 'Date',
    accessor: (r) => formatDate(r.expenseDate),
    sortKey: 'date',
    sortAccessor: (r) => r.expenseDate,
  },
  { header: 'Category', accessor: 'categoryName', sortKey: 'category' },
  {
    header: 'Vendor',
    accessor: (r) => r.vendorName ?? <span className="text-gray-400">—</span>,
    sortKey: 'vendor',
    sortAccessor: (r) => r.vendorName ?? '',
  },
  {
    header: 'Amount',
    accessor: (r) => <span className="tabular-nums">{formatCurrency(r.totalAmount ?? r.amount)}</span>,
    className: 'text-right',
    sortKey: 'amount',
    sortAccessor: (r) => r.totalAmount ?? r.amount,
  },
  {
    header: 'Mode',
    accessor: (r) => <span className="capitalize">{r.paymentMode.replace(/_/g, ' ')}</span>,
    sortKey: 'mode',
    sortAccessor: (r) => r.paymentMode,
  },
  {
    header: 'Status',
    accessor: (r) => <ExpenseStatusBadge status={r.status} />,
    sortKey: 'status',
    sortAccessor: (r) => r.status,
  },
]

export function ExpensesPage() {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('expense.manage')
  const canApprove = hasPermission('expense.approve')
  const { data, isLoading, isError, error, refetch } = useExpenses({ pageSize: 100 })
  const { approve, markPaid } = useExpenseAction()

  const expenses = useMemo(() => data?.list ?? [], [data])
  const total = data?.totalCount

  const [addOpen, setAddOpen] = useState(false)
  const [rejecting, setRejecting] = useState<ExpenseDto | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const gate = useConfirm()

  const runAction = async (id: string, fn: () => Promise<unknown>) => {
    setBusyId(id)
    try {
      await fn()
    } catch (e) {
      showToast('error', e instanceof Error ? e.message : 'Could not complete the action.')
    } finally {
      setBusyId(null)
    }
  }

  const columns: Column<ExpenseDto>[] = [
    ...baseColumns,
    ...(canApprove || canManage
      ? [
          {
            header: '',
            className: 'w-12 text-right',
            accessor: (r: ExpenseDto) => {
              const canAct =
                (canApprove && r.status === 'submitted') ||
                (canManage && r.status === 'approved')
              if (!canAct) return null
              return (
                <div onClick={(e) => e.stopPropagation()}>
                  <ActionMenu busy={busyId === r.id} label="Expense actions">
                    {(close) => (
                      <>
                        {canApprove && r.status === 'submitted' && (
                          <>
                            <ActionMenuItem
                              icon={Check}
                              onClick={() => {
                                close()
                                gate.confirm({
                                  title: 'Approve expense?',
                                  description: `Approve ${r.expenseNumber} for ${formatCurrency(r.totalAmount ?? r.amount)} (${r.categoryName})?`,
                                  confirmLabel: 'Approve',
                                  tone: 'default',
                                  onConfirm: () => runAction(r.id, () => approve.mutateAsync({ id: r.id })),
                                })
                              }}
                            >
                              Approve
                            </ActionMenuItem>
                            <ActionMenuItem icon={Ban} danger onClick={() => { close(); setRejecting(r) }}>
                              Reject
                            </ActionMenuItem>
                          </>
                        )}
                        {canManage && r.status === 'approved' && (
                          <ActionMenuItem
                            icon={Banknote}
                            onClick={() => {
                              close()
                              gate.confirm({
                                title: 'Mark expense as paid?',
                                description: `This records ${formatCurrency(r.totalAmount ?? r.amount)} as paid for ${r.expenseNumber} (${r.categoryName}). This affects the books.`,
                                confirmLabel: 'Mark paid',
                                tone: 'warning',
                                onConfirm: () => runAction(r.id, () => markPaid.mutateAsync({ id: r.id })),
                              })
                            }}
                          >
                            Mark paid
                          </ActionMenuItem>
                        )}
                      </>
                    )}
                  </ActionMenu>
                </div>
              )
            },
          } as Column<ExpenseDto>,
        ]
      : []),
  ]

  const filters: FilterDef<ExpenseDto>[] = [
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (e) => e.status,
      options: distinctOptions(expenses, (e) => e.status),
    },
    {
      key: 'mode',
      allLabel: 'All payment modes',
      value: (e) => e.paymentMode,
      options: PAYMENT_MODES.map((m) => ({ value: m, label: m.replace(/_/g, ' ') })),
    },
    {
      key: 'category',
      allLabel: 'All categories',
      value: (e) => e.categoryName,
      options: distinctOptions(expenses, (e) => e.categoryName),
    },
  ]

  return (
    <div>
      <PageHeader
        title="Expenses"
        description="Track operational expenses across stores and franchises."
        action={
          canManage ? (
            <button
              type="button"
              onClick={() => setAddOpen(true)}
              className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
            >
              <Plus className="h-4 w-4" /> Add expense
            </button>
          ) : undefined
        }
      />
      <Card className="overflow-hidden">
        {isLoading ? (
          <LoadingState message="Loading expenses..." />
        ) : isError ? (
          isForbiddenError(error) ? <ForbiddenState /> : <ErrorState error={error as Error} onRetry={() => void refetch()} />
        ) : (
          <FilterableTable
            columns={columns}
            data={expenses}
            keyFn={(r) => r.id}
            unit="expense"
            totalCount={total}
            searchPlaceholder="Search number, vendor, category, description…"
            searchAccessor={(e) =>
              `${e.expenseNumber} ${e.categoryName} ${e.vendorName ?? ''} ${e.description}`
            }
            filters={filters}
            initialSort={{ key: 'date', dir: 'desc' }}
            csvExport={{
              filename: 'expenses',
              columns: [
                { header: 'Number', value: (r) => r.expenseNumber },
                { header: 'Date', value: (r) => r.expenseDate },
                { header: 'Category', value: (r) => r.categoryName },
                { header: 'Vendor', value: (r) => r.vendorName ?? '' },
                { header: 'Amount', value: (r) => r.totalAmount ?? r.amount },
                { header: 'Mode', value: (r) => r.paymentMode },
                { header: 'Status', value: (r) => r.status },
                { header: 'Description', value: (r) => r.description },
              ],
            }}
            emptyMessage="No expenses found."
            noMatchMessage="No expenses match your filters."
          />
        )}
      </Card>

      <AddExpenseDrawer open={addOpen} onClose={() => setAddOpen(false)} />
      <RejectExpenseDrawer expense={rejecting} onClose={() => setRejecting(null)} />
      <ConfirmDialog {...gate.dialogProps} />
    </div>
  )
}
