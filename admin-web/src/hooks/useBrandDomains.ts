import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  addBrandDomain,
  deleteBrandDomain,
  getBrandDomains,
  verifyBrandDomain,
} from '@/api/brandDomains'
import { useEffectiveBrandId } from './useBrandContext'

const key = (brandId: string | null | undefined) => ['brand-domains', brandId] as const

export function useBrandDomains() {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: key(brandId),
    queryFn: () => getBrandDomains(brandId!),
    enabled: !!brandId,
  })
}

export function useAddBrandDomain() {
  const qc = useQueryClient()
  const brandId = useEffectiveBrandId()
  return useMutation({
    mutationFn: (v: { domain: string; isPrimary: boolean }) =>
      addBrandDomain(brandId!, v.domain, v.isPrimary),
    // Not optimistic: the server generates the challenge value and normalises the hostname, so
    // there is nothing meaningful to render until it answers.
    onSuccess: () => qc.invalidateQueries({ queryKey: key(brandId) }),
  })
}

export function useVerifyBrandDomain() {
  const qc = useQueryClient()
  const brandId = useEffectiveBrandId()
  return useMutation({
    mutationFn: (domainId: string) => verifyBrandDomain(brandId!, domainId),
    // Refetch only on an actual state change — a "not found yet" result leaves the row untouched.
    onSuccess: (result) => {
      if (result.verified) qc.invalidateQueries({ queryKey: key(brandId) })
    },
  })
}

export function useDeleteBrandDomain() {
  const qc = useQueryClient()
  const brandId = useEffectiveBrandId()
  return useMutation({
    mutationFn: (domainId: string) => deleteBrandDomain(brandId!, domainId),
    onSuccess: () => qc.invalidateQueries({ queryKey: key(brandId) }),
  })
}
