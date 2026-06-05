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

/** Mirrors Identity's TokenResponse record */
export interface TokenResponse {
  accessToken:  string;
  refreshToken: string;
  /** Included in system token responses */
  userType?: string;
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
