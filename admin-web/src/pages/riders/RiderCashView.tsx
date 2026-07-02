import { useMemo, useState } from 'react'
import { Loader2, Wallet, BadgeIndianRupee, Receipt, AlertTriangle, Check } from 'lucide-react'
import { usePermissions } from '@/hooks/usePermissions'
import { useCodOutstanding, useRiderCod, useRiderSettlements, useSettleRider } from '@/hooks/useRiders'
import { FormDrawer, DrawerSection, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import { useConfirm } from '@/components/shared/useConfirm'
import type { RiderCodSummary } from '@/types/api'
import { formatDate } from './riderShared'

const inr = (n: number) => `₹${n.toLocaleString('en-IN', { maximumFractionDigits: 2 })}`

export function RiderCashView() {
  const { data: rows = [], isLoading, isError } = useCodOutstanding()
  const [openRider, setOpenRider] = useState<RiderCodSummary | null>(null)

  const total = useMemo(() => rows.reduce((s, r) => s + r.outstandingAmount, 0), [rows])

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-24 text-gray-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading cash reconciliation…
      </div>
    )
  }
  if (isError) return <div className="py-24 text-center text-sm text-red-600">Couldn’t load COD cash.</div>

  return (
    <div className="space-y-4">
      {/* Summary */}
      <div className="flex flex-wrap items-center gap-4 rounded-2xl border border-gray-200 bg-white px-5 py-4">
        <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
          <Wallet className="h-5 w-5" />
        </span>
        <div>
          <p className="text-xs font-medium text-gray-400">Outstanding COD cash</p>
          <p className="text-2xl font-bold text-gray-900">{inr(total)}</p>
        </div>
        <div className="ml-auto text-right">
          <p className="text-xs font-medium text-gray-400">Riders holding cash</p>
          <p className="text-2xl font-bold text-gray-900">{rows.length}</p>
        </div>
      </div>

      {rows.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-gray-200 bg-white py-20 text-center text-sm text-gray-400">
          No outstanding COD cash — everyone is settled up. 🎉
        </div>
      ) : (
        <div className="overflow-hidden rounded-2xl border border-gray-200 bg-white">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-100 text-left text-[11px] font-semibold uppercase tracking-wide text-gray-400">
                <th className="px-5 py-3">Rider</th>
                <th className="px-5 py-3">Franchise</th>
                <th className="px-5 py-3 text-right">Collections</th>
                <th className="px-5 py-3">Oldest</th>
                <th className="px-5 py-3 text-right">Outstanding</th>
                <th className="px-5 py-3 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.riderId} className="border-b border-gray-50 last:border-0 hover:bg-gray-50/60">
                  <td className="px-5 py-3">
                    <p className="font-medium text-gray-900">{r.riderName ?? r.riderCode}</p>
                    <p className="text-xs text-gray-400">{r.riderCode}</p>
                  </td>
                  <td className="px-5 py-3 text-gray-600">{r.franchiseName ?? '—'}</td>
                  <td className="px-5 py-3 text-right text-gray-600">{r.unclearedCount}</td>
                  <td className="px-5 py-3 text-gray-500">{formatDate(r.oldestCollectedAt ?? null)}</td>
                  <td className="px-5 py-3 text-right font-bold text-gray-900">{inr(r.outstandingAmount)}</td>
                  <td className="px-5 py-3 text-right">
                    <button
                      type="button"
                      onClick={() => setOpenRider(r)}
                      className="rounded-lg border border-gray-200 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
                    >
                      View &amp; settle
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {openRider && <CashDrawer rider={openRider} onClose={() => setOpenRider(null)} />}
    </div>
  )
}

function CashDrawer({ rider, onClose }: { rider: RiderCodSummary; onClose: () => void }) {
  const { hasPermission } = usePermissions()
  const canSettle = hasPermission('rider.settle')
  const detail = useRiderCod(rider.riderId)
  const history = useRiderSettlements(rider.riderId)
  const settle = useSettleRider()
  const gate = useConfirm()

  const [reference, setReference] = useState('')
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  const outstanding = detail.data?.outstandingAmount ?? rider.outstandingAmount
  const count = detail.data?.outstandingCount ?? rider.unclearedCount

  const submit = () => {
    setError(null)
    gate.confirm({
      title: `Settle ${inr(outstanding)}?`,
      description: `Records that ${rider.riderName ?? rider.riderCode} handed over ${inr(outstanding)} and clears all ${count} collection${count === 1 ? '' : 's'}. This posts to the cash book and cannot be undone.`,
      confirmLabel: `Mark settled`,
      tone: 'warning',
      onConfirm: async () => {
        try {
          await settle.mutateAsync({
            id: rider.riderId,
            payload: { reference: reference.trim() || undefined, notes: notes.trim() || undefined },
          })
          setDone(true)
        } catch (e) {
          setError(e instanceof Error ? e.message : 'Could not record the settlement.')
        }
      },
    })
  }

  return (
    <FormDrawer
      open
      onClose={onClose}
      icon={Wallet}
      eyebrow="Cash reconciliation"
      title={rider.riderName ?? rider.riderCode}
      width="md"
    >
      {/* Outstanding */}
      <div className="rounded-2xl border border-gray-200 p-4">
            <div className="flex items-center justify-between">
              <p className="text-xs font-medium text-gray-400">Outstanding cash</p>
              <BadgeIndianRupee className="h-4 w-4 text-gray-300" />
            </div>
            <p className="mt-1 text-2xl font-bold text-gray-900">{inr(outstanding)}</p>
            <p className="text-xs text-gray-400">{count} collection{count === 1 ? '' : 's'}</p>

            {detail.data && detail.data.collections.length > 0 && (
              <div className="mt-3 divide-y divide-gray-50 border-t border-gray-100">
                {detail.data.collections.map((c) => (
                  <div key={c.assignmentId} className="flex items-center justify-between py-2 text-sm">
                    <span className="text-gray-600">{c.orderNumber ?? 'Order'} · {formatDate(c.collectedAt)}</span>
                    <span className="font-medium text-gray-900">{inr(c.amount)}</span>
                  </div>
                ))}
              </div>
            )}
          </div>

      {/* Settle form */}
      {canSettle && outstanding > 0 && !done && (
        <DrawerSection title="Record settlement">
          <p className="text-xs text-gray-400">
            Records that this rider handed over <span className="font-semibold text-gray-700">{inr(outstanding)}</span> and
            clears all {count} collection{count === 1 ? '' : 's'}.
          </p>
          <Field label="Reference (deposit slip / txn)">
            <input value={reference} onChange={(e) => setReference(e.target.value)} className={drawerInputCls} placeholder="DEP-0001" />
          </Field>
          <Field label="Notes (optional)">
            <input value={notes} onChange={(e) => setNotes(e.target.value)} className={drawerInputCls} placeholder="Evening cash drop" />
          </Field>
          {error && (
            <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2.5 text-sm text-red-700">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" /><span>{error}</span>
            </div>
          )}
          <button
            type="button"
            onClick={submit}
            disabled={settle.isPending}
            className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
          >
            {settle.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
            Mark {inr(outstanding)} settled
          </button>
        </DrawerSection>
      )}

      {done && (
        <div className="flex items-center gap-2 rounded-xl bg-lg-green/10 px-4 py-3 text-sm font-medium text-lg-green">
          <Check className="h-4 w-4" /> Settlement recorded.
        </div>
      )}

      {!canSettle && (
        <p className="text-xs text-gray-400">You don’t have permission to record settlements (rider.settle).</p>
      )}

      {/* History */}
      <DrawerSection title="Settlement history">
        {history.isLoading ? (
          <p className="text-sm text-gray-400">Loading…</p>
        ) : (history.data?.list.length ?? 0) === 0 ? (
          <p className="text-sm text-gray-400">No settlements yet.</p>
        ) : (
          <div className="divide-y divide-gray-50 rounded-xl border border-gray-100">
            {history.data!.list.map((s) => (
              <div key={s.id} className="flex items-center gap-3 px-3 py-2.5 text-sm">
                <Receipt className="h-4 w-4 shrink-0 text-gray-300" />
                <div className="min-w-0 flex-1">
                  <p className="font-medium text-gray-900">{inr(s.totalAmount)} · {s.collectionCount} collection{s.collectionCount === 1 ? '' : 's'}</p>
                  <p className="truncate text-xs text-gray-400">
                    {formatDate(s.settledAt)}{s.reference ? ` · ${s.reference}` : ''}{s.storeName ? ` · ${s.storeName}` : ''}
                  </p>
                </div>
                <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium capitalize text-emerald-700">{s.status}</span>
              </div>
            ))}
          </div>
        )}
      </DrawerSection>
      <ConfirmDialog {...gate.dialogProps} />
    </FormDrawer>
  )
}
