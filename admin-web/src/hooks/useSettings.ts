import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getSettings,
  updateEmailSettings,
  sendTestEmail,
  updateProvisioning,
  updateMapsSettings,
  updatePayoutSettings,
  updatePaymentGatewaySettings,
  getPlatformPaymentGateway,
  updatePlatformPaymentGateway,
  updateWhatsAppSettings,
  updateSmsSettings,
  getFareSettings,
  updateFareSettings,
  getDispatchSettings,
  updateDispatchSettings,
} from '@/api/settings'
import type {
  UpdateEmailPayload,
  UpdateMapsPayload,
  UpdatePayoutPayload,
  UpdatePaymentGatewayPayload,
  UpdatePlatformPaymentGatewayPayload,
  UpdateWhatsAppPayload,
  UpdateSmsPayload,
  FareSettings,
  DispatchSettings,
} from '@/types/api'
import { useEffectiveBrandId } from './useBrandContext'
import { usePermissions } from './usePermissions'

export function useSettings() {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['settings', brandId],
    queryFn: getSettings,
    enabled: !!brandId,
  })
}

export function useUpdateEmailSettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: UpdateEmailPayload) => updateEmailSettings(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings'] }),
  })
}

export function useSendTestEmail() {
  return useMutation({
    mutationFn: (v: { to: string; settings?: UpdateEmailPayload }) => sendTestEmail(v.to, v.settings),
  })
}

export function useUpdateProvisioning() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (mode: string) => updateProvisioning(mode),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings'] }),
  })
}

export function useUpdateMapsSettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: UpdateMapsPayload) => updateMapsSettings(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings'] }),
  })
}

export function useUpdatePayoutSettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: UpdatePayoutPayload) => updatePayoutSettings(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings'] }),
  })
}

export function useUpdatePaymentGatewaySettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: UpdatePaymentGatewayPayload) => updatePaymentGatewaySettings(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings'] }),
  })
}

// ── Platform billing (operator's SaaS-collection Razorpay account) — platform-admin only ──

export function usePlatformPaymentGateway() {
  const { isPlatformAdmin } = usePermissions()
  return useQuery({
    queryKey: ['settings', 'platform-payment'],
    queryFn: getPlatformPaymentGateway,
    enabled: isPlatformAdmin, // platform-scoped; the endpoint 403s for non-platform admins
  })
}

export function useUpdatePlatformPaymentGateway() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: UpdatePlatformPaymentGatewayPayload) => updatePlatformPaymentGateway(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings', 'platform-payment'] }),
  })
}

export function useUpdateWhatsAppSettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: UpdateWhatsAppPayload) => updateWhatsAppSettings(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings'] }),
  })
}

export function useUpdateSmsSettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: UpdateSmsPayload) => updateSmsSettings(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings'] }),
  })
}

// ── Marketplace fare & dispatch ──────────────────────────────────────────────

export function useFareSettings() {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['settings', 'fare', brandId],
    queryFn: getFareSettings,
    enabled: !!brandId,
  })
}

export function useUpdateFareSettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: FareSettings) => updateFareSettings(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings', 'fare'] }),
  })
}

export function useDispatchSettings() {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['settings', 'dispatch', brandId],
    queryFn: getDispatchSettings,
    enabled: !!brandId,
  })
}

export function useUpdateDispatchSettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: DispatchSettings) => updateDispatchSettings(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings', 'dispatch'] }),
  })
}
