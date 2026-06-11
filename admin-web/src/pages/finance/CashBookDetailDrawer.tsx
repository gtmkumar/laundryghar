import { useMemo, useState } from 'react'
import { BookOpen, Lock, ArrowLeftRight, Loader2 } from 'lucide-react'
import { usePermissions } from '@/hooks/usePermissions'
import { useCashBook, useCloseCashBook, useCreateShiftHandover } from '@/hooks/useFinance'
import { useAuthStore } from '@/stores/authStore'
import { showToast } from '@/stores/toastStore'
import {
  FormDrawer,
  DrawerSection,
  Field,
  drawerInputCls,
  DetailSection,
  DetailRow,
} from '@/components/shared/FormDrawer'
import { ConfirmDialog, useConfirm } from '@/components/shared/ConfirmDialog'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Badge } from '@/components/ui/badge'
import type { CashBookEntryDto } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

function StatusBadge({ status }: { status: string }) {
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

function money(n: number | null | undefined) {
  return n == null ? <span className="text-gray-400">—</span> : <span className="tabular-nums">{formatCurrency(n)}</span>
}

function EntryRow({ entry }: { entry: CashBookEntryDto }) {
  const isIn = entry.direction > 0
  return (
    <div className="flex items-center gap-3 px-3 py-2 text-sm">
      <span
        className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs font-bold ${
          isIn ? 'bg-emerald-50 text-emerald-700' : 'bg-red-50 text-red-700'
        }`}
      >
        {isIn ? '+' : '−'}
      </span>
      <div className="min-w-0 flex-1">
        <p className="truncate font-medium text-gray-800 capitalize">
          {entry.category.replace(/_/g, ' ')}
          <span className="ml-1 text-xs font-normal text-gray-400">· {entry.paymentMode}</span>
        </p>
        {entry.description && <p className="truncate text-xs text-gray-400">{entry.description}</p>}
      </div>
      <span className={`shrink-0 tabular-nums font-semibold ${isIn ? 'text-emerald-700' : 'text-red-700'}`}>
        {isIn ? '+' : '−'}
        {formatCurrency(entry.amount)}
      </span>
    </div>
  )
}

/**
 * Cash-book detail + close (reconcile) drawer. Renders the full ledger (line
 * entries), the expected-vs-counted summary, and — when the book is `open` and
 * the user has `cashbook.manage` — a Close form that captures the physically
 * counted closing balance + a variance reason. The backend derives the variance
 * from (opening + inflows − outflows) vs the counted closing.
 *
 * Note: the backend only supports the open → closed transition. Finalize /
 * dispute are statuses but have no admin endpoint yet, so they are shown
 * read-only and not actionable from here.
 */
export function CashBookDetailDrawer({ id, onClose }: { id: string; onClose: () => void }) {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('cashbook.manage')
  const { data, isLoading, isError, error, refetch } = useCashBook(id)
  const close = useCloseCashBook()
  const gate = useConfirm()

  const [counted, setCounted] = useState('')
  const [reason, setReason] = useState('')
  const [notes, setNotes] = useState('')
  const [formError, setFormError] = useState<string | null>(null)
  const [handoverOpen, setHandoverOpen] = useState(false)

  // Expected closing = opening + all inflows − outflow (mirrors the backend).
  const expected = useMemo(() => {
    if (!data) return null
    return (
      data.openingBalance +
      data.cashInflow +
      data.upiInflow +
      data.cardInflow +
      data.otherInflow -
      data.cashOutflow
    )
  }, [data])

  const countedNum = counted.trim() === '' ? null : Number(counted)
  const liveVariance = countedNum != null && expected != null && !Number.isNaN(countedNum) ? countedNum - expected : null
  const requiresReason = liveVariance != null && Math.abs(liveVariance) > 0.005

  const isOpen = data?.status === 'open'

  const submitClose = () => {
    setFormError(null)
    if (countedNum == null || Number.isNaN(countedNum) || countedNum < 0) {
      setFormError('Enter the counted closing cash (0 or greater).')
      return
    }
    if (requiresReason && reason.trim().length === 0) {
      setFormError('A variance reason is required when counted cash differs from expected.')
      return
    }
    gate.confirm({
      title: 'Close cash book?',
      description: `Reconcile this book at a counted closing of ${formatCurrency(countedNum)}${
        liveVariance != null && Math.abs(liveVariance) > 0.005
          ? ` (variance ${formatCurrency(liveVariance)})`
          : ''
      }. Once closed it can no longer accept entries.`,
      confirmLabel: 'Close book',
      tone: requiresReason ? 'warning' : 'default',
      onConfirm: async () => {
        try {
          await close.mutateAsync({
            id,
            payload: {
              closingBalance: countedNum,
              varianceReason: reason.trim() || undefined,
              notes: notes.trim() || undefined,
            },
          })
          showToast('success', 'Cash book closed.')
        } catch (e) {
          const msg = e instanceof Error ? e.message : 'Could not close the cash book.'
          setFormError(msg)
          showToast('error', msg)
        }
      },
    })
  }

  return (
    <FormDrawer
      open
      onClose={onClose}
      icon={BookOpen}
      eyebrow="Cash book"
      title={data ? `${formatDate(data.bookDate)} · ${data.shiftLabel.replace(/_/g, ' ')}` : 'Cash book'}
      width="lg"
      headerAction={
        data ? (
          <div className="flex items-center gap-2">
            {canManage && (
              <button
                type="button"
                onClick={() => setHandoverOpen(true)}
                className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-2.5 py-1.5 text-xs font-medium text-gray-600 hover:bg-gray-50"
              >
                <ArrowLeftRight className="h-3.5 w-3.5" /> Handover
              </button>
            )}
            <StatusBadge status={data.status} />
          </div>
        ) : undefined
      }
      footer={null}
    >
      {isLoading ? (
        <LoadingState message="Loading cash book…" />
      ) : isError ? (
        isForbiddenError(error) ? (
          <ForbiddenState />
        ) : (
          <ErrorState error={error as Error} onRetry={() => void refetch()} />
        )
      ) : data ? (
        <>
          {/* Summary */}
          <DetailSection title="Reconciliation">
            <DetailRow label="Opening balance" value={money(data.openingBalance)} />
            <DetailRow label="Cash inflow" value={money(data.cashInflow)} />
            <DetailRow label="UPI / card / other" value={money(data.upiInflow + data.cardInflow + data.otherInflow)} />
            <DetailRow label="Cash outflow" value={money(data.cashOutflow)} />
            <DetailRow
              label="Expected closing"
              value={<span className="font-semibold">{money(data.expectedClosing ?? expected)}</span>}
            />
            {data.closingBalance != null && (
              <DetailRow label="Counted closing" value={money(data.closingBalance)} />
            )}
            {data.variance != null && (
              <DetailRow
                label="Variance"
                value={
                  <span className={`tabular-nums font-semibold ${data.variance < 0 ? 'text-red-600' : data.variance > 0 ? 'text-amber-600' : 'text-emerald-700'}`}>
                    {formatCurrency(data.variance)}
                  </span>
                }
              />
            )}
          </DetailSection>

          {data.notes && (
            <DetailSection title="Notes">
              <DetailRow label="Reconciliation notes" value={data.notes} />
            </DetailSection>
          )}

          {/* Ledger entries */}
          <DrawerSection title={`Ledger entries (${data.entries.length})`}>
            {data.entries.length === 0 ? (
              <p className="text-sm text-gray-400">No entries recorded.</p>
            ) : (
              <div className="divide-y divide-gray-50 rounded-xl border border-gray-100">
                {data.entries.map((e) => (
                  <EntryRow key={e.id} entry={e} />
                ))}
              </div>
            )}
          </DrawerSection>

          {/* Close form — only for open books, manage permission */}
          {isOpen && canManage && (
            <DrawerSection title="Close & reconcile">
              <p className="text-xs text-gray-400">
                Count the physical cash in the drawer and enter the closing total. The system compares it against the
                expected closing of <span className="font-semibold text-gray-700">{formatCurrency(data.expectedClosing ?? expected ?? 0)}</span>.
              </p>
              <Field label="Counted closing cash">
                <input
                  type="number"
                  inputMode="decimal"
                  min={0}
                  step="0.01"
                  value={counted}
                  onChange={(e) => setCounted(e.target.value)}
                  className={drawerInputCls}
                  placeholder="0.00"
                />
              </Field>
              {liveVariance != null && (
                <div
                  className={`rounded-lg px-3 py-2 text-sm ${
                    Math.abs(liveVariance) <= 0.005
                      ? 'bg-emerald-50 text-emerald-700'
                      : liveVariance < 0
                        ? 'bg-red-50 text-red-700'
                        : 'bg-amber-50 text-amber-700'
                  }`}
                >
                  Variance: <span className="font-semibold tabular-nums">{formatCurrency(liveVariance)}</span>
                  {Math.abs(liveVariance) <= 0.005 ? ' — balanced' : liveVariance < 0 ? ' — short' : ' — over'}
                </div>
              )}
              <Field label={`Variance reason${requiresReason ? ' (required)' : ' (optional)'}`}>
                <input
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  className={drawerInputCls}
                  placeholder="e.g. ₹50 short — change given without receipt"
                />
              </Field>
              <Field label="Notes (optional)">
                <input value={notes} onChange={(e) => setNotes(e.target.value)} className={drawerInputCls} placeholder="Evening reconciliation" />
              </Field>
              {formError && (
                <p className="text-sm text-red-600">{formError}</p>
              )}
              <button
                type="button"
                onClick={submitClose}
                disabled={close.isPending}
                className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
              >
                {close.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Lock className="h-4 w-4" />}
                Close & reconcile
              </button>
            </DrawerSection>
          )}

          {isOpen && !canManage && (
            <p className="text-xs text-gray-400">You don’t have permission to close cash books (cashbook.manage).</p>
          )}

          {!isOpen && (
            <div className="flex items-center gap-2 rounded-xl bg-gray-50 px-4 py-3 text-sm text-gray-500">
              <Lock className="h-4 w-4 shrink-0" /> This cash book is {data.status.replace(/_/g, ' ')} and can no longer be edited.
            </div>
          )}

          <ConfirmDialog {...gate.dialogProps} />
          {handoverOpen && (
            <ShiftHandoverDrawer
              open={handoverOpen}
              onClose={() => setHandoverOpen(false)}
              storeId={data.storeId}
              cashBookId={data.id}
            />
          )}
        </>
      ) : null}
    </FormDrawer>
  )
}

// ── Shift handover drawer ───────────────────────────────────────────────────

/**
 * Records a shift handover: the outgoing operator hands the counted cash (and
 * the open-work snapshot) to the next shift. Optionally linked to the cash book
 * being closed. Permission-gated (cashbook.manage) at the call site.
 */
export function ShiftHandoverDrawer({
  open,
  onClose,
  storeId,
  cashBookId,
}: {
  open: boolean
  onClose: () => void
  storeId: string
  cashBookId?: string | null
}) {
  const create = useCreateShiftHandover()
  const fromUserId = useAuthStore((s) => s.user?.sub ?? null)

  const [cash, setCash] = useState('')
  const [pendingOrders, setPendingOrders] = useState('')
  const [openComplaints, setOpenComplaints] = useState('')
  const [pickups, setPickups] = useState('')
  const [deliveries, setDeliveries] = useState('')
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)

  const intOr0 = (v: string) => {
    const n = Number(v)
    return Number.isFinite(n) && n >= 0 ? Math.trunc(n) : 0
  }

  const submit = async () => {
    setError(null)
    const cashNum = Number(cash)
    if (cash.trim() === '' || Number.isNaN(cashNum) || cashNum < 0) {
      setError('Enter the cash handed over (0 or greater).')
      return
    }
    if (!fromUserId) {
      setError('Could not determine the current user. Please re-login.')
      return
    }
    try {
      await create.mutateAsync({
        storeId,
        fromUserId,
        cashHandedOver: cashNum,
        pendingOrdersCount: intOr0(pendingOrders),
        openComplaintsCount: intOr0(openComplaints),
        pickupsRemaining: intOr0(pickups),
        deliveriesRemaining: intOr0(deliveries),
        notesFrom: notes.trim() || undefined,
        cashBookId: cashBookId ?? undefined,
      })
      showToast('success', 'Shift handover recorded.')
      onClose()
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Could not record the handover.'
      setError(msg)
      showToast('error', msg)
    }
  }

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={ArrowLeftRight}
      eyebrow="Cash book"
      title="Shift handover"
      width="md"
      elevated
      error={error}
      onSubmit={submit}
      submitLabel="Record handover"
      submittingLabel="Recording…"
      submitting={create.isPending}
      submitIcon={ArrowLeftRight}
    >
      <DrawerSection title="Cash">
        <Field label="Cash handed over">
          <input
            type="number"
            inputMode="decimal"
            min={0}
            step="0.01"
            value={cash}
            onChange={(e) => setCash(e.target.value)}
            className={drawerInputCls}
            placeholder="0.00"
          />
        </Field>
      </DrawerSection>

      <DrawerSection title="Open work snapshot">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Pending orders">
            <input type="number" min={0} value={pendingOrders} onChange={(e) => setPendingOrders(e.target.value)} className={drawerInputCls} placeholder="0" />
          </Field>
          <Field label="Open complaints">
            <input type="number" min={0} value={openComplaints} onChange={(e) => setOpenComplaints(e.target.value)} className={drawerInputCls} placeholder="0" />
          </Field>
          <Field label="Pickups remaining">
            <input type="number" min={0} value={pickups} onChange={(e) => setPickups(e.target.value)} className={drawerInputCls} placeholder="0" />
          </Field>
          <Field label="Deliveries remaining">
            <input type="number" min={0} value={deliveries} onChange={(e) => setDeliveries(e.target.value)} className={drawerInputCls} placeholder="0" />
          </Field>
        </div>
      </DrawerSection>

      <Field label="Handover notes (optional)">
        <textarea
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          rows={3}
          className={`${drawerInputCls} resize-none`}
          placeholder="Anything the next shift should know…"
        />
      </Field>
    </FormDrawer>
  )
}
