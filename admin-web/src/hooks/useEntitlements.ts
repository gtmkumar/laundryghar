import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getBrandEntitlements,
  getModuleBundles,
  getBrandPlatformSubscription,
  cancelBrandPlatformSubscription,
  getPlatformBillingSummary,
  setBrandModule,
  applyBundleToBrand,
  setBrandPlatformInvoiceStatus,
  createBrandPlatformInvoicePaymentLink,
  syncBrandPlatformInvoicePayment,
} from '@/api/entitlements'
import { useEffectiveBrandId } from './useBrandContext'

export function usePlatformBillingSummary() {
  return useQuery({
    queryKey: ['entitlements', 'platform-billing'],
    queryFn: getPlatformBillingSummary,
  })
}

export function useBrandEntitlements() {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['entitlements', 'brand', brandId],
    queryFn: () => getBrandEntitlements(brandId!),
    enabled: !!brandId,
  })
}

export function useBrandPlatformSubscription() {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['entitlements', 'platform-subscription', brandId],
    queryFn: () => getBrandPlatformSubscription(brandId!),
    enabled: !!brandId,
  })
}

export function useSetBrandPlatformInvoiceStatus() {
  const qc = useQueryClient()
  const brandId = useEffectiveBrandId()
  return useMutation({
    mutationFn: (v: { invoiceId: string; status: 'paid' | 'void' }) =>
      setBrandPlatformInvoiceStatus(v.invoiceId, v.status),
    onSuccess: () => {
      // The invoice list (per-brand card) + the platform-wide MRR collected/outstanding both change.
      qc.invalidateQueries({ queryKey: ['entitlements', 'platform-subscription', brandId] })
      qc.invalidateQueries({ queryKey: ['entitlements', 'platform-billing'] })
    },
  })
}

export function useCancelBrandPlatformSubscription() {
  const qc = useQueryClient()
  const brandId = useEffectiveBrandId()
  return useMutation({
    mutationFn: () => cancelBrandPlatformSubscription(brandId!),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['entitlements', 'platform-subscription', brandId] })
      qc.invalidateQueries({ queryKey: ['entitlements', 'platform-billing'] })
    },
  })
}

export function useCreateInvoicePaymentLink() {
  const qc = useQueryClient()
  const brandId = useEffectiveBrandId()
  return useMutation({
    mutationFn: (invoiceId: string) => createBrandPlatformInvoicePaymentLink(invoiceId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['entitlements', 'platform-subscription', brandId] }),
  })
}

export function useSyncInvoicePayment() {
  const qc = useQueryClient()
  const brandId = useEffectiveBrandId()
  return useMutation({
    mutationFn: (invoiceId: string) => syncBrandPlatformInvoicePayment(invoiceId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['entitlements', 'platform-subscription', brandId] })
      qc.invalidateQueries({ queryKey: ['entitlements', 'platform-billing'] })
    },
  })
}

export function useModuleBundles() {
  return useQuery({
    queryKey: ['entitlements', 'bundles'],
    queryFn: getModuleBundles,
  })
}

export function useSetBrandModule() {
  const qc = useQueryClient()
  const brandId = useEffectiveBrandId()
  return useMutation({
    mutationFn: (v: { moduleKey: string; enabled: boolean; validUntil?: string | null }) =>
      setBrandModule(brandId!, v.moduleKey, v.enabled, v.validUntil),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['entitlements', 'brand', brandId] }),
  })
}

export function useApplyBundle() {
  const qc = useQueryClient()
  const brandId = useEffectiveBrandId()
  return useMutation({
    mutationFn: (bundleCode: string) => applyBundleToBrand(brandId!, bundleCode),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['entitlements', 'brand', brandId] })
      // Applying a priced tier also (re)creates the brand's platform subscription + first invoice.
      qc.invalidateQueries({ queryKey: ['entitlements', 'platform-subscription', brandId] })
    },
  })
}
