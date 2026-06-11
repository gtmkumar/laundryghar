import { useMutation, useQueryClient } from '@tanstack/react-query'
import { recordOfflinePayment } from '@/api/payments'
import { orderKeys } from '@/hooks/useOrders'
import type { RecordOfflinePaymentRequest } from '@/types/api'

/**
 * Records an offline (cash/UPI/card) payment for a walk-in order.
 *
 * POS-6: this mutation previously had no onSuccess, so after charging an order
 * the cached order still read "unpaid" — operators could re-open the payment
 * modal and double-charge. On success we now:
 *  - invalidate the orders list (payment_status changed),
 *  - invalidate the affected order's detail (amount_paid / amount_due changed;
 *    `orderId` rides on the mutation variables so we can target it precisely),
 *  - invalidate cash-book queries (a cash tender posts a server-side cash-book
 *    entry, so the open book's inflow + the cash-book list are now stale).
 */
export function useRecordOfflinePayment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: RecordOfflinePaymentRequest) => recordOfflinePayment(req),
    onSuccess: (_data, variables) => {
      // Prefix-invalidate the list (every params-keyed variant) + the one detail.
      void qc.invalidateQueries({ queryKey: ['orders', 'list'] })
      void qc.invalidateQueries({ queryKey: orderKeys.detail(variables.orderId) })
      void qc.invalidateQueries({ queryKey: ['cash-books'] })
    },
  })
}
