import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getBrandEntitlements,
  getModuleBundles,
  setBrandModule,
  applyBundleToBrand,
} from '@/api/entitlements'
import { useEffectiveBrandId } from './useBrandContext'

export function useBrandEntitlements() {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['entitlements', 'brand', brandId],
    queryFn: () => getBrandEntitlements(brandId!),
    enabled: !!brandId,
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
    onSuccess: () => qc.invalidateQueries({ queryKey: ['entitlements', 'brand', brandId] }),
  })
}
