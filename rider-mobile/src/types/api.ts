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

// ---------------------------------------------------------------------------
// Auth DTOs — rider uses the shared password login endpoint
// POST /api/v1/auth/password/login  →  TokenResponse
// POST /api/v1/auth/refresh         →  TokenResponse
// POST /api/v1/auth/logout          →  (status only)
// ---------------------------------------------------------------------------

export interface PasswordLoginRequest {
  /** Phone, email, or rider code — the backend accepts any unique identifier */
  identifier: string;
  password:   string;
}

/** Mirrors Identity's TokenResponse / OtpVerifiedResponse records */
export interface TokenResponse {
  accessToken:       string;
  refreshToken:      string;
  expiresInSeconds?: number;
  tokenType?:        string;
  /** Included in system token responses */
  userType?: string;
}

// --- OTP login (phone) — POST /auth/otp/send + /auth/otp/verify -------------

/** identifierType is "phone" | "email"; purpose is "login" for sign-in. */
export interface OtpSendRequest {
  identifier:     string;   // E.164 phone, e.g. "+919877001234"
  identifierType: string;
  purpose:        string;
}

export interface OtpSentResponse {
  message:   string;
  expiresAt: string;   // ISO-8601
}

export interface OtpVerifyRequest {
  identifier:     string;
  identifierType: string;
  purpose:        string;
  code:           string;
}

// ---------------------------------------------------------------------------
// Rider self-service DTOs — mirrors Logistics RiderDto / RiderSelfDtos
// ---------------------------------------------------------------------------

/** Mirrors RiderDto from laundryghar.Logistics.Application.Riders.Dtos */
export interface RiderDto {
  id:                     string;
  userId:                 string;
  brandId:                string;
  franchiseId:            string;
  primaryStoreId?:        string | null;
  riderCode:              string;
  employmentType:         string;
  vehicleType:            string;
  vehicleNumber?:         string | null;
  vehicleModel?:          string | null;
  drivingLicenseNumber?:  string | null;
  dlExpiryDate?:          string | null;    // DateOnly as ISO string "YYYY-MM-DD"
  insuranceExpiryDate?:   string | null;
  dailyPickupCapacity:    number;
  dailyDeliveryCapacity:  number;
  serviceRadiusKm:        number;
  ratingAverage?:         number | null;
  ratingCount:            number;
  completionRate?:        number | null;
  lifetimeDeliveries:     number;
  isOnline:               boolean;
  isOnDuty:               boolean;
  currentLoad:            number;
  kycStatus:              string;
  status:                 string;
  createdAt:              string;
  updatedAt:              string;
  // Enriched (joined) fields returned by GET /rider/me
  riderName?:             string | null;
  email?:                 string | null;
  phone?:                 string | null;    // E.164
  userStatus?:            string | null;
  franchiseName?:         string | null;
  primaryStoreName?:      string | null;
}

/** Mirrors RiderAssignmentDto */
export interface RiderAssignmentDto {
  id:                  string;
  riderId:             string;
  brandId:             string;
  storeId:             string;
  shiftDate:           string;   // DateOnly as "YYYY-MM-DD"
  shiftStart:          string;   // TimeOnly as "HH:mm:ss"
  shiftEnd:            string;
  actualStartAt?:      string | null;
  actualEndAt?:        string | null;
  maxPickups:          number;
  maxDeliveries:       number;
  completedPickups:    number;
  completedDeliveries: number;
  failedAttempts:      number;
  totalDistanceKm?:    number | null;
  earnings?:           number | null;
  status:              RiderAssignmentStatus;
  notes?:              string | null;
  createdAt:           string;
  updatedAt:           string;
}

/**
 * Valid rider assignment statuses.
 * Riders may transition: scheduled → active → (on_break | completed | cancelled).
 */
export type RiderAssignmentStatus =
  | 'scheduled'
  | 'active'
  | 'on_break'
  | 'completed'
  | 'cancelled';

/** Body for PATCH /api/v1/rider/assignments/{id}/status */
export interface RiderAssignmentStatusUpdateRequest {
  status: RiderAssignmentStatus;
}

// ---------------------------------------------------------------------------
// Rider task DTOs (mobile-facing view of a per-order pickup/delivery job).
//
// The backend models these as `logistics.delivery_assignments` joined to an
// order, but exposes NO rider-facing route group for them yet. This shape is
// the contract a future GET /api/v1/rider/tasks/today should return; until it
// ships, src/api/tasks.ts serves a labelled demo set. See HANDOFF backlog.
// ---------------------------------------------------------------------------

export type TaskLegType    = 'pickup' | 'delivery' | 'return';
export type RiderTaskStatus =
  | 'assigned'   // waiting for the rider to start
  | 'started'    // en route
  | 'arrived'
  | 'completed'
  | 'failed'
  | 'cancelled';

export interface RiderTask {
  id:            string;
  orderNumber:   string;        // "LG-28407"
  legType:       TaskLegType;
  status:        RiderTaskStatus;
  isExpress:     boolean;
  customerName:  string;
  customerPhone: string;        // E.164 — used by the Call action
  addressLine:   string;        // "B-12, Sushant Lok 1"
  zoneLabel?:    string;        // "Sec 45 · T-22"
  distanceKm:    number;
  etaMinutes?:   number;
  windowStart?:  string;        // "12:00"
  windowEnd?:    string;        // "14:00"
  scheduledTime?: string;       // "15:30" (single-point ETA)
  garmentCount:  number;
  amountDue:     number;        // 0 when prepaid
  isPaid:        boolean;
  /**
   * DEMO ONLY: the 4-digit code, present in the local demo set so the demo flow
   * can validate client-side. The real API never sends the code — it sets
   * `requiresOtp` and verifies what the rider types via POST /tasks/{id}/verify-otp.
   */
  deliveryOtp?:  string;
  /** Real API: this leg needs a customer OTP to complete (the code itself is never sent). */
  requiresOtp?:  boolean;
  /** Real API: whether the OTP has already been verified on the server. */
  otpVerified?:  boolean;
  /** Route order within the rider's queue. */
  sequenceNumber?: number;
  payout:        number;        // rider earning for this leg (₹)
  lat?:          number;
  lng?:          number;
  completedAt?:  string;        // ISO — set when status=completed
  rating?:       number;        // 1..5 customer rating after completion
  // Phase 2: drop-at-laundry round-trip (pickup legs collect at the customer,
  // then drop at the store). The server stamps these; geofence drives arrival.
  collectedAt?:  string;        // ISO — pickup: collected from the customer
  droppedAt?:    string;        // ISO — pickup: dropped at the store/laundry
  /** to_customer | at_customer | to_store | dropped | completed | failed | cancelled | assigned */
  phase?:        RiderTaskPhase;
}

export type RiderTaskPhase =
  | 'assigned'
  | 'to_customer'
  | 'at_customer'
  | 'to_store'     // pickup only: collected, heading to the laundry
  | 'dropped'      // pickup only: dropped at the laundry
  | 'completed'
  | 'failed'
  | 'cancelled';

// ---------------------------------------------------------------------------
// Rider KYC document DTOs — mirrors the rider self-service /rider/documents
// contract (GET status + POST multipart upload).
// ---------------------------------------------------------------------------

/** The five document slots a rider must provide. */
export type RiderDocType = 'license' | 'rc' | 'insurance' | 'id' | 'photo';

/** Per-document review state. */
export type RiderDocStatus = 'pending' | 'approved' | 'rejected';

/**
 * Overall KYC / vehicle verification state. Backend may return either the
 * compact ('pending') or expanded ('under_review') wording — both are handled
 * by the badge mapping in the UI.
 */
export type RiderVerificationStatus =
  | 'pending'
  | 'under_review'
  | 'approved'
  | 'verified'
  | 'rejected';

/** A single uploaded document and its review outcome. */
export interface RiderDocumentDto {
  id:               string;
  docType:          RiderDocType;
  fileName:         string;
  status:           RiderDocStatus;
  rejectionReason?: string | null;
  reviewedAt?:      string | null;   // ISO-8601
  uploadedAt:       string;          // ISO-8601
}

/** Response of GET /api/v1/rider/documents. */
export interface RiderVerificationDto {
  kycStatus:                 RiderVerificationStatus;
  vehicleVerificationStatus: RiderVerificationStatus;
  vehicleRejectionReason?:   string | null;
  documents:                 RiderDocumentDto[];
}

// ---------------------------------------------------------------------------
// Rider payouts DTOs — mirrors RiderPayoutSummaryDto / RiderPayoutDayDto
// ---------------------------------------------------------------------------

export interface RiderPayoutDayDto {
  date:         string;   // DateOnly as "YYYY-MM-DD"
  taskCount:    number;
  totalPayout:  number;
}

export interface RiderPayoutSummaryDto {
  totalPayout: number;
  avgPerTask:  number;
  days:        number;
  breakdown:   RiderPayoutDayDto[];
}

// ---------------------------------------------------------------------------
// Rider cash summary DTOs — mirrors RiderCashSummaryDto
// ---------------------------------------------------------------------------

export interface RiderCashSettlementItemDto {
  settledAt: string;   // ISO-8601
  amount:    number;
}

export interface RiderCashSummaryDto {
  cashInHand:          number;
  lastSettlementAt:    string | null;
  recentSettlements:   RiderCashSettlementItemDto[];
}

// ---------------------------------------------------------------------------
// Rider balance + payout request + incentive DTOs
// (GET /rider/balance, /rider/payout-requests, POST /rider/payout-requests,
//  GET /rider/incentives) — mirror the LIVE self-service payout endpoints.
// ---------------------------------------------------------------------------

/** Withdrawable-balance breakdown. `available` = earned + incentives − withdrawnOrPending. */
export interface RiderBalanceDto {
  earnedPayout:       number;
  incentives:         number;
  withdrawnOrPending: number;
  available:          number;
}

/** Lifecycle status of a rider's withdrawal (payout) request. */
export type RiderPayoutRequestStatus = 'requested' | 'approved' | 'rejected' | 'paid';

/** A single withdrawal request and its review/payment outcome. */
export interface RiderPayoutRequestDto {
  id:                string;
  amount:            number;
  status:            RiderPayoutRequestStatus;
  rejectionReason:   string | null;
  paymentReference:  string | null;
  requestedAt:       string;        // ISO-8601
  reviewedAt:        string | null; // ISO-8601
  paidAt:            string | null; // ISO-8601
}

/** A single incentive/bonus award. */
export interface RiderIncentiveDto {
  id:        string;
  ruleName:  string;
  ruleType:  string;
  amount:    number;
  awardedAt: string;   // ISO-8601
}

// ---------------------------------------------------------------------------
// Location ping DTOs — mirrors LocationPingInput / PingBatchResponse
// ---------------------------------------------------------------------------

/** Single location ping (sent as element of a batch array) */
export interface LocationPingInput {
  latitude:            number;
  longitude:           number;
  accuracyMeters?:     number | null;
  speedKmph?:          number | null;
  headingDegrees?:     number | null;
  batteryPercent?:     number | null;
  isMoving?:           boolean | null;
  activityType?:       string | null;
  currentAssignmentId?: string | null;
  /** ISO-8601 datetime of the recorded location */
  pingedAt:            string;
}

/** Response from POST /api/v1/rider/location/ping */
export interface PingBatchResponse {
  /** Number of pings accepted by the server */
  accepted: number;
}

// ---------------------------------------------------------------------------
// Engagement / CMS DTOs — mirrors PublicEngagementEndpoints + EngagementDtos.cs
// ---------------------------------------------------------------------------

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
 */
export interface AppSettingsConfigValue {
  min_version?: string;
  force_update_version?: string;
  maintenance_mode?: boolean;
  feature_flags?: Record<string, boolean>;
}

// ===========================================================================
// Support tickets — rider self-service helpdesk
// Maps to GET/POST /api/v1/rider/support/tickets[/{id}[/messages]]
// ===========================================================================

export type SupportTicketStatus = 'open' | 'in_progress' | 'resolved' | 'closed';

export type TicketMessageSenderType = 'rider' | 'agent' | 'system';

export interface SupportTicketDto {
  id: string;
  ticketNumber: string;
  requesterType: string;
  subject: string;
  category: string | null;
  priority: string;
  status: SupportTicketStatus;
  orderId: string | null;
  lastMessageAt: string | null;
  createdAt: string;
}

export interface TicketMessageDto {
  id: string;
  senderType: TicketMessageSenderType;
  senderId: string | null;
  body: string;
  createdAt: string;
}

/** GET /rider/support/tickets/{id} and POST /rider/support/tickets responses. */
export interface SupportTicketThreadDto {
  ticket: SupportTicketDto;
  messages: TicketMessageDto[];
}

/** Body for POST /rider/support/tickets. */
export interface CreateSupportTicketRequest {
  subject: string;
  message: string;
  category?: string;
  orderId?: string;
}
