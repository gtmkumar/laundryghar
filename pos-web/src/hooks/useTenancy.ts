import { useQuery } from '@tanstack/react-query'
import { getBrands, getStores } from '@/api/tenancy'
import type { PaginationParams } from '@/types/api'

export const tenancyKeys = {
  brands: (params?: object) => ['brands', params] as const,
  stores: (params?: object) => ['stores', params] as const,
}

export function useBrands(params: PaginationParams & { status?: string } = {}) {
  return useQuery({
    queryKey: tenancyKeys.brands(params),
    queryFn: () => getBrands(params),
  })
}

export function useStores(params: PaginationParams & { brandId?: string; franchiseId?: string } = {}) {
  return useQuery({
    queryKey: tenancyKeys.stores(params),
    queryFn: () => getStores(params),
  })
}
