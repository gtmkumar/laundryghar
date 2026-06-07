import { catalogClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  ServiceCategoryDto,
  ServiceDto,
  ItemDto,
  PriceListDto,
  PaginationParams,
  AdminCustomerDto,
  AdminCustomerListParams,
} from '@/types/api'

const ADMIN = '/api/v1/admin'

// ── Service Categories ────────────────────────────────────────────────────────

export async function getServiceCategories(
  params: PaginationParams = {},
): Promise<PaginatedList<ServiceCategoryDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<ServiceCategoryDto>>>(
    `${ADMIN}/service-categories`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

export async function getServiceCategoryById(id: string): Promise<ServiceCategoryDto> {
  const { data } = await catalogClient.get<ApiResponse<ServiceCategoryDto>>(
    `${ADMIN}/service-categories/${id}`,
  )
  return unwrap(data)
}

// ── Services ──────────────────────────────────────────────────────────────────

export async function getServices(
  params: PaginationParams & { categoryId?: string } = {},
): Promise<PaginatedList<ServiceDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<ServiceDto>>>(
    `${ADMIN}/services`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

// ── Items ─────────────────────────────────────────────────────────────────────

export async function getItems(
  params: PaginationParams & { itemGroupId?: string } = {},
): Promise<PaginatedList<ItemDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<ItemDto>>>(
    `${ADMIN}/items`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

// ── Price Lists ───────────────────────────────────────────────────────────────

export async function getPriceLists(
  params: PaginationParams = {},
): Promise<PaginatedList<PriceListDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<PriceListDto>>>(
    `${ADMIN}/price-lists`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

export async function getPriceListById(id: string): Promise<PriceListDto> {
  const { data } = await catalogClient.get<ApiResponse<PriceListDto>>(
    `${ADMIN}/price-lists/${id}`,
  )
  return unwrap(data)
}

// ── Admin Customers ───────────────────────────────────────────────────────────

export async function getAdminCustomers(
  params: AdminCustomerListParams = {},
): Promise<PaginatedList<AdminCustomerDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<AdminCustomerDto>>>(
    `${ADMIN}/customers`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}
