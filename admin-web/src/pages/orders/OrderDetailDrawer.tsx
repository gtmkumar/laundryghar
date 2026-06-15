import { useState } from 'react'
import {
  Package,
  Loader2,
  FileText,
  Download,
  Plus,
  Trash2,
  XCircle,
  AlertTriangle,
  ArrowRight,
  Clock,
} from 'lucide-react'
import {
  FormDrawer,
  DetailSection,
  DetailRow,
  drawerInputCls,
} from '@/components/shared/FormDrawer'
import { Badge } from '@/components/ui/badge'
import { usePermissions } from '@/hooks/usePermissions'
import {
  useOrder,
  useOrderNotes,
  useCreateOrderNote,
  useDeleteOrderNote,
  useUpdateOrderStatus,
  useCancelOrder,
  useInvoice,
  useGenerateInvoice,
} from '@/hooks/useOrders'
import { downloadInvoicePdf } from '@/api/orders'
import { formatCurrency, formatDateTime } from '@/lib/utils'
import type { OrderDto } from '@/types/api'
import {
  advanceableTargets,
  canCancelFrom,
  invoiceAvailable,
  statusBadgeVariant,
  statusLabel,
} from './orderStatus'

// ── Permission codes (exactly what the backend endpoints demand) ──────────────
const PERM = {
  read: 'orders.read',
  statusUpdate: 'orders.status.update',
  cancel: 'orders.cancel',
  notesManage: 'orders.notes.manage',
  invoiceGenerate: 'orders.update', // POST /invoice gate per AdminInvoiceEndpoints
} as const

const NOTE_TYPES = ['internal', 'customer_facing', 'complaint', 'resolution', 'flag']
const NOTE_VISIBILITIES = ['staff', 'customer', 'platform']

function money(amount: number, currency: string) {
  return formatCurrency(amount, currency)
}

// ── Summary ───────────────────────────────────────────────────────────────────

function SummarySection({ order }: { order: OrderDto }) {
  const c = order.currencyCode
  return (
    <>
      <DetailSection title="Summary">
        <DetailRow label="Customer" value={<span className="font-mono text-xs">{order.customerId}</span>} />
        <DetailRow label="Store" value={<span className="font-mono text-xs">{order.storeId}</span>} />
        <DetailRow label="Channel" value={<span className="capitalize">{order.channel}</span>} />
        <DetailRow label="Type" value={<span className="capitalize">{order.orderType}</span>} />
        <DetailRow
          label="Payment"
          value={
            <Badge
              variant={
                order.paymentStatus === 'paid'
                  ? 'success'
                  : order.paymentStatus === 'pending'
                    ? 'warning'
                    : 'secondary'
              }
              className="capitalize"
            >
              {order.paymentStatus}
            </Badge>
          }
        />
        <DetailRow label="Placed" value={formatDateTime(order.placedAt)} />
        <DetailRow label="Last updated" value={formatDateTime(order.updatedAt)} />
        {order.promisedDeliveryAt && (
          <DetailRow
            label="Promised by"
            value={
              <span
                className={
                  new Date(order.promisedDeliveryAt) < new Date() &&
                  !['delivered', 'cancelled', 'closed', 'returned'].includes(order.status)
                    ? 'font-medium text-red-600'
                    : ''
                }
              >
                {formatDateTime(order.promisedDeliveryAt)}
              </span>
            }
          />
        )}
      </DetailSection>

      <DetailSection title="Totals">
        <DetailRow label="Subtotal" value={money(order.subtotal, c)} />
        <DetailRow label="Add-ons" value={money(order.addonTotal, c)} />
        {order.expressSurcharge > 0 && (
          <DetailRow label="Express surcharge" value={money(order.expressSurcharge, c)} />
        )}
        <DetailRow label={`Tax (CGST ${money(order.cgst, c)} + SGST ${money(order.sgst, c)})`} value={money(order.taxTotal, c)} />
        <DetailRow
          label={<span className="font-semibold text-gray-900">Grand total</span>}
          value={<span className="font-semibold">{money(order.grandTotal, c)}</span>}
        />
        <DetailRow label="Amount paid" value={money(order.amountPaid, c)} />
        {order.amountDue !== null && (
          <DetailRow
            label="Amount due"
            value={
              <span className={order.amountDue > 0 ? 'font-semibold text-red-600' : ''}>
                {money(order.amountDue, c)}
              </span>
            }
          />
        )}
      </DetailSection>
    </>
  )
}

// ── Items ─────────────────────────────────────────────────────────────────────

function ItemsSection({ order }: { order: OrderDto }) {
  const items = order.items ?? []
  const addons = order.addons ?? []
  const c = order.currencyCode
  if (items.length === 0) return null
  return (
    <section className="space-y-2">
      <h3 className="text-sm font-semibold text-gray-900">Items ({order.totalItems})</h3>
      <div className="overflow-hidden rounded-xl border border-gray-100">
        <table className="min-w-full text-sm">
          <thead className="bg-gray-50 text-xs uppercase tracking-wider text-gray-500">
            <tr>
              <th className="px-3 py-2 text-left">Item / Service</th>
              <th className="px-3 py-2 text-right">Qty</th>
              <th className="px-3 py-2 text-right">Unit</th>
              <th className="px-3 py-2 text-right">Line total</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-50">
            {items.map((it) => {
              const itemAddons = addons.filter((a) => a.orderItemId === it.id)
              return (
                <tr key={it.id}>
                  <td className="px-3 py-2">
                    <div className="font-medium text-gray-900">{it.itemNameSnapshot}</div>
                    <div className="text-xs text-gray-500">{it.serviceNameSnapshot}</div>
                    {itemAddons.map((a) => (
                      <div key={a.id} className="text-xs text-gray-400">
                        + {a.addonNameSnapshot} ({money(a.totalCharge, c)})
                      </div>
                    ))}
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums">
                    {it.quantity} {it.unitOfMeasure}
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums">{money(it.unitPrice, c)}</td>
                  <td className="px-3 py-2 text-right tabular-nums font-medium">{money(it.lineTotal, c)}</td>
                </tr>
              )
            })}
            {addons
              .filter((a) => !a.orderItemId)
              .map((a) => (
                <tr key={a.id}>
                  <td className="px-3 py-2">
                    <div className="font-medium text-gray-900">{a.addonNameSnapshot}</div>
                    <div className="text-xs text-gray-500">Order add-on</div>
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums">{a.quantity}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{money(a.unitPrice, c)}</td>
                  <td className="px-3 py-2 text-right tabular-nums font-medium">{money(a.totalCharge, c)}</td>
                </tr>
              ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

// ── Status timeline ─────────────────────────────────────────────────────────────

function TimelineSection({ order }: { order: OrderDto }) {
  const history = [...(order.statusHistory ?? [])].sort(
    (a, b) => new Date(a.changedAt).getTime() - new Date(b.changedAt).getTime(),
  )
  if (history.length === 0) return null
  return (
    <section className="space-y-2">
      <h3 className="text-sm font-semibold text-gray-900">Status timeline</h3>
      <ol className="space-y-3 border-l border-gray-200 pl-4">
        {history.map((h) => (
          <li key={h.id} className="relative">
            <span className="absolute -left-[1.3rem] top-1 h-2.5 w-2.5 rounded-full bg-lg-green ring-2 ring-white" />
            <div className="flex flex-wrap items-center gap-2">
              {h.fromStatus && (
                <>
                  <Badge variant="secondary" className="capitalize">{statusLabel(h.fromStatus)}</Badge>
                  <ArrowRight className="h-3 w-3 text-gray-400" />
                </>
              )}
              <Badge variant={statusBadgeVariant(h.toStatus)} className="capitalize">
                {statusLabel(h.toStatus)}
              </Badge>
            </div>
            <div className="mt-1 flex items-center gap-1.5 text-xs text-gray-400">
              <Clock className="h-3 w-3" />
              {formatDateTime(h.changedAt)} · by {h.changedByType}
              {h.customerNotified && <span className="text-lg-green">· customer notified</span>}
            </div>
            {h.reason && <p className="mt-0.5 text-xs text-gray-600">“{h.reason}”</p>}
          </li>
        ))}
      </ol>
    </section>
  )
}

// ── Notes ─────────────────────────────────────────────────────────────────────

function NotesSection({ orderId, canManage }: { orderId: string; canManage: boolean }) {
  const { data: notes, isLoading } = useOrderNotes(orderId)
  const createNote = useCreateOrderNote(orderId)
  const deleteNote = useDeleteOrderNote(orderId)
  const [adding, setAdding] = useState(false)
  const [text, setText] = useState('')
  const [noteType, setNoteType] = useState('internal')
  const [visibility, setVisibility] = useState('staff')
  const [error, setError] = useState<string | null>(null)

  const submit = async () => {
    setError(null)
    if (!text.trim()) {
      setError('Note text is required.')
      return
    }
    try {
      await createNote.mutateAsync({ noteType, visibility, noteText: text.trim(), isPinned: false })
      setText('')
      setAdding(false)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not add note.')
    }
  }

  return (
    <section className="space-y-2">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-gray-900">Notes</h3>
        {canManage && !adding && (
          <button
            type="button"
            onClick={() => setAdding(true)}
            className="inline-flex items-center gap-1 text-xs font-medium text-lg-green hover:underline"
          >
            <Plus className="h-3.5 w-3.5" /> Add note
          </button>
        )}
      </div>

      {canManage && adding && (
        <div className="space-y-2 rounded-xl border border-gray-100 bg-gray-50/50 p-3">
          <div className="grid grid-cols-2 gap-2">
            <select value={noteType} onChange={(e) => setNoteType(e.target.value)} className={drawerInputCls}>
              {NOTE_TYPES.map((t) => (
                <option key={t} value={t} className="capitalize">{t.replace(/_/g, ' ')}</option>
              ))}
            </select>
            <select value={visibility} onChange={(e) => setVisibility(e.target.value)} className={drawerInputCls}>
              {NOTE_VISIBILITIES.map((v) => (
                <option key={v} value={v} className="capitalize">{v}</option>
              ))}
            </select>
          </div>
          <textarea
            value={text}
            onChange={(e) => setText(e.target.value)}
            rows={2}
            placeholder="Write a note…"
            className={drawerInputCls}
          />
          {error && (
            <p className="flex items-center gap-1 text-xs text-red-600">
              <AlertTriangle className="h-3 w-3" /> {error}
            </p>
          )}
          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={() => { setAdding(false); setText(''); setError(null) }}
              className="rounded-lg border border-gray-200 px-3 py-1.5 text-xs font-medium text-gray-600 hover:bg-white"
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={submit}
              disabled={createNote.isPending}
              className="inline-flex items-center gap-1 rounded-lg bg-lg-green px-3 py-1.5 text-xs font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
            >
              {createNote.isPending && <Loader2 className="h-3 w-3 animate-spin" />} Save
            </button>
          </div>
        </div>
      )}

      {isLoading ? (
        <p className="text-xs text-gray-400">Loading notes…</p>
      ) : (notes ?? []).length === 0 ? (
        <p className="text-xs text-gray-400">No notes yet.</p>
      ) : (
        <ul className="space-y-2">
          {(notes ?? []).map((n) => (
            <li key={n.id} className="rounded-xl border border-gray-100 p-3">
              <div className="mb-1 flex items-center gap-2">
                <Badge variant="secondary" className="capitalize">{n.noteType.replace(/_/g, ' ')}</Badge>
                <span className="text-xs text-gray-400 capitalize">{n.visibility}</span>
                <span className="ml-auto text-xs text-gray-400">{formatDateTime(n.createdAt)}</span>
                {canManage && (
                  <button
                    type="button"
                    onClick={() => void deleteNote.mutate(n.id)}
                    className="text-gray-300 hover:text-red-500"
                    title="Delete note"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                )}
              </div>
              <p className="text-sm text-gray-700">{n.noteText}</p>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

// ── Invoice ─────────────────────────────────────────────────────────────────────

function InvoiceSection({
  order,
  canGenerate,
  canRead,
}: {
  order: OrderDto
  canGenerate: boolean
  canRead: boolean
}) {
  const available = invoiceAvailable(order.status)
  const { data: invoice, isLoading } = useInvoice(order.id, available && canRead)
  const generate = useGenerateInvoice(order.id)
  const [error, setError] = useState<string | null>(null)
  const [downloading, setDownloading] = useState(false)

  if (!available) {
    return (
      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-gray-900">Invoice</h3>
        <p className="text-xs text-gray-400">
          An invoice can be generated once the order reaches <span className="font-medium">ready</span>, delivered, or closed.
        </p>
      </section>
    )
  }

  const onGenerate = async () => {
    setError(null)
    try {
      await generate.mutateAsync()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not generate invoice.')
    }
  }

  const onDownload = async () => {
    if (!invoice) return
    setError(null)
    setDownloading(true)
    try {
      await downloadInvoicePdf(order.id, invoice.invoiceNumber)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not download PDF.')
    } finally {
      setDownloading(false)
    }
  }

  return (
    <section className="space-y-2">
      <h3 className="text-sm font-semibold text-gray-900">Invoice</h3>
      {isLoading ? (
        <p className="text-xs text-gray-400">Checking for invoice…</p>
      ) : invoice ? (
        <div className="space-y-2 rounded-xl border border-gray-100 p-3">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <FileText className="h-4 w-4 text-lg-green" />
              <span className="font-mono text-sm font-medium text-gray-900">{invoice.invoiceNumber}</span>
            </div>
            <Badge variant="secondary" className="capitalize">{invoice.status}</Badge>
          </div>
          <div className="text-xs text-gray-500">
            {invoice.invoiceDate} · {money(invoice.grandTotal, order.currencyCode)}
          </div>
          {canRead && (
            <button
              type="button"
              onClick={onDownload}
              disabled={downloading}
              className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-60"
            >
              {downloading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Download className="h-3.5 w-3.5" />}
              Download PDF
            </button>
          )}
        </div>
      ) : canGenerate ? (
        <button
          type="button"
          onClick={onGenerate}
          disabled={generate.isPending}
          className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-3 py-1.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
        >
          {generate.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <FileText className="h-4 w-4" />}
          Generate invoice
        </button>
      ) : (
        <p className="text-xs text-gray-400">No invoice has been generated for this order.</p>
      )}
      {error && (
        <p className="flex items-center gap-1 text-xs text-red-600">
          <AlertTriangle className="h-3 w-3" /> {error}
        </p>
      )}
    </section>
  )
}

// ── Status actions ──────────────────────────────────────────────────────────────

type PendingAction = { kind: 'transition'; toStatus: string } | { kind: 'cancel' }

function ActionsSection({
  order,
  canUpdateStatus,
  canCancel,
}: {
  order: OrderDto
  canUpdateStatus: boolean
  canCancel: boolean
}) {
  const updateStatus = useUpdateOrderStatus(order.id)
  const cancel = useCancelOrder(order.id)
  const [pending, setPending] = useState<PendingAction | null>(null)
  const [reason, setReason] = useState('')
  const [notes, setNotes] = useState('')
  const [notify, setNotify] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const targets = canUpdateStatus ? advanceableTargets(order.status) : []
  const showCancel = canCancel && canCancelFrom(order.status)

  // Reset the confirm panel whenever the order (or its status) changes.
  const orderSig = `${order.id}|${order.status}`
  const [resetSig, setResetSig] = useState(orderSig)
  if (resetSig !== orderSig) {
    setResetSig(orderSig)
    setPending(null)
    setReason('')
    setNotes('')
    setNotify(false)
    setError(null)
  }

  if (targets.length === 0 && !showCancel) {
    return (
      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-gray-900">Actions</h3>
        <p className="text-xs text-gray-400">No status actions available from this state.</p>
      </section>
    )
  }

  const open = (action: PendingAction) => {
    setPending(action)
    setReason('')
    setNotes('')
    setNotify(false)
    setError(null)
  }

  const confirm = async () => {
    if (!pending) return
    setError(null)
    if (pending.kind === 'cancel' && !reason.trim()) {
      setError('A cancellation reason is required.')
      return
    }
    try {
      if (pending.kind === 'cancel') {
        await cancel.mutateAsync(reason.trim())
      } else {
        await updateStatus.mutateAsync({
          toStatus: pending.toStatus,
          reason: reason.trim() || undefined,
          notes: notes.trim() || undefined,
          customerNotified: notify,
        })
      }
      setPending(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Action failed.')
    }
  }

  const busy = updateStatus.isPending || cancel.isPending
  const isCancel = pending?.kind === 'cancel'

  return (
    <section className="space-y-3">
      <h3 className="text-sm font-semibold text-gray-900">Actions</h3>

      {!pending && (
        <div className="flex flex-wrap gap-2">
          {targets.map((t) => (
            <button
              key={t}
              type="button"
              onClick={() => open({ kind: 'transition', toStatus: t })}
              className="inline-flex items-center gap-1.5 rounded-lg border border-lg-green/30 bg-lg-green/5 px-3 py-1.5 text-sm font-medium text-lg-green hover:bg-lg-green/10"
            >
              <ArrowRight className="h-3.5 w-3.5" />
              <span className="capitalize">{statusLabel(t)}</span>
            </button>
          ))}
          {showCancel && (
            <button
              type="button"
              onClick={() => open({ kind: 'cancel' })}
              className="inline-flex items-center gap-1.5 rounded-lg border border-red-200 bg-red-50 px-3 py-1.5 text-sm font-medium text-red-700 hover:bg-red-100"
            >
              <XCircle className="h-3.5 w-3.5" /> Cancel order
            </button>
          )}
        </div>
      )}

      {pending && (
        <div className={`space-y-3 rounded-xl border p-3 ${isCancel ? 'border-red-200 bg-red-50/40' : 'border-gray-100 bg-gray-50/50'}`}>
          <p className="text-sm font-medium text-gray-800">
            {isCancel ? (
              <>Cancel this order?</>
            ) : (
              <>
                Move to <span className="capitalize text-lg-green">{statusLabel((pending as { toStatus: string }).toStatus)}</span>?
              </>
            )}
          </p>

          <label className="block">
            <span className="mb-1 block text-xs font-medium text-gray-500">
              Reason{isCancel ? ' (required)' : ' (optional)'}
            </span>
            <input
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              className={drawerInputCls}
              placeholder={isCancel ? 'Why is this order being cancelled?' : 'Optional reason'}
            />
          </label>

          {!isCancel && (
            <>
              <label className="block">
                <span className="mb-1 block text-xs font-medium text-gray-500">Notes (optional)</span>
                <textarea
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  rows={2}
                  className={drawerInputCls}
                  placeholder="Internal notes for this transition"
                />
              </label>
              <label className="flex items-center gap-2 text-sm text-gray-700">
                <input
                  type="checkbox"
                  checked={notify}
                  onChange={(e) => setNotify(e.target.checked)}
                  className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green"
                />
                Notify customer
              </label>
            </>
          )}

          {error && (
            <p className="flex items-center gap-1 text-xs text-red-600">
              <AlertTriangle className="h-3 w-3" /> {error}
            </p>
          )}

          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setPending(null)}
              className="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-gray-600 hover:bg-white"
            >
              Back
            </button>
            <button
              type="button"
              onClick={confirm}
              disabled={busy}
              className={`inline-flex items-center gap-1.5 rounded-lg px-4 py-1.5 text-sm font-semibold text-white disabled:opacity-60 ${
                isCancel ? 'bg-red-600 hover:bg-red-700' : 'bg-lg-green hover:bg-[var(--lg-green-hover)]'
              }`}
            >
              {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              {isCancel ? 'Confirm cancel' : 'Confirm'}
            </button>
          </div>
        </div>
      )}
    </section>
  )
}

// ── Drawer ─────────────────────────────────────────────────────────────────────

export function OrderDetailDrawer({
  orderId,
  onClose,
}: {
  orderId: string | null
  onClose: () => void
}) {
  const { hasPermission } = usePermissions()
  const { data: order, isLoading, isError, error } = useOrder(orderId)

  const canRead = hasPermission(PERM.read)
  const canUpdateStatus = hasPermission(PERM.statusUpdate)
  const canCancel = hasPermission(PERM.cancel)
  const canManageNotes = hasPermission(PERM.notesManage)
  const canGenerateInvoice = hasPermission(PERM.invoiceGenerate)

  return (
    <FormDrawer
      open={!!orderId}
      onClose={onClose}
      icon={Package}
      eyebrow="Order"
      title={
        order ? (
          <span className="font-mono text-lg">{order.orderNumber}</span>
        ) : (
          'Order'
        )
      }
      headerExtra={
        order ? (
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant={statusBadgeVariant(order.status)} className="capitalize">
              {statusLabel(order.status)}
            </Badge>
            {order.isExpress && <Badge variant="warning">Express</Badge>}
            <span className="text-sm font-medium text-gray-700">
              {formatCurrency(order.grandTotal, order.currencyCode)}
            </span>
          </div>
        ) : undefined
      }
      width="lg"
      footer={null}
    >
      {isLoading && (
        <div className="flex items-center justify-center py-12 text-gray-400">
          <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading order…
        </div>
      )}
      {isError && (
        <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2.5 text-sm text-red-700">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>{error instanceof Error ? error.message : 'Could not load the order.'}</span>
        </div>
      )}

      {order && (
        <div className="space-y-6">
          {(canUpdateStatus || (canCancel && canCancelFrom(order.status))) && (
            <ActionsSection order={order} canUpdateStatus={canUpdateStatus} canCancel={canCancel} />
          )}
          <SummarySection order={order} />
          <ItemsSection order={order} />
          <InvoiceSection order={order} canGenerate={canGenerateInvoice} canRead={canRead} />
          <TimelineSection order={order} />
          {canRead && <NotesSection orderId={order.id} canManage={canManageNotes} />}
        </div>
      )}
    </FormDrawer>
  )
}
