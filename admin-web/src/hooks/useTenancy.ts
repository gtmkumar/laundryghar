import { useQuery } from '@tanstack/react-query'
import { getBrands, getFranchises, getStores, getWarehouses, getPlatforms } from '@/api/tenancy'
import type { PaginationParams } from '@/types/api'

export const tenancyKeys = {
  brands: (params?: object) => ['brands', params] as const,
  franchises: (params?: object) => ['franchises', params] as const,
  stores: (params?: object) => ['stores', params] as const,
  warehouses: (params?: object) => ['warehouses', params] as const,
  platforms: (params?: object) => ['platforms', params] as const,
}

export function useBrands(params: PaginationParams & { status?: string; search?: string } = {}) {
  return useQuery({
    queryKey: tenancyKeys.brands(params),
    queryFn: () => getBrands(params),
  })
}

export function usePlatforms(params: PaginationParams = {}) {
  return useQuery({
    queryKey: tenancyKeys.platforms(params),
    queryFn: () => getPlatforms(params),
  })
}

export function useFranchises(params: PaginationParams & { brandId?: string } = {}) {
  return useQuery({
    queryKey: tenancyKeys.franchises(params),
    queryFn: () => getFranchises(params),
  })
}

export function useStores(params: PaginationParams & { brandId?: string; franchiseId?: string } = {}) {
  return useQuery({
    queryKey: tenancyKeys.stores(params),
    queryFn: () => getStores(params),
  })
}

export function useWarehouses(params: PaginationParams & { brandId?: string; franchiseId?: string } = {}) {
  return useQuery({
    queryKey: tenancyKeys.warehouses(params),
    queryFn: () => getWarehouses(params),
  })
}
