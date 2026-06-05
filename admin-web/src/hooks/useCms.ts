import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import type { PaginationParams, CmsListParams } from '@/types/api'
import {
  getNotificationTemplates,
  getNotificationTemplateById,
  createNotificationTemplate,
  updateNotificationTemplate,
  deleteNotificationTemplate,
  getOnboardingSlides,
  getOnboardingSlideById,
  createOnboardingSlide,
  updateOnboardingSlide,
  deleteOnboardingSlide,
  getAppBanners,
  getAppBannerById,
  createAppBanner,
  updateAppBanner,
  deleteAppBanner,
  getMobileAppConfigs,
  getMobileAppConfigById,
  createMobileAppConfig,
  updateMobileAppConfig,
  deleteMobileAppConfig,
  getNotificationOutbox,
  retryNotificationOutbox,
  getNotificationLogs,
  getWhatsAppLogs,
} from '@/api/engagement'
import type {
  CreateNotificationTemplateRequest,
  UpdateNotificationTemplateRequest,
  CreateOnboardingSlideRequest,
  UpdateOnboardingSlideRequest,
  CreateAppBannerRequest,
  UpdateAppBannerRequest,
  CreateMobileAppConfigRequest,
  UpdateMobileAppConfigRequest,
} from '@/types/api'

// ── Query key factory ─────────────────────────────────────────────────────────

export const cmsKeys = {
  templates: (params?: object) => ['cms', 'templates', params] as const,
  template: (id: string) => ['cms', 'templates', id] as const,
  slides: (params?: object) => ['cms', 'slides', params] as const,
  slide: (id: string) => ['cms', 'slides', id] as const,
  banners: (params?: object) => ['cms', 'banners', params] as const,
  banner: (id: string) => ['cms', 'banners', id] as const,
  appConfigs: (params?: object) => ['cms', 'appConfigs', params] as const,
  appConfig: (id: string) => ['cms', 'appConfigs', id] as const,
  outbox: (params?: object) => ['cms', 'outbox', params] as const,
  logs: (params?: object) => ['cms', 'logs', params] as const,
  waLogs: (params?: object) => ['cms', 'waLogs', params] as const,
}

// ── Notification Templates ────────────────────────────────────────────────────

export function useNotificationTemplates(params: PaginationParams & { status?: string } = {}) {
  return useQuery({
    queryKey: cmsKeys.templates(params),
    queryFn: () => getNotificationTemplates(params),
  })
}

export function useNotificationTemplate(id: string) {
  return useQuery({
    queryKey: cmsKeys.template(id),
    queryFn: () => getNotificationTemplateById(id),
    enabled: Boolean(id),
  })
}

export function useCreateNotificationTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateNotificationTemplateRequest) =>
      createNotificationTemplate(payload),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cms', 'templates'] })
    },
  })
}

export function useUpdateNotificationTemplate(id: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: UpdateNotificationTemplateRequest) =>
      updateNotificationTemplate(id, payload),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cms', 'templates'] })
    },
  })
}

export function useDeleteNotificationTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteNotificationTemplate(id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cms', 'templates'] })
    },
  })
}

// ── Onboarding Slides ─────────────────────────────────────────────────────────

export function useOnboardingSlides(params: PaginationParams = {}) {
  return useQuery({
    queryKey: cmsKeys.slides(params),
    queryFn: () => getOnboardingSlides(params),
  })
}

export function useOnboardingSlide(id: string) {
  return useQuery({
    queryKey: cmsKeys.slide(id),
    queryFn: () => getOnboardingSlideById(id),
    enabled: Boolean(id),
  })
}

export function useCreateOnboardingSlide() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateOnboardingSlideRequest) => createOnboardingSlide(payload),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cms', 'slides'] })
    },
  })
}

export function useUpdateOnboardingSlide(id: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: UpdateOnboardingSlideRequest) =>
      updateOnboardingSlide(id, payload),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cms', 'slides'] })
    },
  })
}

export function useDeleteOnboardingSlide() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteOnboardingSlide(id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cms', 'slides'] })
    },
  })
}

// ── App Banners ───────────────────────────────────────────────────────────────

export function useAppBanners(params: PaginationParams = {}) {
  return useQuery({
    queryKey: cmsKeys.banners(params),
    queryFn: () => getAppBanners(params),
  })
}

export function useAppBanner(id: string) {
  return useQuery({
    queryKey: cmsKeys.banner(id),
    queryFn: () => getAppBannerById(id),
    enabled: Boolean(id),
  })
}

export function useCreateAppBanner() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateAppBannerRequest) => createAppBanner(payload),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cms', 'banners'] })
    },
  })
}

export function useUpdateAppBanner(id: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: UpdateAppBannerRequest) => updateAppBanner(id, payload),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cms', 'banners'] })
    },
  })
}

export function useDeleteAppBanner() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteAppBanner(id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cms', 'banners'] })
    },
  })
}

// ── Mobile App Config ─────────────────────────────────────────────────────────

export function useMobileAppConfigs(params: PaginationParams = {}) {
  return useQuery({
    queryKey: cmsKeys.appConfigs(params),
    queryFn: () => getMobileAppConfigs(params),
  })
}

export function useMobileAppConfig(id: string) {
  return useQuery({
    queryKey: cmsKeys.appConfig(id),
    queryFn: () => getMobileAppConfigById(id),
    enabled: Boolean(id),
  })
}

export function useCreateMobileAppConfig() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateMobileAppConfigRequest) => createMobileAppConfig(payload),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cms', 'appConfigs'] })
    },
  })
}

export function useUpdateMobileAppConfig(id: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: UpdateMobileAppConfigRequest) =>
      updateMobileAppConfig(id, payload),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cms', 'appConfigs'] })
    },
  })
}

export function useDeleteMobileAppConfig() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteMobileAppConfig(id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cms', 'appConfigs'] })
    },
  })
}

// ── Notification Outbox ───────────────────────────────────────────────────────

export function useNotificationOutbox(params: CmsListParams = {}) {
  return useQuery({
    queryKey: cmsKeys.outbox(params),
    queryFn: () => getNotificationOutbox(params),
  })
}

export function useRetryNotificationOutbox() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => retryNotificationOutbox(id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cms', 'outbox'] })
    },
  })
}

// ── Notification Logs ─────────────────────────────────────────────────────────

export function useNotificationLogs(params: CmsListParams = {}) {
  return useQuery({
    queryKey: cmsKeys.logs(params),
    queryFn: () => getNotificationLogs(params),
  })
}

export function useWhatsAppLogs(params: CmsListParams = {}) {
  return useQuery({
    queryKey: cmsKeys.waLogs(params),
    queryFn: () => getWhatsAppLogs(params),
  })
}
