import { catalogClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  ServiceCategoryDto,
  ServiceDto,
  ItemDto,
  FabricTypeDto,
  ItemGroupDto,
  PriceListDto,
  PriceListItemDto,
  PaginationParams,
  AdminCustomerDto,
  AdminCustomerListParams,
  AdminUpdateCustomerPayload,
  CreateServiceCategoryPayload,
  UpdateServiceCategoryPayload,
  CreateServicePayload,
  UpdateServicePayload,
  CreateItemPayload,
  UpdateItemPayload,
  CreatePriceListPayload,
  UpdatePriceListPayload,
  CreatePriceListItemPayload,
  UpdatePriceListItemPayload,
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

export async function createServiceCategory(
  payload: CreateServiceCategoryPayload,
): Promise<ServiceCategoryDto> {
  const { data } = await catalogClient.post<ApiResponse<ServiceCategoryDto>>(
    `${ADMIN}/service-categories`,
    payload,
  )
  return unwrap(data)
}

export async function updateServiceCategory(
  id: string,
  payload: UpdateServiceCategoryPayload,
): Promise<ServiceCategoryDto> {
  const { data } = await catalogClient.put<ApiResponse<ServiceCategoryDto>>(
    `${ADMIN}/service-categories/${id}`,
    payload,
  )
  return unwrap(data)
}

export async function deleteServiceCategory(id: string): Promise<void> {
  await catalogClient.delete(`${ADMIN}/service-categories/${id}`)
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

export async function createService(payload: CreateServicePayload): Promise<ServiceDto> {
  const { data } = await catalogClient.post<ApiResponse<ServiceDto>>(`${ADMIN}/services`, payload)
  return unwrap(data)
}

export async function updateService(
  id: string,
  payload: UpdateServicePayload,
): Promise<ServiceDto> {
  const { data } = await catalogClient.put<ApiResponse<ServiceDto>>(`${ADMIN}/services/${id}`, payload)
  return unwrap(data)
}

export async function deleteService(id: string): Promise<void> {
  await catalogClient.delete(`${ADMIN}/services/${id}`)
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

export async function createItem(payload: CreateItemPayload): Promise<ItemDto> {
  const { data } = await catalogClient.post<ApiResponse<ItemDto>>(`${ADMIN}/items`, payload)
  return unwrap(data)
}

export async function updateItem(id: string, payload: UpdateItemPayload): Promise<ItemDto> {
  const { data } = await catalogClient.put<ApiResponse<ItemDto>>(`${ADMIN}/items/${id}`, payload)
  return unwrap(data)
}

export async function deleteItem(id: string): Promise<void> {
  await catalogClient.delete(`${ADMIN}/items/${id}`)
}

// ── Item image ────────────────────────────────────────────────────────────────
// imageUrl on ItemDto holds an opaque storage key — the image itself is always
// fetched through the streaming endpoint (auth header required, hence the blob).

export async function uploadItemImage(id: string, file: File): Promise<ItemDto> {
  const form = new FormData()
  form.append('file', file)
  // The client default is application/json, which makes axios JSON-encode
  // FormData; multipart/form-data here lets the browser set the boundary.
  const { data } = await catalogClient.post<ApiResponse<ItemDto>>(
    `${ADMIN}/items/${id}/image`,
    form,
    { headers: { 'Content-Type': 'multipart/form-data' } },
  )
  return unwrap(data)
}

export async function deleteItemImage(id: string): Promise<ItemDto> {
  const { data } = await catalogClient.delete<ApiResponse<ItemDto>>(`${ADMIN}/items/${id}/image`)
  return unwrap(data)
}

export async function getItemImageBlob(id: string): Promise<Blob> {
  const { data } = await catalogClient.get<Blob>(`${ADMIN}/items/${id}/image`, {
    responseType: 'blob',
  })
  return data
}

// ── Fabric Types & Item Groups (lookups for the price-list editor) ─────────────

export async function getFabricTypes(
  params: PaginationParams = {},
): Promise<PaginatedList<FabricTypeDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<FabricTypeDto>>>(
    `${ADMIN}/fabric-types`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}

export async function getItemGroups(
  params: PaginationParams = {},
): Promise<PaginatedList<ItemGroupDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<ItemGroupDto>>>(
    `${ADMIN}/item-groups`,
    { params: { page: 1, pageSize: 100, ...params } },
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

export async function createPriceList(payload: CreatePriceListPayload): Promise<PriceListDto> {
  const { data } = await catalogClient.post<ApiResponse<PriceListDto>>(`${ADMIN}/price-lists`, payload)
  return unwrap(data)
}

export async function updatePriceList(
  id: string,
  payload: UpdatePriceListPayload,
): Promise<PriceListDto> {
  const { data } = await catalogClient.put<ApiResponse<PriceListDto>>(
    `${ADMIN}/price-lists/${id}`,
    payload,
  )
  return unwrap(data)
}

export async function publishPriceList(id: string): Promise<PriceListDto> {
  const { data } = await catalogClient.post<ApiResponse<PriceListDto>>(
    `${ADMIN}/price-lists/${id}/publish`,
    {},
  )
  return unwrap(data)
}

export async function deletePriceList(id: string): Promise<void> {
  await catalogClient.delete(`${ADMIN}/price-lists/${id}`)
}

// ── Price List Items (the priced rows inside a list) ───────────────────────────

export async function getPriceListItems(
  priceListId: string,
  params: PaginationParams = {},
): Promise<PaginatedList<PriceListItemDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<PriceListItemDto>>>(
    `${ADMIN}/price-lists/${priceListId}/items`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}

export async function createPriceListItem(
  priceListId: string,
  payload: CreatePriceListItemPayload,
): Promise<PriceListItemDto> {
  const { data } = await catalogClient.post<ApiResponse<PriceListItemDto>>(
    `${ADMIN}/price-lists/${priceListId}/items`,
    payload,
  )
  return unwrap(data)
}

export async function updatePriceListItem(
  priceListId: string,
  id: string,
  payload: UpdatePriceListItemPayload,
): Promise<PriceListItemDto> {
  const { data } = await catalogClient.put<ApiResponse<PriceListItemDto>>(
    `${ADMIN}/price-lists/${priceListId}/items/${id}`,
    payload,
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

export async function updateAdminCustomer(
  id: string,
  payload: AdminUpdateCustomerPayload,
): Promise<AdminCustomerDto> {
  const { data } = await catalogClient.put<ApiResponse<AdminCustomerDto>>(
    `${ADMIN}/customers/${id}`,
    payload,
  )
  return unwrap(data)
}

/** DELETE returns a bare { status: true } envelope (no data) — don't unwrap. */
export async function deleteAdminCustomer(id: string): Promise<void> {
  await catalogClient.delete(`${ADMIN}/customers/${id}`)
}
