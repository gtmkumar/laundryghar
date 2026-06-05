import { useQuery } from '@tanstack/react-query'
import {
  getServiceCategories,
  getServices,
  getItems,
  getPriceLists,
} from '@/api/catalog'
import type { PaginationParams } from '@/types/api'

export const catalogKeys = {
  categories: (params?: object) => ['catalog', 'categories', params] as const,
  services: (params?: object) => ['catalog', 'services', params] as const,
  items: (params?: object) => ['catalog', 'items', params] as const,
  priceLists: (params?: object) => ['catalog', 'priceLists', params] as const,
}

export function useServiceCategories(params: PaginationParams = {}) {
  return useQuery({
    queryKey: catalogKeys.categories(params),
    queryFn: () => getServiceCategories(params),
  })
}

export function useServices(params: PaginationParams & { categoryId?: string } = {}) {
  return useQuery({
    queryKey: catalogKeys.services(params),
    queryFn: () => getServices(params),
  })
}

export function useItems(params: PaginationParams & { itemGroupId?: string } = {}) {
  return useQuery({
    queryKey: catalogKeys.items(params),
    queryFn: () => getItems(params),
  })
}

export function usePriceLists(params: PaginationParams = {}) {
  return useQuery({
    queryKey: catalogKeys.priceLists(params),
    queryFn: () => getPriceLists(params),
  })
}
