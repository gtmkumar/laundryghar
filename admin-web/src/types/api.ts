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

export interface AdminSettings {
  email: EmailSettingsView
  provisioning: ProvisioningView
  app: AppUrlsView
  maps: MapsSettingsView
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
