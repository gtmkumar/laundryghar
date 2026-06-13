import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Check, Ban, Banknote, Bike } from 'lucide-react'
import { usePayoutRequests, useReviewPayout, useMarkPayoutPaid } from '@/hooks/useRiderPayouts'
import { usePermissions } from '@/hooks/usePermissions'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { ActionMenu, ActionMenuItem } from '@/components/ui/ActionMenu'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable } from '@/components/shared/FilterableTable'
import { ConfirmDialog, useConfirm } from '@/components/shared/ConfirmDialog'
import { cn } from '@/lib/utils'
import { showToast } from '@/stores/toastStore'
import type { PayoutRequestStatus, RiderPayoutRequestDto } from '@/types/api'
import { formatCurrency, formatDateTime } from '@/lib/utils'

// The backend returns one status bucket at a time, so the status filter drives
// the query (not an in-memory FilterableTable filter). Default to the live queue.
const STATUS_TABS: { key: PayoutRequestStatus; label: string }[] = [
  { key: 'requested', label: 'Requested' },
  { key: 'approved', label: 'Approved' },
  { key: 'paid', label: 'Paid' },
  { key: 'rejected', label: 'Rejected' },
]

function PayoutStatusBadge({ status }: { status: PayoutRequestStatus }) {
  const variant =
    status === 'paid'
      ? 'success'
      : status === 'rejected'
        ? 'destructive'
        : status === 'approved'
          ? 'default'
          : 'warning'
  return (
    <Badge variant={variant} className="capitalize">
      {status}
    </Badge>
  )
}

export function RiderPayoutsPage() {
  const { hasPermission } = usePermissions()
  const canSettle = hasPermission('rider.settle')

  const [status, setStatus] = useState<PayoutRequestStatus>('requested')
  const { data, isLoading, isError, error, refetch } = usePayoutRequests(status)
  const requests = useMemo(() => data ?? [], [data])

  const review = useReviewPayout()
  const markPaid = useMarkPayoutPaid()
  const gate = useConfirm()
  const [busyId, setBusyId] = useState<string | null>(null)

  const runAction = async (id: string, fn: () => Promise<unknown>, okMsg: string) => {
    setBusyId(id)
    try {
      await fn()
      showToast('success', okMsg)
    } catch (e) {
      showToast('error', e instanceof Error ? e.message : 'Could not complete the action.')
    } finally {
      setBusyId(null)
    }
  }

  const approve = (r: RiderPayoutRequestDto) =>
    gate.confirm({
      title: 'Approve payout?',
      description: `Approve ${formatCurrency(r.amount)} for ${r.riderName}. They can be paid out next.`,
      confirmLabel: 'Approve',
      tone: 'default',
      onConfirm: () =>
        runAction(
          r.id,
          () => review.mutateAsync({ id: r.id, action: 'approve' }),
          'Payout approved.',
        ),
    })

  const reject = (r: RiderPayoutRequestDto) =>
    gate.confirm({
      title: 'Reject payout?',
      description: `Reject ${r.riderName}'s ${formatCurrency(r.amount)} request. Give a reason the rider will see.`,
      confirmLabel: 'Reject',
      tone: 'danger',
      requireReason: true,
      reasonLabel: 'Reason for rejection',
      reasonPlaceholder: 'e.g. amount exceeds cleared earnings',
      onConfirm: (reason) =>
        runAction(
          r.id,
          () => review.mutateAsync({ id: r.id, action: 'reject', reason }),
          'Payout rejected.',
        ),
    })

  const markPaidAction = (r: RiderPayoutRequestDto) =>
    gate.confirm({
      title: 'Mark payout as paid?',
      description: `Record ${formatCurrency(r.amount)} disbursed to ${r.riderName}. This posts to the cash book and cannot be undone.`,
      confirmLabel: 'Mark paid',
      tone: 'warning',
      requireReason: true,
      reasonLabel: 'Payment reference',
      reasonPlaceholder: 'e.g. UPI txn id / bank UTR',
      onConfirm: (reference) =>
        runAction(
          r.id,
          () => markPaid.mutateAsync({ id: r.id, reference: reference ?? '' }),
          'Payout marked paid.',
        ),
    })

  const columns: Column<RiderPayoutRequestDto>[] = [
    {
      header: 'Rider',
      accessor: (r) => <span className="font-medium text-gray-900">{r.riderName}</span>,
      sortKey: 'rider',
      sortAccessor: (r) => r.riderName,
    },
    {
      header: 'Amount',
      accessor: (r) => <span className="tabular-nums">{formatCurrency(r.amount)}</span>,
      className: 'text-right',
      sortKey: 'amount',
      sortAccessor: (r) => r.amount,
    },
    {
      header: 'Status',
      accessor: (r) => <PayoutStatusBadge status={r.status} />,
      sortKey: 'status',
      sortAccessor: (r) => r.status,
    },
    {
      header: 'Requested',
      accessor: (r) => formatDateTime(r.requestedAt),
      sortKey: 'requested',
      sortAccessor: (r) => r.requestedAt,
    },
    {
      header: 'Reviewed',
      accessor: (r) =>
        r.reviewedAt ? formatDateTime(r.reviewedAt) : <span className="text-gray-400">—</span>,
      sortKey: 'reviewed',
      sortAccessor: (r) => r.reviewedAt ?? '',
    },
    {
      header: 'Paid',
      accessor: (r) =>
        r.paidAt ? (
          <div>
            <span>{formatDateTime(r.paidAt)}</span>
            {r.paymentReference && (
              <span className="block text-xs text-gray-400">{r.paymentReference}</span>
            )}
          </div>
        ) : r.status === 'rejected' && r.rejectionReason ? (
          <span className="text-xs text-gray-400">{r.rejectionReason}</span>
        ) : (
          <span className="text-gray-400">—</span>
        ),
      sortKey: 'paid',
      sortAccessor: (r) => r.paidAt ?? '',
    },
    ...(canSettle
      ? [
          {
            header: '',
            className: 'w-12 text-right',
            accessor: (r: RiderPayoutRequestDto) => {
              const canAct = r.status === 'requested' || r.status === 'approved'
              if (!canAct) return null
              return (
                <div onClick={(e) => e.stopPropagation()}>
                  <ActionMenu busy={busyId === r.id} label="Payout actions" width={176}>
                    {(close) => (
                      <>
                        {r.status === 'requested' && (
                          <>
                            <ActionMenuItem
                              icon={Check}
                              onClick={() => {
                                close()
                                approve(r)
                              }}
                            >
                              Approve
                            </ActionMenuItem>
                            <ActionMenuItem
                              icon={Ban}
                              danger
                              onClick={() => {
                                close()
                                reject(r)
                              }}
                            >
                              Reject
                            </ActionMenuItem>
                          </>
                        )}
                        {r.status === 'approved' && (
                          <ActionMenuItem
                            icon={Banknote}
                            onClick={() => {
                              close()
                              markPaidAction(r)
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
          } as Column<RiderPayoutRequestDto>,
        ]
      : []),
  ]

  return (
    <div>
      <PageHeader
        title="Rider payouts"
        description="Review and settle rider withdrawal requests."
        action={
          <Link
            to="/riders"
            className="inline-flex items-center gap-1.5 rounded-xl border border-gray-200 px-4 py-2.5 text-sm font-semibold text-gray-700 hover:bg-gray-50"
          >
            <Bike className="h-4 w-4" /> Riders
          </Link>
        }
      />

      {/* Status tabs — drive the query (server returns one bucket at a time). */}
      <div className="mb-4 flex w-fit items-center gap-1 rounded-xl border border-gray-200 bg-white p-1">
        {STATUS_TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setStatus(t.key)}
            className={cn(
              'rounded-lg px-3.5 py-1.5 text-sm font-medium transition-colors',
              status === t.key ? 'bg-lg-green text-white' : 'text-gray-600 hover:bg-gray-50',
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      <Card className="overflow-hidden">
        {isLoading ? (
          <LoadingState message="Loading payout requests..." />
        ) : isError ? (
          isForbiddenError(error) ? (
            <ForbiddenState />
          ) : (
            <ErrorState error={error as Error} onRetry={() => void refetch()} />
          )
        ) : (
          <FilterableTable
            columns={columns}
            data={requests}
            keyFn={(r) => r.id}
            unit="request"
            searchPlaceholder="Search rider or reference…"
            searchAccessor={(r) => `${r.riderName} ${r.paymentReference ?? ''} ${r.rejectionReason ?? ''}`}
            initialSort={{ key: 'requested', dir: 'desc' }}
            csvExport={{
              filename: `rider-payouts-${status}`,
              columns: [
                { header: 'Rider', value: (r) => r.riderName },
                { header: 'Amount', value: (r) => r.amount },
                { header: 'Status', value: (r) => r.status },
                { header: 'Requested', value: (r) => r.requestedAt },
                { header: 'Reviewed', value: (r) => r.reviewedAt ?? '' },
                { header: 'Paid', value: (r) => r.paidAt ?? '' },
                { header: 'Reference', value: (r) => r.paymentReference ?? '' },
                { header: 'Rejection reason', value: (r) => r.rejectionReason ?? '' },
              ],
            }}
            emptyMessage={emptyMessageFor(status)}
            noMatchMessage="No requests match your search."
          />
        )}
      </Card>

      <ConfirmDialog {...gate.dialogProps} />
    </div>
  )
}

function emptyMessageFor(status: PayoutRequestStatus): string {
  switch (status) {
    case 'requested':
      return 'No payout requests awaiting review.'
    case 'approved':
      return 'No approved payouts pending disbursement.'
    case 'paid':
      return 'No payouts have been paid yet.'
    case 'rejected':
      return 'No rejected payouts.'
  }
}
