/**
 * Mirrors the backend response envelope from laundryghar.Utilities.ApiResponse.ResponseUtil
 *
 * Every response is:
 *   { status: boolean, data: T | null, message?: { ... } | null }
 *
 * Paginated list responses have data shaped as PaginatedList<T>:
 *   { list: T[], hasPreviousPage: boolean, hasNextPage: boolean }
 */

export interface ApiMessage {
  errorTypeCode?: number
  errorMessage?: Record<string, string[]>
  responseMessage?: string
}

export interface ApiResponse<T> {
  status: boolean
  data: T | null
  message?: ApiMessage | null
}

export interface PaginatedList<T> {
  list: T[]
  hasPreviousPage: boolean
  hasNextPage: boolean
}

export type PaginatedResponse<T> = ApiResponse<PaginatedList<T>>
export type SingleResponse<T> = ApiResponse<T>

export interface PaginationParams {
  page?: number
  pageSize?: number
}

// ── Auth ────────────────────────────────────────────────────────────────────

export interface TokenResponse {
  accessToken: string
  refreshToken: string
  expiresInSeconds: number
  tokenType: string
}

export interface PasswordLoginRequest {
  identifier: string
  password: string
}

export interface RefreshTokenRequest {
  refreshToken: string
}

// ── Identity / Tenancy ──────────────────────────────────────────────────────

export interface BrandDto {
  id: string
  platformId: string
  code: string
  name: string
  legalName: string | null
  tagline: string | null
  currencyCode: string
  timezone: string
  status: string
  createdAt: string
  updatedAt: string
}

export interface PlatformDto {
  id: string
  code: string
  name: string
  legalName: string | null
  status: string
  createdAt: string
}

export interface FranchiseDto {
  id: string
  brandId: string
  code: string
  legalName: string
  onboardingStatus: string
  status: string
  createdAt: string
}

export interface StoreDto {
  id: string
  brandId: string
  franchiseId: string
  code: string
  name: string
  storeType: string
  city: string
  status: string
  createdAt: string
}

export interface WarehouseDto {
  id: string
  brandId: string
  franchiseId: string
  code: string
  name: string
  city: string
  status: string
  createdAt: string
}

// ── Catalog ─────────────────────────────────────────────────────────────────

export interface ServiceCategoryDto {
  id: string
  brandId: string
  code: string
  name: string
  nameLocalized: string
  description: string | null
  iconUrl: string | null
  imageUrl: string | null
  colorHex: string | null
  displayOrder: number
  isVisibleMobile: boolean
  isVisiblePos: boolean
  status: string
  createdAt: string
  updatedAt: string
}

export interface ServiceDto {
  id: string
  brandId: string
  categoryId: string
  code: string
  name: string
  nameLocalized: string
  description: string | null
  pricingModel: string
  baseTatHours: number
  expressTatHours: number
  expressMultiplier: number
  isExpressAvailable: boolean
  requiresInspection: boolean
  requiresQc: boolean
  iconUrl: string | null
  displayOrder: number
  status: string
  createdAt: string
  updatedAt: string
}

export interface ItemDto {
  id: string
  brandId: string
  itemGroupId: string | null
  code: string
  name: string
  nameLocalized: string
  description: string | null
  iconUrl: string | null
  imageUrl: string | null
  typicalWeightGrams: number | null
  requiresPerSidePrice: boolean
  aliases: string[]
  displayOrder: number
  status: string
  createdAt: string
  updatedAt: string
}

// ── Pricing ─────────────────────────────────────────────────────────────────

export interface PriceListDto {
  id: string
  brandId: string
  franchiseId: string | null
  storeId: string | null
  code: string
  name: string
  description: string | null
  currencyCode: string
  scopeType: string
  versionNumber: number
  parentPriceListId: string | null
  effectiveFrom: string
  effectiveTo: string | null
  isDefault: boolean
  isPublished: boolean
  publishedAt: string | null
  status: string
  notes: string | null
  createdAt: string
  updatedAt: string
}

export interface PriceListItemDto {
  id: string
  priceListId: string
  brandId: string
  serviceId: string
  itemId: string
  itemVariantId: string | null
  fabricTypeId: string | null
  itemGroupId: string | null
  basePrice: number
  expressPrice: number | null
  minimumQuantity: number
  taxRatePercent: number
  isTaxable: boolean
  displayLabel: string | null
  notes: string | null
  isActive: boolean
  status: string
  createdAt: string
  updatedAt: string
}

// ── Orders ──────────────────────────────────────────────────────────────────

export interface OrderItemDto {
  id: string
  serviceId: string
  itemId: string
  itemVariantId: string | null
  itemNameSnapshot: string
  serviceNameSnapshot: string
  unitPrice: number
  quantity: number
  unitOfMeasure: string
  lineSubtotal: number
  lineTotal: number
  status: string
}

export interface OrderAddonDto {
  id: string
  orderItemId: string | null
  addonId: string
  addonNameSnapshot: string
  pricingType: string
  unitPrice: number
  quantity: number
  totalCharge: number
}

export interface OrderStatusHistoryDto {
  id: string
  fromStatus: string | null
  toStatus: string
  changedAt: string
  changedByType: string
  reason: string | null
  customerNotified: boolean
}

export interface OrderNoteDto {
  id: string
  noteType: string
  visibility: string
  authorType: string
  noteText: string
  isPinned: boolean
  createdAt: string
}

export interface OrderDto {
  id: string
  createdAt: string
  orderNumber: string
  brandId: string
  storeId: string
  customerId: string
  channel: string
  orderType: string
  isExpress: boolean
  subtotal: number
  addonTotal: number
  expressSurcharge: number
  taxTotal: number
  cgst: number
  sgst: number
  grandTotal: number
  amountPaid: number
  amountDue: number | null
  currencyCode: string
  totalItems: number
  status: string
  paymentStatus: string
  placedAt: string
  updatedAt: string
  items: OrderItemDto[] | null
  addons: OrderAddonDto[] | null
  statusHistory: OrderStatusHistoryDto[] | null
}

// ── Orders list filter params ────────────────────────────────────────────────

export interface OrderListParams extends PaginationParams {
  status?: string
  storeId?: string
  dateFrom?: string
  dateTo?: string
}
