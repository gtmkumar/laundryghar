/**
 * Mirrors the backend response envelope.
 * Every response: { status: boolean, data: T | null, message?: {...} | null }
 * Paginated: data = { list: T[], hasPreviousPage: boolean, hasNextPage: boolean }
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

// ── Tenancy ─────────────────────────────────────────────────────────────────

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

export interface PriceResolutionDto {
  itemId: string
  serviceId: string
  itemVariantId: string | null
  storeId: string | null
  basePrice: number
  expressPrice: number | null
  taxRatePercent: number
  isTaxable: boolean
  priceListItemId: string
  itemNameSnapshot: string
  serviceNameSnapshot: string
}

// ── Customers (admin lane) ───────────────────────────────────────────────────

export interface AdminCustomerDto {
  id: string
  brandId: string
  customerCode: string
  phoneE164: string
  email: string | null
  firstName: string | null
  lastName: string | null
  displayName: string | null
  gender: string | null
  dateOfBirth: string | null
  locale: string
  timezone: string
  lifetimeOrders: number
  lifetimeSpend: number
  loyaltyPointsBalance: number
  walletBalance: number
  customerSegment: string | null
  riskFlag: string | null
  status: string
  createdAt: string
  updatedAt: string
}

export interface AdminCustomerListParams extends PaginationParams {
  status?: string
  search?: string
}

// ── Orders ──────────────────────────────────────────────────────────────────

export interface CreateOrderItemRequest {
  itemId: string
  itemVariantId?: string | null
  serviceId: string
  quantity: number
  notes?: string | null
}

export interface CreateOrderAddonRequest {
  addonId: string
  orderItemIndex?: number | null
  quantity: number
}

export interface CreateOrderRequest {
  customerId: string
  storeId: string
  channel: string
  isExpress: boolean
  requiresPickup: boolean
  requiresDelivery: boolean
  pickupAddressId?: string | null
  deliveryAddressId?: string | null
  items: CreateOrderItemRequest[]
  addons: CreateOrderAddonRequest[]
  notesCustomer?: string | null
  couponCode?: string | null
  /**
   * POS-2: client-generated idempotency key. Sent both in the body and as the
   * `Idempotency-Key` header so a double-tap / axios retry can't create a
   * duplicate order. The backend dedupes on it and returns the original order.
   */
  idempotencyKey?: string | null
}

export interface UpdateOrderStatusRequest {
  toStatus: string
  reason?: string | null
  notes?: string | null
  customerNotified: boolean
}

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
  discountTotal: number
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
  /** Marketplace job kind (laundry/parcel) — drives the fulfilment mode for backend-driven
   *  status labels via the fulfilment-config endpoint. Optional until the DTO carries it. */
  jobType?: string
  /**
   * Valid next statuses computed by the backend OrderStateMachine, returned on
   * GET /api/v1/admin/orders/{id}. Optional: older API builds omit it, in
   * which case the UI falls back to the local nextStatuses() mirror.
   */
  allowedTransitions?: string[] | null
}

// ---------------------------------------------------------------------------
// Fulfilment config — backend-driven status labels (multi-vertical Phase 3)
// GET /api/v1/fulfillment-config — one entry per fulfilment mode, built live from the
// backend strategies, so the POS labels statuses per the order's vertical rather than a
// hardcoded laundry ladder.
// ---------------------------------------------------------------------------

/** One stage in a fulfilment mode's happy path. Backend: FulfillmentStageDto. */
export interface FulfillmentStageDto {
  status: string
  label: string
  order: number
  lifecycleState: string
}

/** The client-consumable configuration of one fulfilment mode. Backend: FulfillmentConfigDto. */
export interface FulfillmentConfigDto {
  fulfillmentMode: string
  initialStatus: string
  stages: FulfillmentStageDto[]
  terminalStatuses: string[]
  requiresStoreDrop: boolean
  requiresPickup: boolean
  requiresDelivery: boolean
}

export interface OrderListParams extends PaginationParams {
  status?: string
  storeId?: string
  dateFrom?: string
  dateTo?: string
}

// ── Finance / Cash Books ────────────────────────────────────────────────────

export interface OpenCashBookRequest {
  storeId: string
  franchiseId: string
  bookDate: string          // "YYYY-MM-DD"
  shiftLabel: string        // morning|afternoon|evening|night|full_day
  openingBalance: number
}

export interface AddCashBookEntryRequest {
  entryType: string         // cash_in|cash_out|deposit|withdrawal|adjustment|opening|closing
  category: string          // order_payment|refund|expense|salary|utility|rent|maintenance|supply|tip|adjustment|deposit|other
  direction: number         // 1 = in, -1 = out
  amount: number
  paymentMode: string       // cash|upi|card|bank_transfer|other
  description?: string | null
  payeeName?: string | null
  receiptNumber?: string | null
  expenseId?: string | null
}

export interface CloseCashBookRequest {
  closingBalance: number
  varianceReason?: string | null
  notes?: string | null
}

export interface CashBookEntryDto {
  id: string
  cashBookId: string
  entryType: string
  category: string
  direction: number
  amount: number
  paymentMode: string
  description: string | null
  payeeName: string | null
  receiptNumber: string | null
  expenseId: string | null
  occurredAt: string
  createdAt: string
}

export interface CashBookDto {
  id: string
  brandId: string
  franchiseId: string
  storeId: string
  bookDate: string
  shiftLabel: string
  openingBalance: number
  closingBalance: number | null
  expectedClosing: number | null
  variance: number | null
  cashInflow: number
  cashOutflow: number
  upiInflow: number
  cardInflow: number
  otherInflow: number
  depositAmount: number
  totalOrders: number
  status: string
  notes: string | null
  openedAt: string
  closedAt: string | null
  createdAt: string
  entries: CashBookEntryDto[]
}

export interface CashBookSummaryDto {
  id: string
  storeId: string
  bookDate: string
  shiftLabel: string
  openingBalance: number
  closingBalance: number | null
  variance: number | null
  cashInflow: number
  cashOutflow: number
  status: string
  openedAt: string
  closedAt: string | null
}

export interface CashBookListParams extends PaginationParams {
  storeId?: string
  status?: string
  bookDate?: string
}

// ── Offline payment (Commerce admin lane) ────────────────────────────────────

export interface RecordOfflinePaymentRequest {
  orderId: string
  method: 'cash' | 'upi' | 'card'
  amount: number
  reference?: string | null
  /**
   * POS-2: client-generated idempotency key for the payment-record attempt
   * (sent in the body and the `Idempotency-Key` header). Prevents a double
   * charge on retry. Distinct key per attempt.
   */
  idempotencyKey?: string | null
}

export interface OfflinePaymentDto {
  paymentId: string
  orderId: string
  method: string
  amount: number
  reference: string | null
  orderPaymentStatus: string
  orderAmountPaid: number
  orderAmountDue: number | null
}

// ── Admin create customer ─────────────────────────────────────────────────────

export interface AdminCreateCustomerRequest {
  phone: string
  firstName?: string | null
  lastName?: string | null
  email?: string | null
}
