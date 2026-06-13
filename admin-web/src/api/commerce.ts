import { commerceClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  PromotionDto,
  CreatePromotionPayload,
  UpdatePromotionPayload,
  CouponDto,
  CreateCouponPayload,
  UpdateCouponPayload,
  PackageDto,
  CreatePackagePayload,
  UpdatePackagePayload,
} from '@/types/api'

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

export async function createPromotion(payload: CreatePromotionPayload): Promise<PromotionDto> {
  const { data } = await commerceClient.post<ApiResponse<PromotionDto>>(`${ADMIN}/promotions`, payload)
  return unwrap(data)
}

export async function updatePromotion(id: string, payload: UpdatePromotionPayload): Promise<PromotionDto> {
  const { data } = await commerceClient.put<ApiResponse<PromotionDto>>(`${ADMIN}/promotions/${id}`, payload)
  return unwrap(data)
}

/** Soft-delete (archive) a promotion. */
export async function deletePromotion(id: string): Promise<void> {
  await commerceClient.delete(`${ADMIN}/promotions/${id}`)
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

export async function createCoupon(payload: CreateCouponPayload): Promise<CouponDto> {
  const { data } = await commerceClient.post<ApiResponse<CouponDto>>(`${ADMIN}/coupons`, payload)
  return unwrap(data)
}

export async function updateCoupon(id: string, payload: UpdateCouponPayload): Promise<CouponDto> {
  const { data } = await commerceClient.put<ApiResponse<CouponDto>>(`${ADMIN}/coupons/${id}`, payload)
  return unwrap(data)
}

/** Soft-delete (archive) a coupon. */
export async function deleteCoupon(id: string): Promise<void> {
  await commerceClient.delete(`${ADMIN}/coupons/${id}`)
}

// ── Packages ──────────────────────────────────────────────────────────────────

export async function listPackages(
  params: { page?: number; pageSize?: number } = {},
): Promise<PaginatedList<PackageDto>> {
  const { data } = await commerceClient.get<ApiResponse<PaginatedList<PackageDto>>>(
    `${ADMIN}/packages`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}

export async function createPackage(payload: CreatePackagePayload): Promise<PackageDto> {
  const { data } = await commerceClient.post<ApiResponse<PackageDto>>(`${ADMIN}/packages`, payload)
  return unwrap(data)
}

export async function updatePackage(id: string, payload: UpdatePackagePayload): Promise<PackageDto> {
  const { data } = await commerceClient.put<ApiResponse<PackageDto>>(`${ADMIN}/packages/${id}`, payload)
  return unwrap(data)
}

/** Soft-delete (archive) a package. */
export async function deletePackage(id: string): Promise<void> {
  await commerceClient.delete(`${ADMIN}/packages/${id}`)
}
