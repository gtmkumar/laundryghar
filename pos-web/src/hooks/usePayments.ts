import { useMutation } from '@tanstack/react-query'
import { recordOfflinePayment } from '@/api/payments'
import type { RecordOfflinePaymentRequest } from '@/types/api'

export function useRecordOfflinePayment() {
  return useMutation({
    mutationFn: (req: RecordOfflinePaymentRequest) => recordOfflinePayment(req),
  })
}
