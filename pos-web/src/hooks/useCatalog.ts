import { useQuery } from '@tanstack/react-query'
import {
  getServiceCategories,
  getServices,
  getItems,
  resolvePrice,
} from '@/api/catalog'
import type { PaginationParams } from '@/types/api'

export const catalogKeys = {
  categories: (params?: object) => ['catalog', 'categories', params] as const,
  services: (params?: object) => ['catalog', 'services', params] as const,
  items: (params?: object) => ['catalog', 'items', params] as const,
  priceResolve: (itemId: string, serviceId: string, storeId?: string, variantId?: string) =>
    ['catalog', 'price-resolve', itemId, serviceId, storeId, variantId] as const,
}

export function useServiceCategories(params: PaginationParams = {}) {
  return useQuery({
    queryKey: catalogKeys.categories(params),
    queryFn: () => getServiceCategories(params),
    staleTime: 5 * 60_000,
  })
}

export function useServices(params: PaginationParams & { categoryId?: string } = {}) {
  return useQuery({
    queryKey: catalogKeys.services(params),
    queryFn: () => getServices(params),
    staleTime: 5 * 60_000,
  })
}

export function useItems(params: PaginationParams & { itemGroupId?: string } = {}) {
  return useQuery({
    queryKey: catalogKeys.items(params),
    queryFn: () => getItems(params),
    staleTime: 5 * 60_000,
  })
}

export function usePriceResolve(
  itemId: string,
  serviceId: string,
  storeId?: string,
  variantId?: string,
) {
  return useQuery({
    queryKey: catalogKeys.priceResolve(itemId, serviceId, storeId, variantId),
    queryFn: () => resolvePrice(itemId, serviceId, storeId, variantId),
    enabled: Boolean(itemId) && Boolean(serviceId),
    staleTime: 5 * 60_000,
  })
}
