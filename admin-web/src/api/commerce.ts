import { commerceClient, unwrapPaginated } from './client'
import type { ApiResponse, PaginatedList, PromotionDto, CouponDto } from '@/types/api'

const ADMIN = '/api/v1/admin'

// ── Promotions ────────────────────────────────────────────────────────────────

export async function listPromotions(
  params: { page?: number; pageSize?: number } = {},
): Promise<PaginatedList<PromotionDto>> {
  const { data } = await commerceClient.get<ApiResponse<PaginatedList<PromotionDto>>>(
    `${ADMIN}/promotions`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}

// ── Coupons ───────────────────────────────────────────────────────────────────

export async function listCoupons(
  params: { page?: number; pageSize?: number } = {},
): Promise<PaginatedList<CouponDto>> {
  const { data } = await commerceClient.get<ApiResponse<PaginatedList<CouponDto>>>(
    `${ADMIN}/coupons`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}
