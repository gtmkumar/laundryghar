import { catalogClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  ServiceCategoryDto,
  ServiceDto,
  ItemDto,
  PriceListItemDto,
  PriceResolutionDto,
  PaginationParams,
} from '@/types/api'

const ADMIN = '/api/v1/admin'

// ── Service Categories ────────────────────────────────────────────────────────

export async function getServiceCategories(
  params: PaginationParams = {},
): Promise<PaginatedList<ServiceCategoryDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<ServiceCategoryDto>>>(
    `${ADMIN}/service-categories`,
    { params: { page: 1, pageSize: 50, ...params } },
  )
  return unwrapPaginated(data)
}

// ── Services ──────────────────────────────────────────────────────────────────

export async function getServices(
  params: PaginationParams & { categoryId?: string } = {},
): Promise<PaginatedList<ServiceDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<ServiceDto>>>(
    `${ADMIN}/services`,
    { params: { page: 1, pageSize: 50, ...params } },
  )
  return unwrapPaginated(data)
}

// ── Items ─────────────────────────────────────────────────────────────────────

export async function getItems(
  params: PaginationParams & { itemGroupId?: string } = {},
): Promise<PaginatedList<ItemDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<ItemDto>>>(
    `${ADMIN}/items`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}

// ── Price List Items (for a given price list) ─────────────────────────────────

export async function getPriceListItems(
  priceListId: string,
  params: PaginationParams = {},
): Promise<PaginatedList<PriceListItemDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<PriceListItemDto>>>(
    `${ADMIN}/price-lists/${priceListId}/items`,
    { params: { page: 1, pageSize: 200, ...params } },
  )
  return unwrapPaginated(data)
}

// ── Price Resolution ──────────────────────────────────────────────────────────
// GET /api/v1/admin/pricing/resolve?itemId=&serviceId=&variantId=&storeId=

export async function resolvePrice(
  itemId: string,
  serviceId: string,
  storeId?: string,
  variantId?: string,
): Promise<PriceResolutionDto> {
  const { data } = await catalogClient.get<ApiResponse<PriceResolutionDto>>(
    `${ADMIN}/pricing/resolve`,
    { params: { itemId, serviceId, variantId, storeId } },
  )
  return unwrap(data)
}
