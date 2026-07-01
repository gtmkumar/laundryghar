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
  totalCount?: number
  pageNumber?: number
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

// ── Step-up (§8 sensitive-action re-verification) ─────────────────────────────
// A high/critical action can 403 with responseMessage="step_up_required"; the
// user must re-verify a fresh OTP, then retry with the upgraded access token.

export type StepUpIdentifierType = 'phone' | 'email'

/** POST /api/v1/auth/otp/send response (purpose=sensitive_action). */
export interface OtpSentResponse {
  message: string
  expiresAt: string
}

/**
 * POST /api/v1/auth/step-up/verify response — an UPGRADED access token carrying
 * fresh amr+stepup_at claims. NOTE: no refresh token is issued (this is a
 * re-verification, not a login), so only the access token is swapped.
 */
export interface StepUpTokenResponse {
  accessToken: string
  expiresInSeconds: number
  tokenType: string
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
  /** The brand's industry vertical (laundry/salon/logistics) — drives client-side vertical gating
   *  and terminology (see useActiveVertical / lib/verticalTerms). Populated by the backend BrandDto;
   *  kept optional for backward-compat with brands persisted in the store before this field existed. */
  verticalKey?: string
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

export type StoreType = 'walkin' | 'pickup_only' | 'express' | 'hub' | 'collection_point'

export type StoreStatus = 'active' | 'paused' | 'closed' | 'coming_soon'

export interface CreateStorePayload {
  brandId: string
  franchiseId: string
  code: string
  name: string
  addressLine1: string
  city: string
  state: string
  pincode: string
  storeType: StoreType
}

export interface UpdateStorePayload {
  name?: string
  status?: StoreStatus
  contactPhone?: string
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

export type WarehouseType = 'central' | 'satellite' | 'express' | 'specialty'

export type WarehouseStatus = 'active' | 'paused' | 'maintenance' | 'closed'

export interface CreateWarehousePayload {
  brandId: string
  franchiseId: string
  code: string
  name: string
  addressLine1: string
  city: string
  state: string
  pincode: string
  warehouseType: WarehouseType
}

export interface UpdateWarehousePayload {
  name?: string
  status?: WarehouseStatus
  contactPhone?: string
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
  tatHours: number | null
  expressEligible: boolean
  expressSurcharge: number | null
}

// ── Managed items (Items page aggregate) ──────────────────────────────────────

export interface ItemServicePrice {
  serviceId: string
  basePrice: number
}

export interface ManagedItemDto {
  id: string
  itemGroupId: string | null
  itemGroupName: string | null
  code: string
  name: string
  nameLocalized: string
  description: string | null
  typicalWeightGrams: number | null
  tatHours: number | null
  expressEligible: boolean
  expressSurcharge: number | null
  aliases: string[]
  displayOrder: number
  status: string
  updatedAt: string
  fabricTypeIds: string[]
  servicePrices: ItemServicePrice[]
}

export interface ItemStatsDto {
  totalItems: number
  categoryCount: number
  activeItems: number
  draftItems: number
  avgTatHours: number
}

export interface SaveItemPricingPayload {
  servicePrices: { serviceId: string; basePrice: number | null }[]
  fabricTypeIds: string[]
}

export interface ImportItemRowPayload {
  code: string
  name: string
  category: string | null
  status: string | null
  tatHours: number | null
  servicePrices: { serviceName: string; basePrice: number | null }[]
}

export interface ImportItemsPayload {
  rows: ImportItemRowPayload[]
}

export interface ImportItemsResult {
  created: number
  updated: number
  pricesSet: number
  errors: string[]
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

export interface FabricTypeDto {
  id: string
  brandId: string
  code: string
  name: string
  nameLocalized: string
  description: string | null
  careInstructions: string | null
  priceMultiplier: number
  requiresSpecialCare: boolean
  displayOrder: number
  status: string
  createdAt: string
  updatedAt: string
}

// Full-replace request (backend UpdateFabricTypeRequest sets every field).
export interface UpdateFabricTypePayload {
  name: string
  nameLocalized: string
  description?: string | null
  careInstructions?: string | null
  priceMultiplier: number
  requiresSpecialCare: boolean
  displayOrder: number
  status: string
}

export interface CreateFabricTypePayload {
  code: string
  name: string
  nameLocalized: string
  description?: string | null
  careInstructions?: string | null
  priceMultiplier: number
  requiresSpecialCare: boolean
  displayOrder: number
}

// ── Add-ons / surcharges ─────────────────────────────────────────────────────
export interface AddOnDto {
  id: string
  brandId: string
  code: string
  name: string
  nameLocalized: string | null
  description: string | null
  pricingType: string // 'flat' | 'percent' | 'per_kg'
  priceValue: number
  minCharge: number | null
  maxCharge: number | null
  applicableServices: string[]
  applicableCategories: string[]
  isTaxable: boolean
  taxRatePercent: number
  requiresApproval: boolean
  iconUrl: string | null
  displayOrder: number
  status: string
  createdAt: string
  updatedAt: string
}

// Full-replace requests (backend Create/UpdateAddOnRequest set every field).
export interface CreateAddOnPayload {
  code: string
  name: string
  nameLocalized: string
  description?: string | null
  pricingType: string
  priceValue: number
  minCharge?: number | null
  maxCharge?: number | null
  applicableServices?: string[]
  applicableCategories?: string[]
  isTaxable: boolean
  taxRatePercent: number
  requiresApproval: boolean
  iconUrl?: string | null
  displayOrder: number
}

export interface UpdateAddOnPayload {
  name: string
  nameLocalized: string
  description?: string | null
  pricingType: string
  priceValue: number
  minCharge?: number | null
  maxCharge?: number | null
  applicableServices?: string[]
  applicableCategories?: string[]
  isTaxable: boolean
  taxRatePercent: number
  requiresApproval: boolean
  iconUrl?: string | null
  displayOrder: number
  status: string
}

// ── Price matrix ─────────────────────────────────────────────────────────────
export interface PricingMatrixFabric { code: string; name: string; multiplier: number }
export interface PricingMatrixRow { label: string; basePrice: number }
export interface PricingMatrixStore { id: string; name: string }
export interface PricingMatrix {
  priceListName: string | null
  scopeType: string | null
  fabrics: PricingMatrixFabric[]
  rows: PricingMatrixRow[]
  stores: PricingMatrixStore[]
}

export interface PricingHistoryEntry {
  id: string
  targetKind: string
  targetId: string
  summary: string
  actorName: string | null
  createdAt: string
  revertedAt: string | null
}

export interface ItemGroupDto {
  id: string
  brandId: string
  code: string
  name: string
  nameLocalized: string
  iconUrl: string | null
  displayOrder: number
  isVisibleMobile: boolean
  status: string
  createdAt: string
  updatedAt: string
}

export interface CreateItemGroupPayload {
  code: string
  name: string
  nameLocalized: string
  iconUrl: string | null
  displayOrder: number
  isVisibleMobile: boolean
}

// ── Catalog write payloads ────────────────────────────────────────────────────
// `nameLocalized` is a jsonb column — serialize as a JSON-object string
// (`{"en":"…","hi":"…"}`) on submit. A bare string 400s with 22P02.

export interface CreateServiceCategoryPayload {
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
  requiresWarehouseCap: string[]
}

export interface UpdateServiceCategoryPayload {
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
}

export interface CreateServicePayload {
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
}

export interface UpdateServicePayload {
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
}

export interface CreateItemPayload {
  itemGroupId: string | null
  code: string
  name: string
  nameLocalized: string
  description: string | null
  iconUrl: string | null
  imageUrl: string | null
  typicalWeightGrams: number | null
  requiresPerSidePrice: boolean
  aliases: string[] | null
  displayOrder: number
  tatHours?: number | null
  expressEligible?: boolean
  expressSurcharge?: number | null
}

export interface UpdateItemPayload {
  itemGroupId: string | null
  name: string
  nameLocalized: string
  description: string | null
  iconUrl: string | null
  imageUrl: string | null
  typicalWeightGrams: number | null
  requiresPerSidePrice: boolean
  aliases: string[] | null
  displayOrder: number
  status: string
  tatHours?: number | null
  expressEligible?: boolean
  expressSurcharge?: number | null
}

// ── Pricing write payloads ────────────────────────────────────────────────────

export interface CreatePriceListPayload {
  code: string
  name: string
  description: string | null
  currencyCode: string
  scopeType: string
  franchiseId: string | null
  storeId: string | null
  parentPriceListId: string | null
  effectiveFrom: string
  effectiveTo: string | null
  isDefault: boolean
  notes: string | null
}

export interface UpdatePriceListPayload {
  name: string
  description: string | null
  effectiveFrom: string
  effectiveTo: string | null
  isDefault: boolean
  notes: string | null
  status: string
}

export interface CreatePriceListItemPayload {
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
}

export interface UpdatePriceListItemPayload {
  basePrice: number
  expressPrice: number | null
  minimumQuantity: number
  taxRatePercent: number
  isTaxable: boolean
  displayLabel: string | null
  notes: string | null
  isActive: boolean
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
  /** Marketplace job kind (laundry/parcel) — drives the fulfilment mode for backend-driven
   *  status labels. Optional until the DTO carries it. (Multi-vertical Phase 3.) */
  jobType?: string
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
  /** TAT-computed delivery promise. Null for legacy orders created before this feature. */
  promisedDeliveryAt: string | null
  items: OrderItemDto[] | null
  addons: OrderAddonDto[] | null
  statusHistory: OrderStatusHistoryDto[] | null
}

// ── Ops queues ────────────────────────────────────────────────────────────────

export interface OpsOrderDto {
  id: string
  createdAt: string
  orderNumber: string
  customerName: string
  status: string
  promisedDeliveryAt: string | null
  /** Hours the order is overdue (positive). Null when not overdue. */
  hoursOverdue: number | null
  /** Hours since last status history entry. Populated in the stuck queue. */
  hoursStuck: number | null
  /** Owning store id — join a store name without an extra fetch. */
  storeId: string
  /** Minutes since the order was created — drives the "needs action" age badge. */
  ageMinutes: number
}

export interface OpsQueueBucket {
  count: number
  list: OpsOrderDto[]
  hasNextPage: boolean
  totalCount: number
}

export interface OpsQueuesResponse {
  dueToday: OpsQueueBucket
  overdue: OpsQueueBucket
  stuck: OpsQueueBucket
  /** Orders still in 'placed' with no pickup scheduled — the "needs action" queue. */
  unactioned: OpsQueueBucket
}

export interface OpsQueuesParams {
  page?: number
  pageSize?: number
  storeId?: string
}

// ── Order note create request ────────────────────────────────────────────────
// Mirrors laundryghar.Orders CreateOrderNoteRequest. Valid noteType:
//   internal | customer_facing | complaint | resolution | flag
// Valid visibility: staff | customer | platform
export interface CreateOrderNoteRequest {
  noteType: string
  visibility: string
  noteText: string
  isPinned: boolean
}

// ── Invoice ───────────────────────────────────────────────────────────────────
// Mirrors laundryghar.Orders.Application.Invoices.Dtos.InvoiceDto.

export interface InvoiceLineItemDto {
  description: string
  qty: number
  unit: string
  unitPrice: number
  taxableValue: number
}

export interface InvoiceDto {
  id: string
  orderId: string
  invoiceNumber: string
  invoiceDate: string
  supplierName: string
  supplierAddress: string
  supplierGstin: string | null
  customerName: string
  customerPhone: string
  customerGstin: string | null
  placeOfSupply: string
  sacCode: string
  lineItems: InvoiceLineItemDto[]
  subtotal: number
  discountTotal: number
  taxableTotal: number
  cgstRate: number
  cgstAmount: number
  sgstRate: number
  sgstAmount: number
  igstRate: number
  igstAmount: number
  roundOff: number
  grandTotal: number
  status: string
  createdAt: string
}

// ── Orders list filter params ────────────────────────────────────────────────

export interface OrderListParams extends PaginationParams {
  status?: string
  storeId?: string
  dateFrom?: string
  dateTo?: string
  /** Server-side split: 'active' = non-terminal, 'history' = terminal. Ignored when `status` is set. */
  statusGroup?: 'active' | 'history'
}

// ── CMS / Engagement ─────────────────────────────────────────────────────────

export interface NotificationTemplateDto {
  id: string
  brandId: string
  code: string
  name: string
  description: string | null
  channel: string
  category: string
  locale: string
  subjectTemplate: string | null
  bodyTemplate: string
  smsSenderId: string | null
  whatsAppTemplateName: string | null
  whatsAppTemplateId: string | null
  pushTitleTemplate: string | null
  pushActionDeeplink: string | null
  variables: string
  versionNumber: number
  isTransactional: boolean
  isActive: boolean
  approvedAt: string | null
  status: string
  createdAt: string
  updatedAt: string
}

export interface CreateNotificationTemplateRequest {
  code: string
  name: string
  description?: string | null
  channel: string
  category: string
  locale: string
  subjectTemplate?: string | null
  bodyTemplate: string
  smsSenderId?: string | null
  whatsAppTemplateName?: string | null
  whatsAppTemplateId?: string | null
  whatsAppLangCode?: string | null
  whatsAppNamespace?: string | null
  pushTitleTemplate?: string | null
  pushActionDeeplink?: string | null
  pushIconUrl?: string | null
  pushSound?: string | null
  variables: string
  versionNumber: number
  isTransactional: boolean
  isActive: boolean
}

export interface UpdateNotificationTemplateRequest {
  name: string
  description?: string | null
  subjectTemplate?: string | null
  bodyTemplate: string
  smsSenderId?: string | null
  whatsAppTemplateName?: string | null
  whatsAppTemplateId?: string | null
  whatsAppLangCode?: string | null
  whatsAppNamespace?: string | null
  pushTitleTemplate?: string | null
  pushActionDeeplink?: string | null
  pushIconUrl?: string | null
  pushSound?: string | null
  variables: string
  isTransactional: boolean
  isActive: boolean
  status: string
}

export interface OnboardingSlideDto {
  id: string
  brandId: string
  appType: string
  title: string
  titleLocalized: string
  description: string | null
  descriptionLocalized: string
  imageUrl: string
  imageDarkUrl: string | null
  animationUrl: string | null
  ctaText: string | null
  ctaDeeplink: string | null
  backgroundColor: string | null
  textColor: string | null
  displayOrder: number
  isActive: boolean
  showFrom: string | null
  showUntil: string | null
  minAppVersion: string | null
  maxAppVersion: string | null
  targetSegments: string[] | null
  status: string
  createdAt: string
  updatedAt: string
}

export interface CreateOnboardingSlideRequest {
  appType: string
  title: string
  titleLocalized: string
  description?: string | null
  descriptionLocalized: string
  imageUrl: string
  imageDarkUrl?: string | null
  animationUrl?: string | null
  ctaText?: string | null
  ctaDeeplink?: string | null
  backgroundColor?: string | null
  textColor?: string | null
  displayOrder: number
  isActive: boolean
  showFrom?: string | null
  showUntil?: string | null
  minAppVersion?: string | null
  maxAppVersion?: string | null
  targetSegments?: string[] | null
}

export interface UpdateOnboardingSlideRequest {
  appType: string
  title: string
  titleLocalized: string
  description?: string | null
  descriptionLocalized: string
  imageUrl: string
  imageDarkUrl?: string | null
  animationUrl?: string | null
  ctaText?: string | null
  ctaDeeplink?: string | null
  backgroundColor?: string | null
  textColor?: string | null
  displayOrder: number
  isActive: boolean
  showFrom?: string | null
  showUntil?: string | null
  minAppVersion?: string | null
  maxAppVersion?: string | null
  targetSegments?: string[] | null
  status: string
}

export interface AppBannerDto {
  id: string
  brandId: string
  appType: string
  placement: string
  title: string | null
  titleLocalized: string
  subtitle: string | null
  subtitleLocalized: string
  imageUrl: string
  imageDarkUrl: string | null
  ctaText: string | null
  ctaDeeplink: string | null
  externalUrl: string | null
  promotionId: string | null
  couponId: string | null
  backgroundColor: string | null
  displayOrder: number
  isActive: boolean
  showFrom: string | null
  showUntil: string | null
  targetAudience: string | null
  targetSegments: string[] | null
  targetCities: string[] | null
  impressionsCount: number
  clicksCount: number
  minAppVersion: string | null
  status: string
  createdAt: string
  updatedAt: string
}

export interface CreateAppBannerRequest {
  appType: string
  placement: string
  title?: string | null
  titleLocalized: string
  subtitle?: string | null
  subtitleLocalized: string
  imageUrl: string
  imageDarkUrl?: string | null
  ctaText?: string | null
  ctaDeeplink?: string | null
  externalUrl?: string | null
  promotionId?: string | null
  couponId?: string | null
  backgroundColor?: string | null
  displayOrder: number
  isActive: boolean
  showFrom?: string | null
  showUntil?: string | null
  targetAudience?: string | null
  targetSegments?: string[] | null
  targetCities?: string[] | null
  minAppVersion?: string | null
}

export interface UpdateAppBannerRequest {
  appType: string
  placement: string
  title?: string | null
  titleLocalized: string
  subtitle?: string | null
  subtitleLocalized: string
  imageUrl: string
  imageDarkUrl?: string | null
  ctaText?: string | null
  ctaDeeplink?: string | null
  externalUrl?: string | null
  promotionId?: string | null
  couponId?: string | null
  backgroundColor?: string | null
  displayOrder: number
  isActive: boolean
  showFrom?: string | null
  showUntil?: string | null
  targetAudience?: string | null
  targetSegments?: string[] | null
  targetCities?: string[] | null
  minAppVersion?: string | null
  status: string
}

export interface MobileAppConfigDto {
  id: string
  brandId: string
  appType: string
  platform: string
  configKey: string
  configValue: string
  description: string | null
  isForceUpdate: boolean
  minAppVersion: string | null
  maxAppVersion: string | null
  targetSegments: string[] | null
  rolloutPercent: number | null
  isActive: boolean
  status: string
  createdAt: string
  updatedAt: string
}

export interface CreateMobileAppConfigRequest {
  appType: string
  platform: string
  configKey: string
  configValue: string
  description?: string | null
  isForceUpdate: boolean
  minAppVersion?: string | null
  maxAppVersion?: string | null
  targetSegments?: string[] | null
  rolloutPercent?: number | null
  isActive: boolean
}

export interface UpdateMobileAppConfigRequest {
  appType: string
  platform: string
  configKey: string
  configValue: string
  description?: string | null
  isForceUpdate: boolean
  minAppVersion?: string | null
  maxAppVersion?: string | null
  targetSegments?: string[] | null
  rolloutPercent?: number | null
  isActive: boolean
  status: string
}

export interface NotificationOutboxDto {
  id: string
  brandId: string
  templateId: string | null
  templateCode: string
  channel: string
  locale: string
  recipientType: string
  recipientId: string | null
  recipientPhone: string | null
  recipientEmail: string | null
  body: string
  subject: string | null
  priority: number
  scheduledAt: string
  expiresAt: string | null
  attempts: number
  maxAttempts: number
  lastAttemptAt: string | null
  lastError: string | null
  sentAt: string | null
  provider: string | null
  providerMessageId: string | null
  status: string
  suppressionReason: string | null
  createdAt: string
}

export interface NotificationLogDto {
  id: string
  sentAt: string
  brandId: string
  outboxId: string | null
  channel: string
  templateCode: string | null
  recipientType: string
  recipientId: string | null
  recipientAddress: string | null
  provider: string | null
  providerMessageId: string | null
  status: string
  deliveredAt: string | null
  readAt: string | null
  clickedAt: string | null
  failureCode: string | null
  failureMessage: string | null
  cost: number | null
  referenceType: string | null
  referenceId: string | null
  createdAt: string
}

export interface WhatsAppMessageLogDto {
  id: string
  brandId: string
  direction: string
  customerId: string | null
  userId: string | null
  phoneE164: string
  provider: string
  waMessageId: string | null
  waConversationId: string | null
  templateName: string | null
  messageType: string | null
  bodyText: string | null
  referenceType: string | null
  referenceId: string | null
  status: string | null
  sentAt: string
  deliveredAt: string | null
  readAt: string | null
  failedAt: string | null
  errorCode: string | null
  errorMessage: string | null
  createdAt: string
}

export interface CmsListParams extends PaginationParams {
  status?: string
  channel?: string
  direction?: string
}

// ── Analytics ────────────────────────────────────────────────────────────────

export interface DailyStoreRevenueDto {
  brandId: string
  franchiseId: string
  storeId: string
  revenueDate: string          // DateOnly serializes as "YYYY-MM-DD"
  ordersCount: number
  deliveredOrders: number
  cancelledOrders: number
  expressOrders: number
  grossRevenue: number
  collectedAmount: number
  outstandingAmount: number
  refundAmount: number
  totalDiscount: number
  totalTax: number
  avgOrderValue: number
  uniqueCustomers: number
}

export interface MonthlyFranchiseRevenueDto {
  brandId: string
  franchiseId: string
  revenueMonth: string         // DateOnly "YYYY-MM-DD"
  ordersCount: number
  uniqueCustomers: number
  grossRevenue: number
  netRevenue: number
  collectedAmount: number
  refundAmount: number
  totalTax: number
  avgOrderValue: number
  expressOrders: number
}

export interface WarehouseThroughputDto {
  brandId: string
  warehouseId: string
  throughputDate: string       // DateOnly "YYYY-MM-DD"
  garmentsReceived: number
  garmentsDelivered: number
  issuesCount: number
  rewashCount: number
  avgTatHours: number | null   // null when no garments have completed a turnaround yet
}

export interface CustomerLtvDto {
  brandId: string
  customerId: string
  customerSegment: string | null
  lifetimeOrders: number
  lifetimeRevenue: number
  avgOrderValue: number
  firstOrderAt: string
  lastOrderAt: string
  daysSinceLastOrder: number
  expressOrders: number
  cancelledOrders: number
  activePackages: number
  loyaltyPointsBalance: number
  walletBalance: number
}

export interface RiderPerformanceDto {
  brandId: string
  franchiseId: string
  riderId: string
  riderCode: string
  perfDate: string             // DateOnly "YYYY-MM-DD"
  assignmentsTotal: number
  assignmentsCompleted: number
  assignmentsFailed: number
  pickupsDone: number
  deliveriesDone: number
  totalKm: number
  avgDurationMin: number
  ratingAverage: number
  completionRate: number
}

export interface AnalyticsDashboardToday {
  ordersCount: number
  grossRevenue: number
  collectedAmount: number
  uniqueCustomers: number
}

export interface AnalyticsDashboardThisMonth {
  ordersCount: number
  grossRevenue: number
  netRevenue: number
}

export interface AnalyticsDashboardTopCustomer {
  customerId: string
  customerSegment: string | null
  lifetimeRevenue: number
  lifetimeOrders: number
}

export interface AnalyticsDashboard {
  today: AnalyticsDashboardToday
  thisMonth: AnalyticsDashboardThisMonth
  topCustomersByLtv: AnalyticsDashboardTopCustomer[]
}

export interface RefreshResultItem {
  view: string
  success: boolean
  error: string | null
}

export interface AnalyticsListParams extends PaginationParams {
  storeId?: string
  franchiseId?: string
  warehouseId?: string
  from?: string
  to?: string
  year?: number
}

// ── Catalog Admin Customers ──────────────────────────────────────────────────

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

// ── Finance: Cash books & Expenses ──────────────────────────────────────────

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

/** Full cash-book detail (GET /cash-books/{id}) — includes line entries. */
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

export interface CloseCashBookPayload {
  closingBalance: number
  varianceReason?: string | null
  notes?: string | null
}

export interface ShiftHandoverDto {
  id: string
  storeId: string
  fromUserId: string
  toUserId: string | null
  cashBookId: string | null
  handoverAt: string
  cashHandedOver: number
  cashVariance: number | null
  status: string
  notesFrom: string | null
  createdAt: string
}

export interface ShiftHandoverListParams extends PaginationParams {
  storeId?: string
  status?: string
}

export interface CreateShiftHandoverPayload {
  storeId: string
  fromUserId: string
  toUserId?: string | null
  cashHandedOver: number
  pendingOrdersCount: number
  openComplaintsCount: number
  pickupsRemaining: number
  deliveriesRemaining: number
  notesFrom?: string | null
  cashBookId?: string | null
}

export interface ExpenseDto {
  id: string
  brandId: string
  franchiseId: string
  storeId: string | null
  categoryId: string
  categoryName: string
  expenseNumber: string
  expenseDate: string
  amount: number
  taxAmount: number
  totalAmount: number | null
  paymentMode: string
  vendorName: string | null
  billNumber: string | null
  description: string
  notes: string | null
  isRecurring: boolean
  recurrenceFrequency: string | null
  isReimbursable: boolean
  status: string
  submittedAt: string
  approvedAt: string | null
  paidAt: string | null
  rejectionReason: string | null
  createdAt: string
}

export interface ExpenseCategoryDto {
  id: string
  brandId: string
  parentId: string | null
  code: string
  name: string
  description: string | null
  isTaxDeductible: boolean
  requiresApproval: boolean
  approvalThreshold: number | null
  accountingCode: string | null
  displayOrder: number
  isActive: boolean
  status: string
  createdAt: string
}

export interface CreateExpensePayload {
  franchiseId: string
  storeId?: string | null
  warehouseId?: string | null
  categoryId: string
  expenseDate: string
  amount: number
  taxAmount: number
  paymentMode: string
  description: string
  vendorName?: string | null
  vendorGstin?: string | null
  vendorPhone?: string | null
  billNumber?: string | null
  billDate?: string | null
  notes?: string | null
  isRecurring: boolean
  recurrenceFrequency?: string | null
  isReimbursable: boolean
  requiresApproval: boolean
  submitNow: boolean
}

export interface OpenCashBookPayload {
  storeId: string
  franchiseId: string
  bookDate: string
  shiftLabel: string
  openingBalance: number
}

export interface CashBookListParams extends PaginationParams {
  storeId?: string
  status?: string
  bookDate?: string
}

export interface ExpenseListParams extends PaginationParams {
  status?: string
  categoryId?: string
  storeId?: string
}

export interface AdminUpdateCustomerPayload {
  firstName?: string | null
  lastName?: string | null
  email?: string | null
  gender?: string | null
  dateOfBirth?: string | null
  customerSegment?: string | null
  riskFlag?: string | null
}

// ── Commerce (Promotions / Coupons — for banner picker) ──────────────────────

export interface PromotionDto {
  id: string
  brandId: string
  code: string
  name: string
  description: string | null
  promotionType: string
  targetAudience: string
  eligibleSegments: string[] | null
  rules: string
  rewardConfig: string
  couponId: string | null
  bannerImageUrl: string | null
  deeplinkUrl: string | null
  validFrom: string
  validUntil: string | null
  totalBudget: number | null
  spentBudget: number
  redemptionsCount: number
  status: string
  createdAt: string
  updatedAt: string
}

/**
 * POST /admin/promotions body. `rules` and `rewardConfig` are JSON strings the
 * order pipeline reads. `rewardConfig` is shaped
 * `{ discount_type: 'percent'|'flat', discount_value: number, max_discount?: number }`.
 */
export interface CreatePromotionPayload {
  code: string
  name: string
  description?: string | null
  promotionType: string
  targetAudience: string
  eligibleSegments?: string[] | null
  rules: string
  rewardConfig: string
  couponId?: string | null
  bannerImageUrl?: string | null
  deeplinkUrl?: string | null
  validFrom: string
  validUntil?: string | null
  totalBudget?: number | null
}

/** PUT /admin/promotions/{id} body — same shape as create. */
export type UpdatePromotionPayload = CreatePromotionPayload

export interface CouponDto {
  id: string
  brandId: string
  code: string
  name: string
  description: string | null
  couponType: string
  discountValue: number
  maxDiscountAmount: number | null
  minOrderValue: number
  applicableServices: string[]
  applicableStores: string[]
  applicableFranchises: string[]
  customerEligibility: string
  isFirstOrderOnly: boolean
  isSingleUsePerCust: boolean
  maxTotalUses: number | null
  maxUsesPerCustomer: number
  currentUsageCount: number
  isStackable: boolean
  isPublic: boolean
  isAutoApply: boolean
  validFrom: string
  validUntil: string | null
  status: string
  createdAt: string
  updatedAt: string
}

/** POST /admin/coupons body. `code` is upper-cased server-side. */
export interface CreateCouponPayload {
  code: string
  name: string
  description: string | null
  couponType: string
  discountValue: number
  maxDiscountAmount: number | null
  minOrderValue: number
  applicableServices: string[] | null
  applicableStores: string[] | null
  applicableFranchises: string[] | null
  customerEligibility: string
  eligibleCustomerIds: string[] | null
  eligibleSegments: string[] | null
  isFirstOrderOnly: boolean
  isSingleUsePerCust: boolean
  maxTotalUses: number | null
  maxUsesPerCustomer: number
  isStackable: boolean
  isPublic: boolean
  isAutoApply: boolean
  validFrom: string
  validUntil: string | null
}

/** PUT /admin/coupons/{id} body. `code` is immutable; adds `status`. */
export interface UpdateCouponPayload {
  name: string
  description: string | null
  discountValue: number
  maxDiscountAmount: number | null
  minOrderValue: number
  applicableServices: string[] | null
  applicableStores: string[] | null
  applicableFranchises: string[] | null
  customerEligibility: string
  eligibleCustomerIds: string[] | null
  eligibleSegments: string[] | null
  isFirstOrderOnly: boolean
  isSingleUsePerCust: boolean
  maxTotalUses: number | null
  maxUsesPerCustomer: number
  isStackable: boolean
  isPublic: boolean
  isAutoApply: boolean
  validFrom: string
  validUntil: string | null
  status: string
}

// ── Packages ────────────────────────────────────────────────────────────────

export interface PackageDto {
  id: string
  brandId: string
  code: string
  name: string
  nameLocalized: string
  tier: string
  description: string | null
  price: number
  creditValue: number
  discountPercent: number
  creditMultiplier: number
  validityDays: number | null
  isUnlimitedValidity: boolean
  applicableServices: string[]
  excludedServices: string[]
  minimumOrderValue: number | null
  maxUsagePerOrder: number | null
  maxPurchasesPerCust: number | null
  iconUrl: string | null
  colorHex: string | null
  displayOrder: number
  isFeatured: boolean
  termsAndConditions: string | null
  status: string
  availableFrom: string | null
  availableTo: string | null
  createdAt: string
  updatedAt: string
}

/** POST /admin/packages body. Status defaults to "active" server-side. */
export interface CreatePackagePayload {
  code: string
  name: string
  nameLocalized: string
  tier: string
  description: string | null
  price: number
  creditValue: number
  discountPercent: number
  creditMultiplier: number
  validityDays: number | null
  isUnlimitedValidity: boolean
  applicableServices: string[] | null
  excludedServices: string[] | null
  minimumOrderValue: number | null
  maxUsagePerOrder: number | null
  maxPurchasesPerCust: number | null
  iconUrl: string | null
  colorHex: string | null
  displayOrder: number
  isFeatured: boolean
  termsAndConditions: string | null
  availableFrom: string | null
  availableTo: string | null
}

/** PUT /admin/packages/{id} body. `code`/`tier` immutable; adds `status`. */
export interface UpdatePackagePayload {
  name: string
  nameLocalized: string
  description: string | null
  price: number
  creditValue: number
  discountPercent: number
  creditMultiplier: number
  validityDays: number | null
  isUnlimitedValidity: boolean
  applicableServices: string[] | null
  excludedServices: string[] | null
  minimumOrderValue: number | null
  maxUsagePerOrder: number | null
  maxPurchasesPerCust: number | null
  iconUrl: string | null
  colorHex: string | null
  displayOrder: number
  isFeatured: boolean
  termsAndConditions: string | null
  availableFrom: string | null
  availableTo: string | null
  status: string
}

// ── Warehouse kanban board ─────────────────────────────────────────────────────

export interface WarehouseGarmentCard {
  id: string
  tagCode: string
  itemName: string
  fabricName: string
  customerName: string
  stage: string
  lastScannedAt: string | null
  isFlagged: boolean
}

export interface WarehouseStageColumn {
  stage: string
  label: string
  count: number
  cards: WarehouseGarmentCard[]
}

export interface WarehouseBoardSummary {
  warehouseId: string | null
  warehouseName: string
  warehouseCode: string
  inFlightCount: number
  capacityPct: number
  throughputTarget: number
  throughputToday: number
}

export interface WarehouseBoard {
  summary: WarehouseBoardSummary
  columns: WarehouseStageColumn[]
}

// ── Garment journey (by-tag lookup) ──────────────────────────────────────────────

export interface GarmentDto {
  id: string
  brandId: string
  storeId: string
  warehouseId: string | null
  orderId: string
  orderItemId: string
  customerId: string
  tagCode: string
  secondaryTagCode: string | null
  itemId: string | null
  itemVariantId: string | null
  fabricTypeId: string | null
  color: string | null
  size: string | null
  weightGrams: number | null
  hasOrnaments: boolean
  hasLining: boolean
  isDesignerWear: boolean
  currentStage: string
  currentBatchId: string | null
  lastScannedAt: string | null
  rewashCount: number
  status: string
  createdAt: string
  updatedAt: string
}

export interface ProcessLogDto {
  id: string
  processCode: string
  action: string
  fromStage: string | null
  toStage: string | null
  occurredAt: string
}

export interface GarmentJourneyDto {
  garment: GarmentDto
  inspections: unknown[]
  processLogs: ProcessLogDto[]
  qualityChecks: unknown[]
}

// ── Stock Reconciliation ──────────────────────────────────────────────────────────

export interface StockReconciliationDto {
  id: string
  brandId: string
  warehouseId: string | null
  storeId: string | null
  reconDate: string
  reconType: string
  startedAt: string
  startedBy: string
  completedAt: string | null
  expectedCount: number
  scannedCount: number
  matchedCount: number
  missingCount: number
  unexpectedCount: number
  status: string
  createdAt: string
}

// ── Process log create ────────────────────────────────────────────────────────────

export interface CreateProcessLogRequest {
  garmentId: string
  warehouseId: string
  batchId: string | null
  processId: string | null
  processCode: string
  action: string
  fromStage: string | null
  toStage: string | null
  performedByName: string | null
}

// ── Create Garment (manual register) ─────────────────────────────────────────────

export interface CreateGarmentRequest {
  orderItemId: string
  tagCode: string
  color: string | null
  size: string | null
  weightGrams: number | null
  hasOrnaments: boolean
  hasLining: boolean
  isDesignerWear: boolean
  warehouseId: string | null
}

// ── Access Control console ──────────────────────────────────────────────────────

export interface AccessPerson {
  id: string
  name: string
  email: string
  initials: string
  roleCode: string
  roleName: string
  scopeLabel: string
  tier: string // "enterprise" | "franchise"
  status: string // "active" | "invited" | ...
  /** Coarse account type (e.g. "ops_staff" | "warehouse_staff" | "store_admin"). */
  userType: string
  lastActiveAt: string | null
}

export interface AccessPeopleCounts {
  all: number
  hqEmployees: number
  franchiseOwners: number
  franchiseStaff: number
}

export interface SetPersonStatusResult {
  status: string
  mustChangePassword: boolean
}

// ── Settings ────────────────────────────────────────────────────────────────
export interface EmailSettingsView {
  enabled: boolean
  host: string
  port: number
  secure: boolean
  username: string
  passwordSet: boolean
  fromEmail: string
  fromName: string
}

export interface ProvisioningView {
  mode: string // "admin_activate" | "self_service"
}

export interface AppUrlsView {
  adminBaseUrl: string
}

export type MapProviderId = 'osm' | 'google' | 'mapbox'

export interface MapsSettingsView {
  provider: MapProviderId
  googleApiKey: string | null
  mapboxToken: string | null
}

export interface UpdateMapsPayload {
  provider: MapProviderId
  googleApiKey?: string // omit/blank to keep the stored key
  mapboxToken?: string
}

export interface PayoutSettingsView {
  baseFare: number
  perKm: number
  expressBonus: number
  codBonus: number
  roundToNearest: number
}

export type UpdatePayoutPayload = PayoutSettingsView

// ── Settings — Marketplace fare & dispatch ───────────────────────────────────

/** Vehicle tiers a fare can be quoted for. */
export type FareTier = 'two_wheeler' | 'three_wheeler' | 'four_wheeler' | 'cycle' | 'foot'

export interface FareTierRate {
  baseFare: number
  perKm: number
  pickupFlat: number
}

/** A surge window. `days` are 0=Sun..6=Sat; empty = applies every day. */
export interface SurgeWindow {
  days: number[]
  startHour: number
  endHour: number
  multiplier: number
}

export interface FareSettings {
  minFare: number
  roundToNearest: number
  quoteTtlSeconds: number
  tierRates: Record<string, FareTierRate>
  surge: SurgeWindow[]
}

export type DispatchMode = 'push' | 'offer_accept'

export interface DispatchSettings {
  mode: DispatchMode
  offerTtlSeconds: number
  maxOfferRounds: number
  offersPerRound: number
}

// ── Settings — Payment Gateway ───────────────────────────────────────────────

export interface PaymentGatewaySettingsView {
  provider: string               // 'razorpay'
  enabled: boolean
  keyId: string | null
  keySecretTail: string | null   // '••••XXXX' or null
  keySecretSet: boolean
  webhookSecretTail: string | null
  webhookSecretSet: boolean
  codEnabled: boolean
}

export interface UpdatePaymentGatewayPayload {
  enabled: boolean
  keyId?: string
  keySecret?: string             // omit/blank to keep stored secret
  webhookSecret?: string         // omit/blank to keep stored secret
  codEnabled: boolean
}

// The platform-scoped Razorpay account that collects SaaS tier invoices from tenant
// brands (Settings → Platform billing). The GET/PUT return a PaymentGatewaySettingsView
// (codEnabled is always false here — irrelevant to B2B platform billing). Platform-admin only.
export interface UpdatePlatformPaymentGatewayPayload {
  enabled: boolean
  keyId?: string
  keySecret?: string             // omit/blank to keep stored secret
  webhookSecret?: string         // omit/blank to keep stored secret
}

// ── Settings — WhatsApp ──────────────────────────────────────────────────────

export interface WhatsAppSettingsView {
  enabled: boolean
  phoneNumberId: string | null
  accessTokenTail: string | null
  accessTokenSet: boolean
  otpEnabled: boolean
  otpTemplateName: string | null
}

export interface UpdateWhatsAppPayload {
  enabled: boolean
  phoneNumberId?: string
  accessToken?: string           // omit/blank to keep stored token
  otpEnabled: boolean
  otpTemplateName?: string       // approved authentication-category template
}

// ── Settings — SMS ───────────────────────────────────────────────────────────

export interface SmsSettingsView {
  provider: string               // 'msg91'
  enabled: boolean
  authKeyTail: string | null
  authKeySet: boolean
  senderId: string | null
  dltTemplateId: string | null
}

export interface UpdateSmsPayload {
  enabled: boolean
  authKey?: string               // omit/blank to keep stored key
  senderId?: string
  dltTemplateId?: string
}

export interface AdminSettings {
  email: EmailSettingsView
  provisioning: ProvisioningView
  app: AppUrlsView
  maps: MapsSettingsView
  payout: PayoutSettingsView
  paymentGateway: PaymentGatewaySettingsView
  whatsApp: WhatsAppSettingsView
  sms: SmsSettingsView
}

export interface UpdateEmailPayload {
  enabled: boolean
  host: string
  port: number
  secure: boolean
  username: string
  password?: string // omit/blank to keep the stored password
  fromEmail: string
  fromName: string
}

export interface TestEmailResult {
  sent: boolean
  error: string | null
}

// ── Invite acceptance (public) ──────────────────────────────────────────────
export interface InvitePreview {
  valid: boolean
  email: string | null
  name: string | null
}

// ── Franchise onboarding ────────────────────────────────────────────────────
export interface OnboardingStep {
  key: string
  title: string
  description: string
  done: boolean
  summary: string | null
}

export interface OnboardingAddress {
  line1: string
  city: string
  state: string
  pincode: string
}

export interface OnboardingOwner {
  userId: string | null
  name: string | null
  email: string | null
  status: string | null
}

export interface OnboardingState {
  id: string
  code: string
  legalName: string
  displayName: string | null
  gstin: string | null
  pan: string | null
  contactPhone: string
  contactEmail: string | null
  billingAddress: OnboardingAddress | null
  operationalAddress: OnboardingAddress | null
  royaltyPercent: number
  marketingFeePercent: number
  initialFranchiseFee: number
  termYears: number
  agreementCreated: boolean
  agreementNumber: string | null
  owner: OnboardingOwner
  storeCount: number
  onboardingStatus: string
  isActive: boolean
  progressPct: number
  canActivate: boolean
  steps: OnboardingStep[]
}

export interface StartOnboardingPayload {
  legalName: string
  displayName?: string
  contactPhone: string
  contactEmail?: string
}

export interface SaveDetailsPayload {
  legalName: string
  displayName?: string
  gstin?: string
  pan?: string
  contactPhone: string
  contactEmail?: string
  billingAddress?: OnboardingAddress
  operationalAddress?: OnboardingAddress
}

export interface SaveCommercialsPayload {
  royaltyPercent: number
  marketingFeePercent: number
  initialFranchiseFee: number
  termYears: number
}

export interface InviteOwnerPayload {
  email: string
  firstName?: string
  lastName?: string
  phone?: string
}

export interface AddStorePayload {
  name: string
  code?: string
  addressLine1: string
  city: string
  state: string
  pincode: string
}

export interface AccessPeople {
  counts: AccessPeopleCounts
  people: AccessPerson[]
}

/** Paged people response: aggregate counts (full set) + the current page of people. */
export interface AccessPeoplePage {
  counts: AccessPeopleCounts
  people: PaginatedList<AccessPerson>
}

export interface MatrixModule {
  key: string
  label: string
}

export interface AccessRoleSummary {
  id: string
  code: string
  name: string
  description: string | null
  scopeType: string
  isSystem: boolean
  memberCount: number
  onCells: string[] // "module:action" cells that are enabled
  /** Vertical this role belongs to (laundry/salon/logistics), or null = neutral/all brands. */
  verticalKey?: string | null
}

export interface AccessRoleGroup {
  tier: string
  tierLabel: string
  roles: AccessRoleSummary[]
}

export interface AccessRoles {
  modules: MatrixModule[]
  actions: string[]
  groups: AccessRoleGroup[]
  /** cellKey ("module:action") → permission codes that cell grants (fan-out disclosure). */
  cells: Record<string, string[]>
}

export interface RoleCellChange {
  cellKey: string
  enabled: boolean
}

// Role CRUD payloads (UI-managed custom roles)
export interface CreateRolePayload {
  code: string
  name: string
  description?: string | null
  scopeType: string // 'brand' | 'franchise' | 'store' | 'warehouse'
}
export interface UpdateRolePayload {
  name: string
  description?: string | null
}
export interface CloneRolePayload {
  code: string
  name: string
  description?: string | null
}

export interface AccessFranchise {
  id: string
  name: string
  ownershipType: string // "franchise" | "company"
  location: string
  sinceYear: number
  ownerName: string | null
  ownerInitials: string | null
  storeCount: number
  staffCount: number
  riderCount: number
  revenueMonthly: number
  status: string // "Active" | "Onboarding"
}

export interface AccessFranchises {
  franchises: AccessFranchise[]
}

// ── Entitlements (PaaS per-brand module licensing) ──────────────────────────
export interface BrandModuleEntitlement {
  key: string
  label: string
  section: string | null
  isCore: boolean
  entitled: boolean
  source: string | null // 'bundle' | 'manual' | 'core' | null
  validUntil: string | null
}

export interface BrandEntitlements {
  brandId: string
  brandName: string
  modules: BrandModuleEntitlement[]
}

export interface ModuleBundleItem {
  key: string
  label: string
}

export interface ModuleBundle {
  code: string
  name: string
  description: string | null
  items: ModuleBundleItem[]
  verticalKey?: string | null
  /** Brand-tier pricing — what applying this bundle costs the tenant (null = unpriced/custom). */
  price?: number | null
  billingInterval?: string | null
  currencyCode?: string | null
  isPublic?: boolean
}

export interface BrandPlatformInvoice {
  id: string
  periodStart: string
  periodEnd: string
  amount: number
  currencyCode: string
  status: string
  issuedAt: string
  dueAt: string
  /** Razorpay payment-link short URL, once a link has been generated for this invoice. */
  paymentLinkUrl?: string | null
}

/** The brand's own platform subscription (the priced tier it pays for) + its invoices. */
export interface BrandPlatformSubscription {
  id: string
  brandId: string
  bundleCode: string
  planName: string
  price: number
  billingInterval: string
  currencyCode: string
  status: string
  currentPeriodStart: string
  currentPeriodEnd: string
  nextBillingAt: string
  autoRenew: boolean
  invoices: BrandPlatformInvoice[]
}

export interface TierMrr {
  bundleCode: string
  planName: string
  activeCount: number
  monthlyMrr: number
}

export interface InvoiceStatusTotal {
  status: string
  count: number
  totalAmount: number
}

/** Platform-wide SaaS revenue summary (operator MRR view). */
export interface PlatformBillingSummary {
  currency: string
  monthlyMrr: number
  annualRunRate: number
  activeTenants: number
  cancelledTenants: number
  outstandingAmount: number
  collectedAmount: number
  byTier: TierMrr[]
  invoicesByStatus: InvoiceStatusTotal[]
}

export interface InviteUserPayload {
  email: string
  phone?: string
  firstName?: string
  lastName?: string
  userType: string
  roleId: string
  scopeType: string
  scopeId?: string | null
  password?: string
}

// ── Riders (Identity invite + Logistics profile) ────────────────────────────────

/** Minimal user shape returned by the Identity invite/access-control endpoint. */
export interface UserDto {
  id: string
  email: string | null
  phone: string | null
  firstName: string | null
  lastName: string | null
  userType: string
  status: string
}

/** Employment type for people (HQ + franchise employees). */
export type UserEmploymentType = 'full_time' | 'part_time' | 'contractual' | 'consultant' | 'intern'

/** KYC verification state for a person's identity documents. */
export type UserKycStatus = 'pending' | 'verified' | 'rejected'

/** Full admin view of a user (GET /admin/users/{id}). */
export interface AdminUserDetail {
  id: string
  email: string | null
  phoneE164: string | null
  userType: string
  status: string
  mfaEnabled: boolean
  lastLoginAt: string | null
  createdAt: string
  firstName: string | null
  lastName: string | null
  displayName: string | null
  designation: string | null
  employmentType: UserEmploymentType | null
  panNumber: string | null
  aadhaarNumberMasked: string | null
  kycStatus: UserKycStatus | null
  kycVerifiedAt: string | null
  bankAccountName: string | null
  bankAccountNumber: string | null
  bankIfsc: string | null
  upiId: string | null
}

export interface UpdateUserPayload {
  email?: string | null
  phone?: string | null
  firstName?: string | null
  lastName?: string | null
  designation?: string | null
  // Employment + KYC + bank. Empty string clears; omit/undefined leaves unchanged.
  employmentType?: string | null
  panNumber?: string | null
  aadhaarNumberMasked?: string | null
  kycStatus?: string | null
  bankAccountName?: string | null
  bankAccountNumber?: string | null
  bankIfsc?: string | null
  upiId?: string | null
}

/** Replace a user's primary role (POST /admin/users/{id}/change-role). */
export interface ChangeRolePayload {
  roleId: string
  scopeType: string // "franchise" | "brand"
  scopeId: string | null
}

export interface MembershipDto {
  id: string
  userId: string
  scopeType: string
  scopeId: string | null
  roleId: string
  roleCode: string
  isPrimary: boolean
  grantedAt: string
}

export type RiderEmploymentType = 'employee' | 'contractor' | 'gig' | 'outsourced'
export type RiderVehicleType = 'two_wheeler' | 'three_wheeler' | 'four_wheeler' | 'cycle' | 'foot'

export interface RiderDto {
  id: string
  userId: string
  brandId: string
  franchiseId: string
  primaryStoreId: string | null
  riderCode: string
  employmentType: string
  vehicleType: string
  vehicleNumber: string | null
  vehicleModel: string | null
  drivingLicenseNumber: string | null
  dlExpiryDate: string | null
  insuranceExpiryDate: string | null
  dailyPickupCapacity: number
  dailyDeliveryCapacity: number
  serviceRadiusKm: number
  ratingAverage: number | null
  ratingCount: number
  completionRate: number | null
  lifetimeDeliveries: number
  isOnline: boolean
  isOnDuty: boolean
  currentLoad: number
  kycStatus: string
  vehicleVerificationStatus: RiderVehicleVerificationStatus
  status: string
  createdAt: string
  updatedAt: string
  riderName: string | null
  email: string | null
  phone: string | null
  userStatus: string | null
  franchiseName: string | null
  primaryStoreName: string | null
}

export type RiderKycStatus = 'pending' | 'submitted' | 'verified' | 'rejected' | 'expired'
export type RiderStatus = 'active' | 'suspended' | 'terminated'

/** Vehicle verification lifecycle (separate from KYC) returned on RiderDto + the verification view. */
export type RiderVehicleVerificationStatus = 'pending' | 'under_review' | 'approved' | 'rejected'

/** A single uploaded KYC document under review. */
export type RiderDocType = 'license' | 'rc' | 'insurance' | 'id' | 'photo'
export type RiderDocStatus = 'pending' | 'approved' | 'rejected'

export interface RiderDocumentDto {
  id: string
  docType: RiderDocType
  fileName: string
  status: RiderDocStatus
  rejectionReason: string | null
  reviewedAt: string | null
  uploadedAt: string
}

/** GET /admin/riders/{id}/verification — the document + vehicle review packet. */
export interface RiderVerificationDto {
  kycStatus: string
  vehicleVerificationStatus: RiderVehicleVerificationStatus
  vehicleRejectionReason: string | null
  documents: RiderDocumentDto[]
}

/** Sort keys accepted by GET /riders `sort` param. Prefix with `-` for descending. */
export type RiderSortKey = 'created' | 'name' | 'franchise' | 'kyc' | 'status'
export type RiderSort = RiderSortKey | `-${RiderSortKey}`

export interface RiderListParams extends PaginationParams {
  search?: string
  kycStatus?: string
  status?: string
  franchiseId?: string
  sort?: string
}

/**
 * Step 1 — create the rider login (Identity).
 *
 * Posts to the dedicated narrow endpoint `/access-control/riders/invite`. The
 * server forces the franchise for franchise-scoped callers and validates it for
 * admins, so the client no longer supplies a roleId/scope — just the identity
 * fields plus the target franchise.
 */
export interface InviteRiderUserPayload {
  email: string
  phone?: string
  firstName?: string
  lastName?: string
  franchiseId: string
}

/**
 * Edit subset accepted by PUT /riders/{id} — every field optional. Only the keys
 * actually sent are applied server-side, so omitting a field leaves it unchanged
 * (this is how the sensitive KYC/payout fields — never returned in RiderDto — are
 * "leave blank to keep" rather than cleared). KYC *status* is NOT editable here.
 */
export interface UpdateRiderPayload {
  status?: string
  employmentType?: RiderEmploymentType
  vehicleType?: RiderVehicleType
  vehicleNumber?: string
  vehicleModel?: string
  drivingLicenseNumber?: string
  dlExpiryDate?: string | null
  aadhaarNumberMasked?: string
  panNumber?: string
  insuranceExpiryDate?: string | null
  bankAccountNumber?: string
  bankIfsc?: string
  bankAccountName?: string
  upiId?: string
  dailyPickupCapacity?: number
  dailyDeliveryCapacity?: number
  serviceRadiusKm?: number
  primaryStoreId?: string | null
}

// ── Rider Ops (live board) ────────────────────────────────────────────────────

/** Derived operational state of a rider on the live board. */
export type RiderOpsStatus = 'offline' | 'idle' | 'on_the_way' | 'arrived' | 'to_store'

/** A rider's current snapshot for the live map + roster (GET /admin/riders/live). */
export interface RiderLiveDto {
  id: string
  riderCode: string
  riderName: string | null
  phone: string | null
  status: string
  isOnDuty: boolean
  currentLoad: number
  lat: number | null
  lng: number | null
  lastPingAt: string | null
  isStale: boolean
  opsStatus: RiderOpsStatus
  activeLegType: string | null
  activeOrderId: string | null
  activeOrderNumber: string | null
  pickupsToday: number
  deliveriesToday: number
}

/** One GPS breadcrumb (GET /admin/riders/{id}/track). */
export interface RiderTrackPointDto {
  lat: number
  lng: number
  pingedAt: string
  speedKmph: number | null
  isMoving: boolean | null
}

/** Per-rider throughput over a date range (GET /admin/riders/{id}/stats). */
export interface RiderStatsDto {
  riderId: string
  riderCode: string
  riderName: string | null
  from: string
  to: string
  pickupsDone: number
  deliveriesDone: number
  assignmentsTotal: number
  assignmentsFailed: number
  totalKm: number
  codCollected: number
  earnings: number
}

// ── Rider COD cash + settlement (Phase 3) ─────────────────────────────────────

/** Per-rider uncleared COD cash (GET /admin/riders/cod/outstanding). */
export interface RiderCodSummary {
  riderId: string
  riderCode: string
  riderName: string | null
  franchiseName: string | null
  outstandingAmount: number
  outstandingCount: number
  oldestCollectedAt: string | null
}

/** A single outstanding COD collection. */
export interface CodCollection {
  assignmentId: string
  orderId: string | null
  orderNumber: string | null
  amount: number
  collectedAt: string
}

/** A rider's outstanding cash + the collections (GET /admin/riders/{id}/cod). */
export interface RiderCodDetail {
  riderId: string
  riderCode: string
  riderName: string | null
  outstandingAmount: number
  outstandingCount: number
  collections: CodCollection[]
}

/** A recorded settlement / cash handover. */
export interface RiderSettlement {
  id: string
  riderId: string
  storeId: string | null
  storeName: string | null
  totalAmount: number
  collectionCount: number
  reference: string | null
  status: string
  settledAt: string
  settledBy: string | null
  notes: string | null
}

/** Record a settlement that clears all of a rider's outstanding COD cash. */
export interface SettleRiderPayload {
  storeId?: string
  reference?: string
  notes?: string
}

/** Step 2 — create the rider operational profile (Logistics). */
export interface CreateRiderProfilePayload {
  userId: string
  franchiseId: string
  primaryStoreId?: string
  employmentType: RiderEmploymentType
  vehicleType: RiderVehicleType
  vehicleNumber?: string
  vehicleModel?: string
  drivingLicenseNumber?: string
  dlExpiryDate?: string
  aadhaarNumberMasked?: string
  panNumber?: string
  insuranceExpiryDate?: string
  bankAccountNumber?: string
  bankIfsc?: string
  bankAccountName?: string
  upiId?: string
  dailyPickupCapacity: number
  dailyDeliveryCapacity: number
  serviceRadiusKm: number
}

// ── Navigator (data-driven sidebar menu) ────────────────────────────────────────

export interface NavItem {
  key: string
  label: string
  icon: string | null
  route: string | null
}

export interface NavSection {
  section: string
  items: NavItem[]
}

export interface Navigator {
  sections: NavSection[]
}

// ── Pickup requests (Orders: laundryghar.Orders/Application/Pickup/Dtos) ─────────

/**
 * A single estimated cart line submitted by the customer at booking time.
 * Mirrors RequestedCartItemDto. These are ESTIMATES — the real order is created
 * after weighing at the store, so serviceId/itemId/estimatedUnitPrice may be null.
 */
export interface RequestedCartItemDto {
  serviceId: string | null
  itemId: string | null
  displayLabel: string
  quantity: number
  estimatedUnitPrice: number | null
}

/** Full pickup request — mirrors PickupRequestDto (admin + customer paths share it). */
export interface PickupRequestDto {
  id: string
  requestNumber: string
  brandId: string
  storeId: string | null
  customerId: string
  addressId: string
  pickupSlotId: string | null
  pickupDate: string // DateOnly → "YYYY-MM-DD"
  pickupWindowStart: string // TimeOnly → "HH:mm:ss"
  pickupWindowEnd: string
  isExpress: boolean
  estimatedItems: number | null
  estimatedAmount: number | null
  status: string
  createdAt: string
  cartItems: RequestedCartItemDto[]
  /** Customer payment intent recorded at booking time: "wallet" | "cod" | "upi-deferred". */
  paymentPreference: string
}

export interface PickupRequestListParams extends PaginationParams {
  status?: string
}

/** Body for POST /pickup-requests/{id}/assign — mirrors AssignPickupRequest. */
export interface AssignPickupPayload {
  riderId: string
}

/** Body for POST /pickup-requests/{id}/reject — mirrors RejectPickupRequest. */
export interface RejectPickupPayload {
  /** Mandatory rejection reason, max 300 characters. */
  reason: string
}

/** Result of assigning a pickup to a rider — mirrors DeliveryAssignmentDto. */
export interface DeliveryAssignmentDto {
  id: string
  brandId: string
  storeId: string
  riderId: string
  orderId: string | null
  pickupRequestId: string | null
  legType: string
  assignedAt: string
  status: string
}

// ── Delivery slots (Orders: laundryghar.Orders/Application/Delivery/Dtos) ────────

export type DeliverySlotType = 'pickup' | 'delivery'

/** A managed time slot — mirrors DeliverySlotDto. */
export interface DeliverySlotDto {
  id: string
  brandId: string
  storeId: string
  slotDate: string // DateOnly → "YYYY-MM-DD"
  slotStart: string // TimeOnly → "HH:mm:ss"
  slotEnd: string
  slotType: DeliverySlotType | string
  capacity: number
  bookedCount: number
  available: number
  isExpress: boolean
  isActive: boolean
  status: string
}

export interface DeliverySlotListParams extends PaginationParams {
  storeId?: string
  date?: string
  slotType?: string
}

/** Body for POST /delivery-slots — mirrors CreateDeliverySlotRequest. */
export interface CreateDeliverySlotPayload {
  storeId: string
  slotDate: string // "YYYY-MM-DD"
  slotStart: string // "HH:mm:ss"
  slotEnd: string // "HH:mm:ss"
  slotType: DeliverySlotType
  capacity: number
  isExpress: boolean
}

/** Body for PUT /delivery-slots/{id} — mirrors UpdateDeliverySlotRequest (all optional). */
export interface UpdateDeliverySlotPayload {
  capacity?: number
  isActive?: boolean
  status?: string
}

// ── Finance: Royalty invoices ────────────────────────────────────────────────

export interface RoyaltyCalculationDto {
  id: string
  royaltyInvoiceId: string
  storeId: string | null
  orderId: string | null
  calculationDate: string   // DateOnly — "YYYY-MM-DD"
  revenueType: string
  grossAmount: number
  excludedAmount: number
  eligibleAmount: number
  royaltyRate: number
  royaltyAmount: number
  notes: string | null
}

export interface RoyaltyInvoiceDto {
  id: string
  brandId: string
  franchiseId: string
  franchiseAgreementId: string | null
  invoiceNumber: string
  periodStart: string        // DateOnly — "YYYY-MM-DD"
  periodEnd: string          // DateOnly — "YYYY-MM-DD"
  grossRevenue: number
  eligibleRevenue: number
  royaltyPercent: number
  royaltyAmount: number
  marketingFeePercent: number
  marketingFeeAmount: number
  technologyFeeAmount: number
  otherCharges: number
  adjustments: number
  subtotal: number
  taxTotal: number
  grandTotal: number
  amountPaid: number
  amountDue: number | null
  currencyCode: string
  totalOrders: number
  invoiceDate: string        // DateOnly — "YYYY-MM-DD"
  dueDate: string            // DateOnly — "YYYY-MM-DD"
  status: string             // draft | issued | sent | viewed | partial | paid | overdue | void
  notes: string | null
  createdAt: string
  calculations: RoyaltyCalculationDto[]
}

export interface RoyaltyListParams extends PaginationParams {
  franchiseId?: string
  status?: string
}

export interface GenerateRoyaltyInvoicePayload {
  franchiseId: string
  franchiseAgreementId?: string | null
  periodStart: string        // DateOnly — "YYYY-MM-DD"
  periodEnd: string          // DateOnly — "YYYY-MM-DD"
  grossRevenueOverride?: number | null
  royaltyPercent: number
  marketingFeePercent: number
  technologyFeeAmount: number
  otherCharges: number
  adjustments: number
  gstRate: number
  notes?: string | null
  currencyCode: string
}

export interface IssueRoyaltyInvoicePayload {
  notes?: string | null
}

export interface RecordRoyaltyPaymentPayload {
  amountPaid: number
  notes?: string | null
}

// ── Subscriptions (Commerce: plans + customer subscriptions) ───────────────────

export interface SubscriptionPlanDto {
  id: string
  brandId: string
  code: string
  name: string
  /** JSON-object string, e.g. '{"en":"Basic","hi":"बेसिक"}'. */
  nameLocalized: string
  description: string | null
  tier: string
  billingInterval: string
  intervalCount: number
  price: number
  setupFee: number
  currencyCode: string
  trialDays: number
  quotaType: string
  quotaValue: number | null
  rolloverUnused: boolean
  maxRollover: number | null
  overageDiscountPercent: number
  applicableServices: string[]
  excludedServices: string[]
  pickupIncluded: boolean
  deliveryIncluded: boolean
  expressIncluded: boolean
  maxActiveSubscribers: number | null
  currentSubscriberCount: number
  gateway: string | null
  gatewayPlanId: string | null
  termsAndConditions: string | null
  iconUrl: string | null
  colorHex: string | null
  displayOrder: number
  isPublic: boolean
  isFeatured: boolean
  status: string
  availableFrom: string | null
  availableTo: string | null
  createdAt: string
  updatedAt: string
}

export interface CreateSubscriptionPlanPayload {
  code: string
  name: string
  nameLocalized: string
  description?: string | null
  tier: string
  billingInterval: string
  intervalCount: number
  price: number
  setupFee: number
  currencyCode: string
  trialDays: number
  quotaType: string
  quotaValue?: number | null
  rolloverUnused: boolean
  maxRollover?: number | null
  overageDiscountPercent: number
  applicableServices?: string[] | null
  excludedServices?: string[] | null
  pickupIncluded: boolean
  deliveryIncluded: boolean
  expressIncluded: boolean
  maxActiveSubscribers?: number | null
  gateway?: string | null
  gatewayPlanId?: string | null
  termsAndConditions?: string | null
  iconUrl?: string | null
  colorHex?: string | null
  displayOrder: number
  isPublic: boolean
  isFeatured: boolean
  availableFrom?: string | null
  availableTo?: string | null
}

export interface UpdateSubscriptionPlanPayload {
  name: string
  nameLocalized: string
  description?: string | null
  tier: string
  price: number
  setupFee: number
  quotaType: string
  quotaValue?: number | null
  rolloverUnused: boolean
  maxRollover?: number | null
  overageDiscountPercent: number
  applicableServices?: string[] | null
  excludedServices?: string[] | null
  pickupIncluded: boolean
  deliveryIncluded: boolean
  expressIncluded: boolean
  maxActiveSubscribers?: number | null
  gateway?: string | null
  gatewayPlanId?: string | null
  termsAndConditions?: string | null
  iconUrl?: string | null
  colorHex?: string | null
  displayOrder: number
  isPublic: boolean
  isFeatured: boolean
  status: string
  availableFrom?: string | null
  availableTo?: string | null
}

export interface CustomerSubscriptionDto {
  id: string
  brandId: string
  customerId: string
  planId: string
  subscriptionNumber: string
  priceSnapshot: number
  billingInterval: string
  intervalCount: number
  quotaType: string
  quotaValue: number | null
  currencyCode: string
  status: string
  autoRenew: boolean
  currentPeriodStart: string | null
  currentPeriodEnd: string | null
  nextBillingAt: string | null
  creditsRemaining: number
  cancelAtPeriodEnd: boolean
  cancelledAt: string | null
  dunningAttempts: number
  totalCyclesBilled: number
  createdAt: string
  updatedAt: string
}

export interface CustomerSubscriptionListParams extends PaginationParams {
  customerId?: string
  status?: string
}

// ── Platform plans + franchise subscriptions (Finance SaaS) ────────────────────

export interface PlatformPlanDto {
  id: string
  brandId: string | null
  code: string
  name: string
  description: string | null
  tier: string
  billingInterval: string
  intervalCount: number
  price: number
  setupFee: number
  annualDiscountPercent: number
  currencyCode: string
  trialDays: number
  maxStores: number | null
  maxWarehouses: number | null
  maxUsers: number | null
  maxOrdersPerMonth: number | null
  maxRiders: number | null
  overagePerOrder: number
  overagePerStore: number
  overagePerUser: number
  /** JSON-object string of feature flags/limits. */
  features: string
  supportLevel: string
  isPublic: boolean
  isFeatured: boolean
  displayOrder: number
  status: string
  createdAt: string
  updatedAt: string
}

export interface CreatePlatformPlanPayload {
  brandId?: string | null
  code: string
  name: string
  description?: string | null
  tier: string
  billingInterval: string
  intervalCount: number
  price: number
  setupFee: number
  annualDiscountPercent: number
  currencyCode: string
  trialDays: number
  maxStores?: number | null
  maxWarehouses?: number | null
  maxUsers?: number | null
  maxOrdersPerMonth?: number | null
  maxRiders?: number | null
  overagePerOrder: number
  overagePerStore: number
  overagePerUser: number
  features: string
  supportLevel: string
  isPublic: boolean
  isFeatured: boolean
  displayOrder: number
}

export interface UpdatePlatformPlanPayload {
  name: string
  description?: string | null
  tier: string
  price: number
  setupFee: number
  annualDiscountPercent: number
  maxStores?: number | null
  maxWarehouses?: number | null
  maxUsers?: number | null
  maxOrdersPerMonth?: number | null
  maxRiders?: number | null
  overagePerOrder: number
  overagePerStore: number
  overagePerUser: number
  features: string
  supportLevel: string
  isPublic: boolean
  isFeatured: boolean
  displayOrder: number
  status: string
}

export interface PlatformPlanListParams extends PaginationParams {
  status?: string
}

export interface FranchiseSubscriptionDto {
  id: string
  brandId: string
  franchiseId: string
  platformPlanId: string
  subscriptionNumber: string
  priceSnapshot: number
  billingInterval: string
  status: string
  autoRenew: boolean
  currentPeriodStart: string | null
  currentPeriodEnd: string | null
  nextBillingAt: string | null
  dunningAttempts: number
  suspendedAt: string | null
  totalCyclesBilled: number
  createdAt: string
  updatedAt: string
}

export interface FranchiseSubscriptionListParams extends PaginationParams {
  franchiseId?: string
  status?: string
}

export interface AssignFranchisePlanPayload {
  franchiseId: string
  platformPlanId: string
  paymentMethod: string
  autoRenew: boolean
}

// ── Rider payout requests (Logistics) ──────────────────────────────────────────

export type PayoutRequestStatus = 'requested' | 'approved' | 'rejected' | 'paid'

export interface RiderPayoutRequestDto {
  id: string
  riderId: string
  riderName: string
  amount: number
  status: PayoutRequestStatus
  rejectionReason: string | null
  paymentReference: string | null
  requestedAt: string
  reviewedAt: string | null
  paidAt: string | null
}

export interface RejectPayoutPayload {
  reason: string
}

export interface MarkPayoutPaidPayload {
  reference: string
}

// ── Rider incentive rules (Logistics) ──────────────────────────────────────────

export type IncentiveRuleType = 'trips_target' | 'surge_bonus'
export type IncentiveWindow = 'daily'

export interface IncentiveRuleDto {
  id: string
  name: string
  ruleType: IncentiveRuleType
  threshold: number
  rewardAmount: number
  window: IncentiveWindow
  isActive: boolean
  validFrom: string
  validUntil: string | null
}

export interface CreateIncentiveRulePayload {
  name: string
  ruleType: IncentiveRuleType
  threshold: number
  rewardAmount: number
  isActive?: boolean
  validUntil?: string | null
}

export type UpdateIncentiveRulePayload = CreateIncentiveRulePayload

// ── Support inbox ─────────────────────────────────────────────────────────────
// Mirrors laundryghar.Operations Orders/Application/Support SupportCommands DTOs,
// served by the logistics client under /api/v1/admin/support/tickets.

export type SupportRequesterType = 'customer' | 'rider'
export type SupportTicketPriority = 'low' | 'normal' | 'high'
export type SupportTicketStatus = 'open' | 'in_progress' | 'resolved' | 'closed'
export type TicketSenderType = 'customer' | 'rider' | 'agent' | 'system'

export interface SupportTicketDto {
  id: string
  ticketNumber: string
  requesterType: SupportRequesterType
  requesterName: string | null
  subject: string
  category: string
  priority: SupportTicketPriority
  status: SupportTicketStatus
  orderId: string | null
  lastMessageAt: string
  createdAt: string
}

export interface TicketMessageDto {
  id: string
  senderType: TicketSenderType
  senderId: string | null
  body: string
  createdAt: string
}

export interface SupportTicketDetailDto {
  ticket: SupportTicketDto
  messages: TicketMessageDto[]
}

/** PATCH body — every field optional; send only what changes. */
export interface UpdateTicketPayload {
  status?: SupportTicketStatus
  priority?: SupportTicketPriority
  assignedTo?: string | null
}

// ---------------------------------------------------------------------------
// Fulfilment config — backend-driven status labels (multi-vertical Phase 3)
// GET /api/v1/fulfillment-config — one entry per fulfilment mode, built live from the
// backend strategies, so the admin labels statuses per the order's vertical rather than
// a hardcoded laundry ladder.
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
