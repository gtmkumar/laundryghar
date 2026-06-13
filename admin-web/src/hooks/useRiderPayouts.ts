import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getPayoutRequests,
  approvePayoutRequest,
  rejectPayoutRequest,
  markPayoutPaid,
} from '@/api/riderPayouts'
import type { PayoutRequestStatus } from '@/types/api'
import { useEffectiveBrandId } from './useBrandContext'

/** Payout requests for the chosen workflow status (defaults to 'requested' at the call site). */
export function usePayoutRequests(status?: PayoutRequestStatus) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['rider-payouts', brandId, status ?? ''],
    queryFn: () => getPayoutRequests(status),
    enabled: !!brandId,
  })
}

/** Invalidate every payout-status bucket after a review action so all filters refresh. */
function invalidatePayouts(qc: ReturnType<typeof useQueryClient>) {
  qc.invalidateQueries({ queryKey: ['rider-payouts'] })
}

/**
 * Approve or reject a 'requested' payout. Reject carries a reason; approve does
 * not. Both invalidate the list so the row leaves the 'requested' bucket.
 */
export function useReviewPayout() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({
      id,
      action,
      reason,
    }: {
      id: string
      action: 'approve' | 'reject'
      reason?: string
    }) =>
      action === 'approve'
        ? approvePayoutRequest(id)
        : rejectPayoutRequest(id, { reason: reason ?? '' }),
    onSuccess: () => invalidatePayouts(qc),
  })
}

/** Mark an 'approved' payout paid with a payment reference (posts to the cash book). */
export function useMarkPayoutPaid() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, reference }: { id: string; reference: string }) =>
      markPayoutPaid(id, { reference }),
    onSuccess: () => invalidatePayouts(qc),
  })
}
