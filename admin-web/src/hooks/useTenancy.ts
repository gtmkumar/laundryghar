import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getBrands,
  getFranchises,
  getStores,
  getWarehouses,
  getPlatforms,
  createStore,
  updateStore,
} from '@/api/tenancy'
import type { PaginationParams, CreateStorePayload, UpdateStorePayload } from '@/types/api'

export const tenancyKeys = {
  brands: (params?: object) => ['brands', params] as const,
  franchises: (params?: object) => ['franchises', params] as const,
  franchisesInfinite: (params?: object) => ['franchises', 'infinite', params] as const,
  stores: (params?: object) => ['stores', params] as const,
  storesInfinite: (params?: object) => ['stores', 'infinite', params] as const,
  warehouses: (params?: object) => ['warehouses', params] as const,
  platforms: (params?: object) => ['platforms', params] as const,
}

const TENANCY_PAGE_SIZE = 100

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

export function useFranchisesInfinite(brandId?: string) {
  return useInfiniteQuery({
    queryKey: tenancyKeys.franchisesInfinite({ brandId }),
    queryFn: ({ pageParam }) =>
      getFranchises({ brandId, page: pageParam, pageSize: TENANCY_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
  })
}

export function useStores(params: PaginationParams & { brandId?: string; franchiseId?: string } = {}) {
  return useQuery({
    queryKey: tenancyKeys.stores(params),
    queryFn: () => getStores(params),
  })
}

export function useStoresInfinite(brandId?: string) {
  return useInfiniteQuery({
    queryKey: tenancyKeys.storesInfinite({ brandId }),
    queryFn: ({ pageParam }) =>
      getStores({ brandId, page: pageParam, pageSize: TENANCY_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
  })
}

export function useCreateStore() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateStorePayload) => createStore(payload),
    onSuccess: () => {
      // Refresh both the paginated and infinite store lists.
      void qc.invalidateQueries({ queryKey: ['stores'] })
    },
  })
}

export function useUpdateStore() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateStorePayload }) =>
      updateStore(id, payload),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['stores'] })
    },
  })
}

export function useWarehouses(params: PaginationParams & { brandId?: string; franchiseId?: string } = {}) {
  return useQuery({
    queryKey: tenancyKeys.warehouses(params),
    queryFn: () => getWarehouses(params),
  })
}
