import { engagementClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  PaginationParams,
  CmsListParams,
  NotificationTemplateDto,
  CreateNotificationTemplateRequest,
  UpdateNotificationTemplateRequest,
  OnboardingSlideDto,
  CreateOnboardingSlideRequest,
  UpdateOnboardingSlideRequest,
  AppBannerDto,
  CreateAppBannerRequest,
  UpdateAppBannerRequest,
  MobileAppConfigDto,
  CreateMobileAppConfigRequest,
  UpdateMobileAppConfigRequest,
  NotificationOutboxDto,
  NotificationLogDto,
  WhatsAppMessageLogDto,
} from '@/types/api'

const ADMIN = '/api/v1/admin'

// ── Notification Templates ────────────────────────────────────────────────────

export async function getNotificationTemplates(
  params: PaginationParams & { status?: string } = {},
): Promise<PaginatedList<NotificationTemplateDto>> {
  const { data } = await engagementClient.get<
    ApiResponse<PaginatedList<NotificationTemplateDto>>
  >(`${ADMIN}/notification-templates`, {
    params: { page: 1, pageSize: 20, ...params },
  })
  return unwrapPaginated(data)
}

export async function getNotificationTemplateById(
  id: string,
): Promise<NotificationTemplateDto> {
  const { data } = await engagementClient.get<ApiResponse<NotificationTemplateDto>>(
    `${ADMIN}/notification-templates/${id}`,
  )
  return unwrap(data)
}

export async function createNotificationTemplate(
  payload: CreateNotificationTemplateRequest,
): Promise<NotificationTemplateDto> {
  const { data } = await engagementClient.post<ApiResponse<NotificationTemplateDto>>(
    `${ADMIN}/notification-templates`,
    payload,
  )
  return unwrap(data)
}

export async function updateNotificationTemplate(
  id: string,
  payload: UpdateNotificationTemplateRequest,
): Promise<NotificationTemplateDto> {
  const { data } = await engagementClient.put<ApiResponse<NotificationTemplateDto>>(
    `${ADMIN}/notification-templates/${id}`,
    payload,
  )
  return unwrap(data)
}

export async function deleteNotificationTemplate(id: string): Promise<void> {
  await engagementClient.delete(`${ADMIN}/notification-templates/${id}`)
}

// ── Onboarding Slides ─────────────────────────────────────────────────────────

export async function getOnboardingSlides(
  params: PaginationParams = {},
): Promise<PaginatedList<OnboardingSlideDto>> {
  const { data } = await engagementClient.get<
    ApiResponse<PaginatedList<OnboardingSlideDto>>
  >(`${ADMIN}/onboarding-slides`, {
    params: { page: 1, pageSize: 20, ...params },
  })
  return unwrapPaginated(data)
}

export async function getOnboardingSlideById(id: string): Promise<OnboardingSlideDto> {
  const { data } = await engagementClient.get<ApiResponse<OnboardingSlideDto>>(
    `${ADMIN}/onboarding-slides/${id}`,
  )
  return unwrap(data)
}

export async function createOnboardingSlide(
  payload: CreateOnboardingSlideRequest,
): Promise<OnboardingSlideDto> {
  const { data } = await engagementClient.post<ApiResponse<OnboardingSlideDto>>(
    `${ADMIN}/onboarding-slides`,
    payload,
  )
  return unwrap(data)
}

export async function updateOnboardingSlide(
  id: string,
  payload: UpdateOnboardingSlideRequest,
): Promise<OnboardingSlideDto> {
  const { data } = await engagementClient.put<ApiResponse<OnboardingSlideDto>>(
    `${ADMIN}/onboarding-slides/${id}`,
    payload,
  )
  return unwrap(data)
}

export async function deleteOnboardingSlide(id: string): Promise<void> {
  await engagementClient.delete(`${ADMIN}/onboarding-slides/${id}`)
}

// ── App Banners ───────────────────────────────────────────────────────────────

export async function getAppBanners(
  params: PaginationParams = {},
): Promise<PaginatedList<AppBannerDto>> {
  const { data } = await engagementClient.get<ApiResponse<PaginatedList<AppBannerDto>>>(
    `${ADMIN}/app-banners`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

export async function getAppBannerById(id: string): Promise<AppBannerDto> {
  const { data } = await engagementClient.get<ApiResponse<AppBannerDto>>(
    `${ADMIN}/app-banners/${id}`,
  )
  return unwrap(data)
}

export async function createAppBanner(
  payload: CreateAppBannerRequest,
): Promise<AppBannerDto> {
  const { data } = await engagementClient.post<ApiResponse<AppBannerDto>>(
    `${ADMIN}/app-banners`,
    payload,
  )
  return unwrap(data)
}

export async function updateAppBanner(
  id: string,
  payload: UpdateAppBannerRequest,
): Promise<AppBannerDto> {
  const { data } = await engagementClient.put<ApiResponse<AppBannerDto>>(
    `${ADMIN}/app-banners/${id}`,
    payload,
  )
  return unwrap(data)
}

export async function deleteAppBanner(id: string): Promise<void> {
  await engagementClient.delete(`${ADMIN}/app-banners/${id}`)
}

// ── Mobile App Config ─────────────────────────────────────────────────────────

export async function getMobileAppConfigs(
  params: PaginationParams = {},
): Promise<PaginatedList<MobileAppConfigDto>> {
  const { data } = await engagementClient.get<
    ApiResponse<PaginatedList<MobileAppConfigDto>>
  >(`${ADMIN}/app-config`, {
    params: { page: 1, pageSize: 20, ...params },
  })
  return unwrapPaginated(data)
}

export async function getMobileAppConfigById(id: string): Promise<MobileAppConfigDto> {
  const { data } = await engagementClient.get<ApiResponse<MobileAppConfigDto>>(
    `${ADMIN}/app-config/${id}`,
  )
  return unwrap(data)
}

export async function createMobileAppConfig(
  payload: CreateMobileAppConfigRequest,
): Promise<MobileAppConfigDto> {
  const { data } = await engagementClient.post<ApiResponse<MobileAppConfigDto>>(
    `${ADMIN}/app-config`,
    payload,
  )
  return unwrap(data)
}

export async function updateMobileAppConfig(
  id: string,
  payload: UpdateMobileAppConfigRequest,
): Promise<MobileAppConfigDto> {
  const { data } = await engagementClient.put<ApiResponse<MobileAppConfigDto>>(
    `${ADMIN}/app-config/${id}`,
    payload,
  )
  return unwrap(data)
}

export async function deleteMobileAppConfig(id: string): Promise<void> {
  await engagementClient.delete(`${ADMIN}/app-config/${id}`)
}

// ── Notification Outbox ───────────────────────────────────────────────────────

export async function getNotificationOutbox(
  params: CmsListParams = {},
): Promise<PaginatedList<NotificationOutboxDto>> {
  const { data } = await engagementClient.get<
    ApiResponse<PaginatedList<NotificationOutboxDto>>
  >(`${ADMIN}/notification-outbox`, {
    params: { page: 1, pageSize: 20, ...params },
  })
  return unwrapPaginated(data)
}

export async function retryNotificationOutbox(id: string): Promise<void> {
  await engagementClient.post(`${ADMIN}/notification-outbox/${id}/retry`)
}

// ── Notification Logs ─────────────────────────────────────────────────────────

export async function getNotificationLogs(
  params: CmsListParams = {},
): Promise<PaginatedList<NotificationLogDto>> {
  const { data } = await engagementClient.get<
    ApiResponse<PaginatedList<NotificationLogDto>>
  >(`${ADMIN}/notification-logs`, {
    params: { page: 1, pageSize: 20, ...params },
  })
  return unwrapPaginated(data)
}

// ── WhatsApp Logs ─────────────────────────────────────────────────────────────

export async function getWhatsAppLogs(
  params: CmsListParams = {},
): Promise<PaginatedList<WhatsAppMessageLogDto>> {
  const { data } = await engagementClient.get<
    ApiResponse<PaginatedList<WhatsAppMessageLogDto>>
  >(`${ADMIN}/whatsapp-logs`, {
    params: { page: 1, pageSize: 20, ...params },
  })
  return unwrapPaginated(data)
}
