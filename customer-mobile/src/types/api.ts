// ---------------------------------------------------------------------------
// API response envelope — mirrors laundryghar.Utilities.ApiResponse shapes
// ---------------------------------------------------------------------------

export interface ApiMessage {
  errorTypeCode?: number;
  errorMessage?: Record<string, string[]>;
  responseMessage?: string;
}

/** Base envelope: { status, message? } */
export interface BaseResponse {
  status: boolean;
  message?: ApiMessage;
}

/** Single object: { status, data?, message? } */
export interface SingleResponse<T> extends BaseResponse {
  data?: T;
}

/** Flat list (non-paginated): { status, data?, message? } where data is an array */
export interface ListResponse<T> extends BaseResponse {
  data?: T[];
}

/** Paginated list: { status, data: { list, hasPreviousPage, hasNextPage }, message? } */
export interface PaginatedData<T> {
  list: T[];
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface PaginatedListResponse<T> extends BaseResponse {
  data?: PaginatedData<T>;
}

// ---------------------------------------------------------------------------
// Auth DTOs — mirrors CustomerAuthEndpoints shapes
// ---------------------------------------------------------------------------

export interface OtpSendRequest {
  phone: string;
  brandCode?: string;
}

export interface OtpSendResponse {
  /** Backend confirms OTP sent — may include masked destination */
  message?: string;
}

export interface OtpVerifyRequest {
  phone: string;
  code: string;
  brandCode?: string;
}

export interface CustomerTokenResponse {
  accessToken: string;
  refreshToken: string;
  isNewCustomer: boolean;
}

export interface RefreshTokenRequest {
  refreshToken: string;
}

export interface LogoutRequest {
  refreshToken: string;
}

export interface CustomerMeResponse {
  /** Backend field name — maps to CustomerId in CustomerMeResponse.cs */
  customerId: string;
  brandId: string;
  /** Backend field name — maps to Phone in CustomerMeResponse.cs */
  phone: string;
  firstName?: string;
  lastName?: string;
  displayName?: string;
  status: string;
}

// ---------------------------------------------------------------------------
// Catalog DTOs — mirrors CustomerEndpoints / pricing queries
// ---------------------------------------------------------------------------

export interface ServiceCategoryDto {
  id: string;
  name: string;
  nameLocalized?: string;
  iconUrl?: string;
  imageUrl?: string;
  /** Backend field name — maps to displayOrder in ServiceCategoryDtos.cs */
  displayOrder: number;
  /** Backend field name — category is active when status === 'active' */
  status: string;
}

export interface ServiceDto {
  id: string;
  categoryId: string;
  name: string;
  nameLocalized?: string;
  description?: string;
  iconUrl?: string;
  /** Backend field name — maps to displayOrder in ServiceDtos.cs */
  displayOrder: number;
  /** Backend field name — service is active when status === 'active' */
  status: string;
}

export interface PriceListItemDto {
  id: string;
  priceListId: string;
  brandId: string;
  serviceId: string;
  itemId: string;
  itemVariantId?: string;
  fabricTypeId?: string;
  itemGroupId?: string;
  /** Backend field name — maps to basePrice in PricingDtos.cs */
  basePrice: number;
  expressPrice?: number;
  minimumQuantity: number;
  taxRatePercent: number;
  isTaxable: boolean;
  /** Optional display label; used as the item name when present */
  displayLabel?: string;
  notes?: string;
  isActive: boolean;
  status: string;
}

export interface CustomerProfileDto {
  id: string;
  brandId: string;
  phoneE164: string;
  firstName?: string;
  lastName?: string;
  displayName?: string;
  email?: string;
  avatarUrl?: string;
  locale: string;
  status: string;
}

export interface CustomerAddressDto {
  id: string;
  customerId: string;
  /** Label preset: 'home' | 'work' | 'other' | custom */
  label: string;
  customLabel?: string;
  recipientName?: string;
  recipientPhone?: string;
  /** Backend field: addressLine1 */
  addressLine1: string;
  addressLine2?: string;
  landmark?: string;
  floor?: string;
  flatNumber?: string;
  buildingName?: string;
  society?: string;
  area?: string;
  city: string;
  state: string;
  pincode: string;
  countryCode: string;
  deliveryInstructions?: string;
  isDefault: boolean;
  isVerified: boolean;
  status: string;
  createdAt: string;
}

export interface CreateAddressRequest {
  label: string;
  customLabel?: string;
  recipientName?: string;
  recipientPhone?: string;
  addressLine1: string;
  addressLine2?: string;
  landmark?: string;
  floor?: string;
  flatNumber?: string;
  buildingName?: string;
  society?: string;
  area?: string;
  city: string;
  state: string;
  pincode: string;
  countryCode: string;
  deliveryInstructions?: string;
  isDefault: boolean;
}

export type UpdateAddressRequest = CreateAddressRequest;

export interface ServiceabilityDto {
  serviceable: boolean;
}

// ---------------------------------------------------------------------------
// Orders DTOs — mirrors CustomerOrderEndpoints shapes
// ---------------------------------------------------------------------------

export type OrderStatus =
  | 'placed'
  | 'pickup_scheduled'
  | 'pickup_assigned'
  | 'picked_up'
  | 'received'
  | 'sorting'
  | 'in_process'
  | 'qc'
  | 'ready'
  | 'delivery_scheduled'
  | 'out_for_delivery'
  | 'delivered'
  | 'closed'
  | 'cancelled'
  | 'returned'
  | 'rewash'
  | 'disputed';

export interface OrderItemDto {
  id: string;
  itemId: string;
  itemName: string;
  serviceId: string;
  serviceName: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface OrderDto {
  id: string;
  orderNumber: string;
  status: OrderStatus;
  channel: string;
  isExpress: boolean;
  subtotal: number;
  discountTotal: number;
  taxTotal: number;
  grandTotal: number;
  amountPaid: number;
  amountDue: number;
  currencyCode: string;
  placedAt: string;
  pickedUpAt?: string;
  readyAt?: string;
  deliveredAt?: string;
  cancelledAt?: string;
  items?: OrderItemDto[];
  storeId: string;
  storeName?: string;
  notes?: string;
  /** Customer rating 1–5. Null until rated. */
  rating?: number | null;
  ratingComment?: string | null;
  ratedAt?: string | null;
  /**
   * Delivery OTP — present when status is out_for_delivery.
   * Backend exposes this field to the customer for doorstep handoff confirmation.
   * The rider must verify this OTP to complete delivery (MOB-7).
   */
  deliveryOtp?: string | null;
}

export interface RateOrderRequest {
  score: number;
  comment?: string | null;
}

export interface OrderStatusHistoryDto {
  id: string;
  /** Previous status before this transition (null on first event). Backend field: fromStatus. */
  fromStatus?: OrderStatus | null;
  /** Status this event transitioned to. Backend field: toStatus. */
  toStatus: OrderStatus;
  changedAt: string;
  changedByType: string;
  reason?: string;
  customerNotified: boolean;
}

export interface DeliverySlotDto {
  id: string;
  storeId: string;
  slotDate: string;
  slotStart: string;
  slotEnd: string;
  slotType: 'pickup' | 'delivery';
  capacity: number;
  bookedCount: number;
  isExpress: boolean;
  isActive: boolean;
  available: boolean;
}

// ---------------------------------------------------------------------------
// Pickup-request DTOs — mirrors PickupDtos.cs in Orders service
// ---------------------------------------------------------------------------

/** Estimated cart line submitted by the customer at booking time. */
export interface RequestedCartItemDto {
  /** Catalog service id — null when not selected. */
  serviceId?: string | null;
  /** Catalog item id — null when not selected. */
  itemId?: string | null;
  /** Human-readable label, e.g. "Shirt – Wash & Iron". */
  displayLabel: string;
  /** Quantity >= 1. */
  quantity: number;
  /** Estimated unit price from price list; null when unavailable. */
  estimatedUnitPrice?: number | null;
}

export type PickupRequestStatus =
  | 'pending'
  | 'assigned'
  | 'rider_dispatched'
  | 'arrived'
  | 'completed'
  | 'converted'
  | 'cancelled'
  | 'no_response'
  | 'rescheduled';

export interface PickupRequestDto {
  id: string;
  requestNumber: string;
  brandId: string;
  storeId?: string | null;
  customerId: string;
  addressId: string;
  pickupSlotId?: string | null;
  pickupDate: string;
  pickupWindowStart: string;
  pickupWindowEnd: string;
  isExpress: boolean;
  estimatedItems?: number | null;
  estimatedAmount?: number | null;
  status: PickupRequestStatus;
  createdAt: string;
  /** Estimated cart lines — empty array when none were supplied. */
  cartItems: RequestedCartItemDto[];
  /** Payment intent recorded at booking: "wallet" | "cod" | "upi-deferred". */
  paymentPreference: string;
}

export interface CreatePickupRequestRequest {
  addressId: string;
  slotId?: string | null;
  pickupDate: string;
  pickupWindowStart: string;
  pickupWindowEnd: string;
  isExpress: boolean;
  estimatedItems?: number | null;
  estimatedAmount?: number | null;
  servicesRequested: string[];
  customerNotes?: string | null;
  /** Estimated cart lines from the booking flow. */
  cartItems?: RequestedCartItemDto[] | null;
  /**
   * Payment intent: "wallet" | "cod" | "upi-deferred".
   * UPI/card are normalised to "upi-deferred" server-side.
   */
  paymentPreference?: string | null;
}

// ---------------------------------------------------------------------------
// Commerce DTOs — mirrors CustomerCommerceEndpoints shapes
// ---------------------------------------------------------------------------

export interface PackageDto {
  id: string;
  brandId: string;
  name: string;
  description?: string;
  tier: 'diamond' | 'gold' | 'silver' | string;
  credits: number;
  price: number;
  validityDays: number;
  isActive: boolean;
}

export interface CustomerPackageDto {
  id: string;
  packageId: string;
  packageName: string;
  tier: string;
  creditsTotal: number;
  creditsUsed: number;
  creditsRemaining: number;
  purchasedAt: string;
  expiresAt?: string;
  status: string;
}

export interface PackageUsageLedgerDto {
  id: string;
  orderId?: string;
  orderNumber?: string;
  creditsUsed: number;
  usedAt: string;
  description?: string;
}

export interface LoyaltyBalanceDto {
  customerId: string;
  brandId: string;
  totalEarned: number;
  totalRedeemed: number;
  balance: number;
}

export interface WalletAccountDto {
  id: string;
  customerId: string;
  brandId: string;
  balance: number;
  currencyCode: string;
  isActive: boolean;
}

export interface WalletTransactionDto {
  id: string;
  walletId: string;
  type: 'credit' | 'debit';
  amount: number;
  balanceAfter: number;
  referenceType?: string;
  referenceId?: string;
  description?: string;
  createdAt: string;
}

export interface CouponDto {
  id: string;
  code: string;
  name?: string;
  description?: string;
  /** Backend field name — maps to couponType in CommerceDtos.cs */
  couponType: 'flat' | 'percent';
  discountValue: number;
  /** Backend field name — maps to maxDiscountAmount in CommerceDtos.cs */
  maxDiscountAmount?: number;
  minOrderValue?: number;
  /** Backend field name — maps to validUntil in CommerceDtos.cs */
  validUntil?: string;
}

export interface PaymentDto {
  id: string;
  orderId?: string;
  purpose: string;
  amount: number;
  currencyCode: string;
  status: string;
  gatewayOrderId?: string;
  razorpayKeyId?: string;
  createdAt: string;
}

export interface PurchasePackageRequest {
  packageId: string;
}

export interface WalletTopUpRequest {
  amount: number;
}

export interface VerifyPaymentRequest {
  gatewayOrderId: string;
  gatewayPaymentId: string;
  gatewaySignature: string;
}

export interface InitiatePaymentRequest {
  orderId: string;
  amount: number;
  paymentMethod?: string;
}

export interface ValidateCouponRequest {
  code: string;
  orderId?: string;
  orderAmount?: number;
}

export interface CouponRedemptionDto {
  id: string;
  couponId: string;
  couponCode: string;
  discountAmount: number;
  appliedAt: string;
}

export interface PatchProfileRequest {
  firstName?: string;
  lastName?: string;
  email?: string;
}

// ---------------------------------------------------------------------------
// Account deletion DTOs — mirrors Catalog/Customer/Self/Dtos/SelfDtos.cs
// ---------------------------------------------------------------------------

export interface AccountDeletionRequestDto {
  id: string;
  status: string;
  requestSource: string;
  reason?: string | null;
  requestedAt: string;
  gracePeriodEndsAt: string;
  cancelledAt?: string | null;
}

export interface CreateDeletionRequestRequest {
  requestSource: string;
  reason?: string | null;
  reasonText?: string | null;
}

// ---------------------------------------------------------------------------
// Engagement / CMS DTOs — mirrors PublicEngagementEndpoints + EngagementDtos.cs
// ---------------------------------------------------------------------------

/** Mirrors OnboardingSlideDto */
export interface OnboardingSlideDto {
  id: string;
  brandId: string;
  appType: string;
  title: string;
  titleLocalized: string;
  description?: string | null;
  descriptionLocalized: string;
  imageUrl: string;
  imageDarkUrl?: string | null;
  animationUrl?: string | null;
  ctaText?: string | null;
  ctaDeeplink?: string | null;
  backgroundColor?: string | null;
  textColor?: string | null;
  displayOrder: number;
  isActive: boolean;
  showFrom?: string | null;
  showUntil?: string | null;
  minAppVersion?: string | null;
  maxAppVersion?: string | null;
  targetSegments?: string[] | null;
  status: string;
  createdAt: string;
  updatedAt: string;
}

/** Mirrors AppBannerDto */
export interface AppBannerDto {
  id: string;
  brandId: string;
  appType: string;
  placement: string;
  title?: string | null;
  titleLocalized: string;
  subtitle?: string | null;
  subtitleLocalized: string;
  imageUrl: string;
  imageDarkUrl?: string | null;
  ctaText?: string | null;
  ctaDeeplink?: string | null;
  externalUrl?: string | null;
  promotionId?: string | null;
  couponId?: string | null;
  backgroundColor?: string | null;
  displayOrder: number;
  isActive: boolean;
  showFrom?: string | null;
  showUntil?: string | null;
  targetAudience?: string | null;
  targetSegments?: string[] | null;
  targetCities?: string[] | null;
  impressionsCount: number;
  clicksCount: number;
  minAppVersion?: string | null;
  status: string;
  createdAt: string;
  updatedAt: string;
}

/** Mirrors MobileAppConfigDto — each row is a key/value config entry */
export interface MobileAppConfigDto {
  id: string;
  brandId: string;
  appType: string;
  platform: string;
  configKey: string;
  configValue: string;
  description?: string | null;
  isForceUpdate: boolean;
  minAppVersion?: string | null;
  maxAppVersion?: string | null;
  targetSegments?: string[] | null;
  rolloutPercent?: number | null;
  isActive: boolean;
  status: string;
  createdAt: string;
  updatedAt: string;
}

/**
 * Parsed shape of configValue for the "app_settings" configKey.
 * The backend stores this as a JSON string — parse defensively.
 *
 * Version-gate contract (read by versionGate.ts):
 *   min_version           — soft minimum: show dismissible banner if app < min_version
 *   force_update_version  — hard minimum: blocking modal if app < force_update_version
 *   store_url             — deep-link / https URL to open for forced updates (optional)
 */
export interface AppSettingsConfigValue {
  min_version?: string;
  force_update_version?: string;
  /** App Store / Play Store URL shown in the force-update modal. */
  store_url?: string;
  maintenance_mode?: boolean;
  feature_flags?: Record<string, boolean>;
}
