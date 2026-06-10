import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getSettings,
  updateEmailSettings,
  sendTestEmail,
  updateProvisioning,
  updateMapsSettings,
  updatePayoutSettings,
  updatePaymentGatewaySettings,
  updateWhatsAppSettings,
  updateSmsSettings,
} from '@/api/settings'
import type {
  UpdateEmailPayload,
  UpdateMapsPayload,
  UpdatePayoutPayload,
  UpdatePaymentGatewayPayload,
  UpdateWhatsAppPayload,
  UpdateSmsPayload,
} from '@/types/api'
import { useEffectiveBrandId } from './useBrandContext'

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
