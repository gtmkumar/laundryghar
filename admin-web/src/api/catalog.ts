import { catalogClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  ServiceCategoryDto,
  ServiceDto,
  ItemDto,
  FabricTypeDto,
  CreateFabricTypePayload,
  UpdateFabricTypePayload,
  AddOnDto,
  CreateAddOnPayload,
  UpdateAddOnPayload,
  PricingMatrix,
  PricingHistoryEntry,
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
  ManagedItemDto,
  ItemStatsDto,
  SaveItemPricingPayload,
  ImportItemsPayload,
  ImportItemsResult,
  ImportParseResult,
  ParseGoogleSheetPayload,
  CreateItemGroupPayload,
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

// ── Managed items (Items page: item + per-service prices + fabric variants) ─────

export async function getManagedItems(
  params: PaginationParams & { itemGroupId?: string; search?: string } = {},
): Promise<PaginatedList<ManagedItemDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<ManagedItemDto>>>(
    `${ADMIN}/items/managed`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}

export async function getItemStats(): Promise<ItemStatsDto> {
  const { data } = await catalogClient.get<ApiResponse<ItemStatsDto>>(`${ADMIN}/items/stats`)
  return unwrap(data)
}

export async function saveItemPricing(id: string, payload: SaveItemPricingPayload): Promise<void> {
  await catalogClient.put<ApiResponse<unknown>>(`${ADMIN}/items/${id}/pricing`, payload)
}

export async function importItems(payload: ImportItemsPayload): Promise<ImportItemsResult> {
  const { data } = await catalogClient.post<ApiResponse<ImportItemsResult>>(`${ADMIN}/items/import`, payload)
  return unwrap(data)
}

/**
 * Dry-run parse of an uploaded .csv/.xlsx (≤10MB). The server detects the layout,
 * normalizes rows, and returns a preview report (no writes). `onProgress` reports
 * upload percentage for the file transfer.
 */
export async function parseImportFile(
  file: File,
  onProgress?: (pct: number) => void,
): Promise<ImportParseResult> {
  const form = new FormData()
  form.append('file', file)
  const { data } = await catalogClient.post<ApiResponse<ImportParseResult>>(
    `${ADMIN}/items/import/parse`,
    form,
    {
      headers: { 'Content-Type': 'multipart/form-data' },
      onUploadProgress: (e) => {
        if (onProgress && e.total) onProgress(Math.round((e.loaded / e.total) * 100))
      },
    },
  )
  return unwrap(data)
}

/**
 * Dry-run parse of a published Google Sheet (read via its CSV export). Returns
 * the SAME ParseImportResult as the file endpoint (always layout 'flat'), with
 * `sourceUrl` set to the sheet link. The sheet must be shared as "Anyone with
 * the link can view" — otherwise the server returns a business error.
 */
export async function parseGoogleSheet(
  payload: ParseGoogleSheetPayload,
): Promise<ImportParseResult> {
  const { data } = await catalogClient.post<ApiResponse<ImportParseResult>>(
    `${ADMIN}/items/import/parse-google-sheet`,
    payload,
  )
  return unwrap(data)
}

/** Downloads the import template (blob) in the requested format for the user to save. */
export async function downloadImportTemplate(format: 'csv' | 'xlsx'): Promise<Blob> {
  const { data } = await catalogClient.get<Blob>(`${ADMIN}/items/import/template`, {
    params: { format },
    responseType: 'blob',
  })
  return data
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

export async function createFabricType(payload: CreateFabricTypePayload): Promise<FabricTypeDto> {
  const { data } = await catalogClient.post<ApiResponse<FabricTypeDto>>(`${ADMIN}/fabric-types`, payload)
  return unwrap(data)
}
export async function updateFabricType(id: string, payload: UpdateFabricTypePayload): Promise<FabricTypeDto> {
  const { data } = await catalogClient.put<ApiResponse<FabricTypeDto>>(`${ADMIN}/fabric-types/${id}`, payload)
  return unwrap(data)
}

// ── Add-ons / surcharges ──────────────────────────────────────────────────────
export async function getAddOns(params: PaginationParams = {}): Promise<PaginatedList<AddOnDto>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<AddOnDto>>>(
    `${ADMIN}/add-ons`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
}
export async function createAddOn(payload: CreateAddOnPayload): Promise<AddOnDto> {
  const { data } = await catalogClient.post<ApiResponse<AddOnDto>>(`${ADMIN}/add-ons`, payload)
  return unwrap(data)
}
export async function updateAddOn(id: string, payload: UpdateAddOnPayload): Promise<AddOnDto> {
  const { data } = await catalogClient.put<ApiResponse<AddOnDto>>(`${ADMIN}/add-ons/${id}`, payload)
  return unwrap(data)
}
export async function deleteAddOn(id: string): Promise<void> {
  await catalogClient.delete(`${ADMIN}/add-ons/${id}`)
}

// ── Price matrix ──────────────────────────────────────────────────────────────
export async function getPricingMatrix(storeId?: string): Promise<PricingMatrix> {
  const { data } = await catalogClient.get<ApiResponse<PricingMatrix>>(
    `${ADMIN}/pricing/matrix`,
    { params: storeId ? { storeId } : {} },
  )
  return unwrap(data)
}

// ── Pricing change history ────────────────────────────────────────────────────
export async function getPricingHistory(params: PaginationParams = {}): Promise<PaginatedList<PricingHistoryEntry>> {
  const { data } = await catalogClient.get<ApiResponse<PaginatedList<PricingHistoryEntry>>>(
    `${ADMIN}/pricing/history`,
    { params: { page: 1, pageSize: 30, ...params } },
  )
  return unwrapPaginated(data)
}
export async function revertPricingChange(id: string): Promise<void> {
  await catalogClient.post(`${ADMIN}/pricing/history/${id}/revert`, {})
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

export async function createItemGroup(payload: CreateItemGroupPayload): Promise<ItemGroupDto> {
  const { data } = await catalogClient.post<ApiResponse<ItemGroupDto>>(`${ADMIN}/item-groups`, payload)
  return unwrap(data)
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
