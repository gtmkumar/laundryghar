/**
 * Payment capture for a freshly-created walk-in order.
 *
 * Calls POST /api/v1/admin/payments (Commerce) to record the offline tender.
 * The backend creates the commerce.payments row, updates order.amount_paid +
 * payment_status, and (for cash tenders) posts the cash-book entry server-side
 * on the store's open full_day book — the frontend no longer writes cash-book
 * entries directly.
 */
import { useState } from 'react'
import { Loader2, Banknote, Smartphone, CreditCard } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Modal } from '@/components/shared/Modal'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Button } from '@/components/ui/button'
import { useRecordOfflinePayment } from '@/hooks/usePayments'
import { formatCurrency } from '@/lib/utils'
import type { OrderDto } from '@/types/api'

type PaymentMethod = 'cash' | 'upi' | 'card'

export interface RecordedPayment {
  amount: number
  method: PaymentMethod
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

export function PaymentModal({ open, onClose, order, onRecorded }: PaymentModalProps) {
  const { t } = useTranslation()
  const grandTotal = order.amountDue ?? order.grandTotal

  const [method, setMethod] = useState<PaymentMethod>('cash')
  const [amount, setAmount] = useState(String(grandTotal))
  const [reference, setReference] = useState('')
  const [error, setError] = useState<string | null>(null)

  const { mutate: recordPayment, isPending } = useRecordOfflinePayment()

  const parsedAmount = parseFloat(amount)
  const amountValid = !isNaN(parsedAmount) && parsedAmount > 0
  const tendered = amountValid ? parsedAmount : 0
  // For cash, anything over the total is change handed back; UPI/card are exact.
  const changeDue = method === 'cash' ? Math.max(0, tendered - grandTotal) : 0
  const balanceDue = Math.max(0, grandTotal - tendered)
  const refRequired = method !== 'cash'

  function handleRecord() {
    if (!amountValid) return setError(t('payment.invalidAmount'))
    if (refRequired && !reference.trim()) {
      return setError(t('payment.refRequired'))
    }
    setError(null)

    // The booked amount caps at grand total (cash change is handed back to customer).
    const bookedAmount = method === 'cash' ? Math.min(tendered, grandTotal) : tendered

    recordPayment(
      {
        orderId: order.id,
        method,
        amount: bookedAmount,
        reference: refRequired ? reference.trim() : null,
      },
      {
        onSuccess: () => onRecorded({ amount: bookedAmount, method }),
        onError: (err) =>
          setError(err instanceof Error ? err.message : t('payment.failed')),
      },
    )
  }

  return (
    <Modal open={open} onClose={onClose} title={t('payment.title')}>
      <div className="space-y-5">
        {/* Amount due */}
        <div className="rounded-xl bg-blue-50 p-4 text-center">
          <p className="text-xs text-blue-700">{t('payment.amountDue')}</p>
          <p className="text-2xl font-bold text-blue-800">
            {formatCurrency(grandTotal, order.currencyCode)}
          </p>
          <p className="text-xs text-blue-600 mt-0.5">Order {order.orderNumber}</p>
        </div>

        {/* Method */}
        <div className="space-y-2">
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
                  onClick={() => {
                    setMethod(m.value)
                    setError(null)
                  }}
                  className={`flex flex-col items-center gap-1 h-16 rounded-xl border-2 text-sm font-medium transition-colors ${
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
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
          />
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

        {/* Change / balance summary */}
        {amountValid && (
          <div className="rounded-xl bg-gray-50 p-3 text-sm space-y-1">
            {method === 'cash' && changeDue > 0 && (
              <div className="flex justify-between font-semibold text-green-700">
                <span>{t('payment.changeDue')}</span>
                <span>{formatCurrency(changeDue, order.currencyCode)}</span>
              </div>
            )}
            {balanceDue > 0 && (
              <div className="flex justify-between font-semibold text-amber-700">
                <span>{t('payment.balanceDue')}</span>
                <span>{formatCurrency(balanceDue, order.currencyCode)}</span>
              </div>
            )}
            {balanceDue === 0 && changeDue === 0 && (
              <div className="flex justify-between font-semibold text-blue-700">
                <span>{t('payment.amountPaid')}</span>
                <span>{formatCurrency(grandTotal, order.currencyCode)}</span>
              </div>
            )}
          </div>
        )}

        {error && <p className="text-sm text-red-600">{error}</p>}

        <div className="flex gap-3">
          <Button variant="outline" size="touch" className="flex-1" onClick={onClose}>
            Skip
          </Button>
          <Button
            size="touch"
            className="flex-1"
            disabled={isPending || !amountValid}
            onClick={handleRecord}
          >
            {isPending ? (
              <>
                <Loader2 className="h-5 w-5 animate-spin" /> {t('payment.recording')}
              </>
            ) : (
              t('payment.recordPayment')
            )}
          </Button>
        </div>
      </div>
    </Modal>
  )
}
