import { commerceClient, unwrap } from './client'
import type { ApiResponse, RecordOfflinePaymentRequest, OfflinePaymentDto } from '@/types/api'

const ADMIN = '/api/v1/admin'

// ── Record offline payment ────────────────────────────────────────────────────
// POST /api/v1/admin/payments
// Creates a commerce payment row, updates order amount_paid + payment_status,
// and (cash-only) mirrors the entry into the store's open cash book server-side.
// Requires permission: payment.record.
// 422 if payment would exceed order grand total.
// Idempotent: same (orderId + amount + reference) returns the existing record.

export async function recordOfflinePayment(
  req: RecordOfflinePaymentRequest,
): Promise<OfflinePaymentDto> {
  const { data } = await commerceClient.post<ApiResponse<OfflinePaymentDto>>(
    `${ADMIN}/payments`,
    req,
  )
  return unwrap(data)
}
