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
