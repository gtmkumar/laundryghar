/**
 * Payment capture for a walk-in order.
 *
 * Calls POST /api/v1/admin/payments (Commerce) to record the offline tender.
 * The backend creates the commerce.payments row, updates order.amount_paid +
 * payment_status, and (for cash tenders) posts the cash-book entry server-side
 * on the store's open full_day book — the frontend no longer writes cash-book
 * entries directly.
 *
 * POS-3: cash change is computed from the RAW tendered amount, not the capped
 *   booked amount, so the receipt can print real "Change ₹X".
 * POS-4: partial / pay-later are first-class — a tender below the balance is
 *   recorded as a partial payment (balance stays due) and an explicit
 *   "Pay later (credit)" path records nothing but marks the order unpaid-on-credit.
 * POS-2 / H3: one idempotency key per modal-open (held in a ref), reused across
 *   retries, prevents a double charge on retry-after-error / double-tap.
 * POS-7: state resets every time the modal opens; amount has inline validation.
 * WEB-3: the record action is gated on the `payment.record` permission.
 */
import { useMemo, useRef, useState } from 'react'
import { Loader2, Banknote, Smartphone, CreditCard, Clock } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Modal } from '@/components/shared/Modal'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Button } from '@/components/ui/button'
import { useRecordOfflinePayment } from '@/hooks/usePayments'
import { usePermissions, PERMISSIONS } from '@/hooks/usePermissions'
import { formatCurrency, newIdempotencyKey } from '@/lib/utils'
import type { OrderDto } from '@/types/api'

type PaymentMethod = 'cash' | 'upi' | 'card'

export interface RecordedPayment {
  /** Amount booked against the order (capped at the balance due). */
  amount: number
  /** Raw cash handed over (>= amount for cash; equals amount otherwise). */
  tendered: number
  method: PaymentMethod
  /** Server-reported payment status after this tender: paid | partial | unpaid. */
  paymentStatus: string
  /** Server-reported balance still owed after this tender. */
  balanceDue: number
  /** True when the customer explicitly chose to pay later (credit) — nothing charged. */
  credit?: boolean
}

interface PaymentModalProps {
  open: boolean
  onClose: () => void
  order: OrderDto
  onRecorded: (payment: RecordedPayment) => void
}

const METHODS: { value: PaymentMethod; label: string; icon: typeof Banknote }[] = [
  { value: 'cash', label: 'Cash', icon: Banknote },
  { value: 'upi', label: 'UPI', icon: Smartphone },
  { value: 'card', label: 'Card', icon: CreditCard },
]

/**
 * Thin wrapper: owns the Modal shell and mounts the form fresh per open.
 *
 * POS-7: rather than resetting form state in an effect (which triggers the
 * react-hooks/set-state-in-effect lint and a cascading render), the inner form
 * is keyed by `order.id` and only mounted while `open`. Closing unmounts it, so
 * the next open re-runs the useState initializers — a clean reset with no effect.
 */
export function PaymentModal({ open, onClose, order, onRecorded }: PaymentModalProps) {
  const { t } = useTranslation()
  return (
    <Modal open={open} onClose={onClose} title={t('payment.title')}>
      {open && (
        <PaymentForm
          key={order.id}
          order={order}
          onClose={onClose}
          onRecorded={onRecorded}
        />
      )}
    </Modal>
  )
}

function PaymentForm({
  order,
  onClose,
  onRecorded,
}: {
  order: OrderDto
  onClose: () => void
  onRecorded: (payment: RecordedPayment) => void
}) {
  const { t } = useTranslation()
  const { can } = usePermissions()
  const canRecord = can(PERMISSIONS.paymentRecord)
  const grandTotal = order.amountDue ?? order.grandTotal

  const [method, setMethod] = useState<PaymentMethod>('cash')
  const [amount, setAmount] = useState(String(grandTotal))
  const [reference, setReference] = useState('')
  const [error, setError] = useState<string | null>(null)

  // H3: one idempotency key per modal-open, reused across retry-after-error.
  // PaymentForm is keyed by order.id and mounted only while open, so this ref is
  // initialized exactly once per open — a retry sends the SAME key and dedupes.
  const idempotencyKeyRef = useRef<string>(newIdempotencyKey())

  const { mutate: recordPayment, isPending } = useRecordOfflinePayment()

  const parsedAmount = parseFloat(amount)
  const amountValid = !isNaN(parsedAmount) && parsedAmount > 0
  const tendered = amountValid ? parsedAmount : 0
  // POS-3: change is real cash handed back — computed from the RAW tender.
  const changeDue = method === 'cash' ? Math.max(0, tendered - grandTotal) : 0
  // POS-4: amount booked is capped at the balance (change is handed back, not booked).
  const bookedAmount = Math.min(tendered, grandTotal)
  const balanceDue = useMemo(() => Math.max(0, grandTotal - bookedAmount), [grandTotal, bookedAmount])
  const isPartial = amountValid && bookedAmount > 0 && balanceDue > 0
  const refRequired = method !== 'cash'

  function handleRecord() {
    if (!canRecord) return
    if (!amountValid) return setError(t('payment.invalidAmount'))
    if (refRequired && !reference.trim()) {
      return setError(t('payment.refRequired'))
    }
    setError(null)

    recordPayment(
      {
        orderId: order.id,
        method,
        amount: bookedAmount,
        reference: refRequired ? reference.trim() : null,
        idempotencyKey: idempotencyKeyRef.current,
      },
      {
        onSuccess: (res) =>
          onRecorded({
            amount: bookedAmount,
            tendered,
            method,
            // Prefer the server's authoritative status/balance; fall back to local derivation.
            paymentStatus: res.orderPaymentStatus ?? (balanceDue > 0 ? 'partial' : 'paid'),
            balanceDue: res.orderAmountDue ?? balanceDue,
          }),
        onError: (err) =>
          setError(err instanceof Error ? err.message : t('payment.failed')),
      },
    )
  }

  // POS-4: explicit pay-later — records nothing, marks the order on credit so the
  // confirmation + receipt show the full balance as owed (distinct from "Skip",
  // which just dismisses the modal without a decision).
  function handlePayLater() {
    onRecorded({
      amount: 0,
      tendered: 0,
      method,
      paymentStatus: order.paymentStatus === 'paid' ? 'paid' : 'unpaid',
      balanceDue: grandTotal,
      credit: true,
    })
  }

  return (
    <div className="space-y-5">
      {/* Amount due */}
        <div className="rounded-xl bg-blue-50 p-4 text-center">
          <p className="text-xs text-blue-700">{t('payment.amountDue')}</p>
          <p className="text-2xl font-bold text-blue-800">
            {formatCurrency(grandTotal, order.currencyCode)}
          </p>
          <p className="text-xs text-blue-600 mt-0.5">Order {order.orderNumber}</p>
        </div>

        {!canRecord && (
          <p
            className="text-sm text-amber-700 bg-amber-50 rounded-lg p-3"
            role="status"
          >
            {t('payment.noPermission', {
              defaultValue:
                'You do not have permission to record payments. You can still mark this order to pay later.',
            })}
          </p>
        )}

        {/* Method */}
        <fieldset disabled={!canRecord} className="space-y-2 disabled:opacity-60">
          <Label>{t('payment.paymentMethod')}</Label>
          <div className="grid grid-cols-3 gap-2">
            {METHODS.map((m) => {
              const Icon = m.icon
              const active = method === m.value
              const methodLabel = t(`payment.${m.value}`, { defaultValue: m.label })
              return (
                <button
                  key={m.value}
                  type="button"
                  disabled={!canRecord}
                  onClick={() => {
                    setMethod(m.value)
                    setError(null)
                  }}
                  className={`flex flex-col items-center gap-1 h-16 rounded-xl border-2 text-sm font-medium transition-colors disabled:cursor-not-allowed ${
                    active
                      ? 'border-blue-600 bg-blue-50 text-blue-700'
                      : 'border-gray-200 bg-white text-gray-600'
                  }`}
                >
                  <Icon className="h-5 w-5" />
                  {methodLabel}
                </button>
              )
            })}
          </div>

          {/* Amount */}
          <div className="space-y-2">
            <Label htmlFor="payAmount">
              {method === 'cash' ? t('payment.cashTendered') : t('payment.amount')}
            </Label>
            <Input
              id="payAmount"
              type="number"
              min="0.01"
              step="0.01"
              inputMode="decimal"
              value={amount}
              aria-invalid={amount.length > 0 && !amountValid}
              onChange={(e) => {
                setAmount(e.target.value)
                if (error) setError(null)
              }}
            />
            {amount.length > 0 && !amountValid && (
              <p className="text-xs text-red-600">{t('payment.invalidAmount')}</p>
            )}
          </div>

          {/* Reference for non-cash */}
          {refRequired && (
            <div className="space-y-2">
              <Label htmlFor="payRef">
                {method === 'upi' ? t('payment.upiRef') : t('payment.cardRef')}
              </Label>
              <Input
                id="payRef"
                type="text"
                placeholder={t('payment.refPlaceholder')}
                value={reference}
                onChange={(e) => setReference(e.target.value)}
              />
            </div>
          )}
        </fieldset>

        {/* Change / balance summary */}
        {amountValid && (
          <div className="rounded-xl bg-gray-50 p-3 text-sm space-y-1">
            {method === 'cash' && changeDue > 0 && (
              <div className="flex justify-between font-semibold text-green-700">
                <span>{t('payment.changeDue')}</span>
                <span>{formatCurrency(changeDue, order.currencyCode)}</span>
              </div>
            )}
            {isPartial && (
              <div className="flex justify-between font-semibold text-amber-700">
                <span>{t('payment.balanceDue')}</span>
                <span>{formatCurrency(balanceDue, order.currencyCode)}</span>
              </div>
            )}
            {!isPartial && changeDue === 0 && (
              <div className="flex justify-between font-semibold text-blue-700">
                <span>{t('payment.amountPaid')}</span>
                <span>{formatCurrency(bookedAmount, order.currencyCode)}</span>
              </div>
            )}
            {isPartial && (
              <p className="text-[11px] text-amber-600 pt-1">
                {t('payment.partialNote', {
                  defaultValue: 'Recorded as a partial payment — the balance stays due.',
                })}
              </p>
            )}
          </div>
        )}

        {error && <p className="text-sm text-red-600">{error}</p>}

        <div className="flex flex-col gap-3">
          <Button
            size="touch"
            className="w-full"
            disabled={isPending || !amountValid || !canRecord}
            onClick={handleRecord}
          >
            {isPending ? (
              <>
                <Loader2 className="h-5 w-5 animate-spin" /> {t('payment.recording')}
              </>
            ) : isPartial ? (
              t('payment.recordPartial', { defaultValue: 'Record partial payment' })
            ) : (
              t('payment.recordPayment')
            )}
          </Button>
          <div className="flex gap-3">
            <Button variant="outline" size="touch" className="flex-1" onClick={onClose}>
              {t('payment.skip', { defaultValue: 'Skip' })}
            </Button>
            <Button
              variant="secondary"
              size="touch"
              className="flex-1"
              onClick={handlePayLater}
            >
              <Clock className="h-5 w-5" />
              {t('payment.payLater', { defaultValue: 'Pay later (credit)' })}
            </Button>
          </div>
        </div>
      </div>
  )
}
