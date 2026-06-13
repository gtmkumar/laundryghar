import { logisticsClient, unwrap } from './client'
import type {
  ApiResponse,
  RiderPayoutRequestDto,
  PayoutRequestStatus,
  RejectPayoutPayload,
  MarkPayoutPaidPayload,
} from '@/types/api'

const PAYOUTS = '/api/v1/admin/rider-payout-requests'

/** Rider self-service payout requests, optionally narrowed to one workflow status. */
export async function getPayoutRequests(
  status?: PayoutRequestStatus,
): Promise<RiderPayoutRequestDto[]> {
  const { data } = await logisticsClient.get<ApiResponse<RiderPayoutRequestDto[]>>(PAYOUTS, {
    params: status ? { status } : undefined,
  })
  return unwrap(data) ?? []
}

/** Approve a 'requested' payout → moves it to 'approved'. */
export async function approvePayoutRequest(id: string): Promise<RiderPayoutRequestDto> {
  const { data } = await logisticsClient.post<ApiResponse<RiderPayoutRequestDto>>(
    `${PAYOUTS}/${id}/approve`,
  )
  return unwrap(data)
}

/** Reject a 'requested' payout with a reason → moves it to 'rejected'. */
export async function rejectPayoutRequest(
  id: string,
  payload: RejectPayoutPayload,
): Promise<RiderPayoutRequestDto> {
  const { data } = await logisticsClient.post<ApiResponse<RiderPayoutRequestDto>>(
    `${PAYOUTS}/${id}/reject`,
    payload,
  )
  return unwrap(data)
}

/**
 * Settle an 'approved' payout with a payment reference → moves it to 'paid'.
 * The server posts the disbursement to the cash book as a side effect.
 */
export async function markPayoutPaid(
  id: string,
  payload: MarkPayoutPaidPayload,
): Promise<RiderPayoutRequestDto> {
  const { data } = await logisticsClient.post<ApiResponse<RiderPayoutRequestDto>>(
    `${PAYOUTS}/${id}/mark-paid`,
    payload,
  )
  return unwrap(data)
}
