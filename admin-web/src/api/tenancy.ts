import { identityClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  BrandDto,
  PlatformDto,
  FranchiseDto,
  StoreDto,
  WarehouseDto,
  PaginationParams,
  CreateStorePayload,
  UpdateStorePayload,
  CreateWarehousePayload,
  UpdateWarehousePayload,
} from '@/types/api'

const ADMIN = '/api/v1/admin'

// ── Brands ───────────────────────────────────────────────────────────────────

export async function getBrands(
  params: PaginationParams & { status?: string; search?: string } = {},
): Promise<PaginatedList<BrandDto>> {
  const { data } = await identityClient.get<ApiResponse<PaginatedList<BrandDto>>>(
    `${ADMIN}/brands`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

export async function getBrandById(id: string): Promise<BrandDto> {
  const { data } = await identityClient.get<ApiResponse<BrandDto>>(`${ADMIN}/brands/${id}`)
  return unwrap(data)
}

// ── Platforms ────────────────────────────────────────────────────────────────

export async function getPlatforms(params: PaginationParams = {}): Promise<PaginatedList<PlatformDto>> {
  const { data } = await identityClient.get<ApiResponse<PaginatedList<PlatformDto>>>(
    `${ADMIN}/platforms`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

// ── Franchises ────────────────────────────────────────────────────────────────

export async function getFranchises(
  params: PaginationParams & { brandId?: string } = {},
): Promise<PaginatedList<FranchiseDto>> {
  const { data } = await identityClient.get<ApiResponse<PaginatedList<FranchiseDto>>>(
    `${ADMIN}/franchises`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

// ── Stores ────────────────────────────────────────────────────────────────────

export async function getStores(
  params: PaginationParams & { brandId?: string; franchiseId?: string } = {},
): Promise<PaginatedList<StoreDto>> {
  const { data } = await identityClient.get<ApiResponse<PaginatedList<StoreDto>>>(
    `${ADMIN}/stores`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

export async function createStore(payload: CreateStorePayload): Promise<StoreDto> {
  const { data } = await identityClient.post<ApiResponse<StoreDto>>(`${ADMIN}/stores`, payload)
  return unwrap(data)
}

export async function updateStore(id: string, payload: UpdateStorePayload): Promise<StoreDto> {
  const { data } = await identityClient.put<ApiResponse<StoreDto>>(`${ADMIN}/stores/${id}`, payload)
  return unwrap(data)
}

// ── Warehouses ────────────────────────────────────────────────────────────────

export async function getWarehouses(
  params: PaginationParams & { brandId?: string; franchiseId?: string } = {},
): Promise<PaginatedList<WarehouseDto>> {
  const { data } = await identityClient.get<ApiResponse<PaginatedList<WarehouseDto>>>(
    `${ADMIN}/warehouses`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

export async function createWarehouse(payload: CreateWarehousePayload): Promise<WarehouseDto> {
  const { data } = await identityClient.post<ApiResponse<WarehouseDto>>(`${ADMIN}/warehouses`, payload)
  return unwrap(data)
}

export async function updateWarehouse(
  id: string,
  payload: UpdateWarehousePayload,
): Promise<WarehouseDto> {
  const { data } = await identityClient.put<ApiResponse<WarehouseDto>>(
    `${ADMIN}/warehouses/${id}`,
    payload,
  )
  return unwrap(data)
}
