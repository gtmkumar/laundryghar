import { identityClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  BrandDto,
  StoreDto,
  PaginationParams,
} from '@/types/api'

const ADMIN = '/api/v1/admin'

// ── Brands ───────────────────────────────────────────────────────────────────

export async function getBrands(
  params: PaginationParams & { status?: string } = {},
): Promise<PaginatedList<BrandDto>> {
  const { data } = await identityClient.get<ApiResponse<PaginatedList<BrandDto>>>(
    `${ADMIN}/brands`,
    { params: { page: 1, pageSize: 50, ...params } },
  )
  return unwrapPaginated(data)
}

export async function getBrandById(id: string): Promise<BrandDto> {
  const { data } = await identityClient.get<ApiResponse<BrandDto>>(`${ADMIN}/brands/${id}`)
  return unwrap(data)
}

// ── Stores ────────────────────────────────────────────────────────────────────

export async function getStores(
  params: PaginationParams & { brandId?: string; franchiseId?: string } = {},
): Promise<PaginatedList<StoreDto>> {
  const { data } = await identityClient.get<ApiResponse<PaginatedList<StoreDto>>>(
    `${ADMIN}/stores`,
    { params: { page: 1, pageSize: 50, ...params } },
  )
  return unwrapPaginated(data)
}
