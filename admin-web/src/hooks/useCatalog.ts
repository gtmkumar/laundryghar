import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import {
  getServiceCategories,
  getServices,
  getItems,
  getPriceLists,
  getAdminCustomers,
} from '@/api/catalog'
import type { PaginationParams, AdminCustomerListParams } from '@/types/api'

export const catalogKeys = {
  categories: (params?: object) => ['catalog', 'categories', params] as const,
  categoriesInfinite: (params?: object) => ['catalog', 'categories', 'infinite', params] as const,
  services: (params?: object) => ['catalog', 'services', params] as const,
  servicesInfinite: (params?: object) => ['catalog', 'services', 'infinite', params] as const,
  items: (params?: object) => ['catalog', 'items', params] as const,
  priceLists: (params?: object) => ['catalog', 'priceLists', params] as const,
  priceListsInfinite: (params?: object) => ['catalog', 'priceLists', 'infinite', params] as const,
  adminCustomers: (params?: object) => ['catalog', 'adminCustomers', params] as const,
}

const CATALOG_PAGE_SIZE = 100

export function useServiceCategories(params: PaginationParams = {}) {
  return useQuery({
    queryKey: catalogKeys.categories(params),
    queryFn: () => getServiceCategories(params),
  })
}

export function useServiceCategoriesInfinite() {
  return useInfiniteQuery({
    queryKey: catalogKeys.categoriesInfinite(),
    queryFn: ({ pageParam }) =>
      getServiceCategories({ page: pageParam, pageSize: CATALOG_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
  })
}

export function useServices(params: PaginationParams & { categoryId?: string } = {}) {
  return useQuery({
    queryKey: catalogKeys.services(params),
    queryFn: () => getServices(params),
  })
}

export function useServicesInfinite() {
  return useInfiniteQuery({
    queryKey: catalogKeys.servicesInfinite(),
    queryFn: ({ pageParam }) =>
      getServices({ page: pageParam, pageSize: CATALOG_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
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

export function usePriceListsInfinite() {
  return useInfiniteQuery({
    queryKey: catalogKeys.priceListsInfinite(),
    queryFn: ({ pageParam }) =>
      getPriceLists({ page: pageParam, pageSize: CATALOG_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
  })
}

// ── Admin Customers ───────────────────────────────────────────────────────────

export function useAdminCustomers(params: AdminCustomerListParams = {}, enabled = true) {
  return useQuery({
    queryKey: catalogKeys.adminCustomers(params),
    queryFn: () => getAdminCustomers(params),
    enabled,
    // Customer names change rarely — 5 min stale time is fine
    staleTime: 5 * 60_000,
  })
}

/**
 * Returns a stable Map<customerId, displayName> derived from the first page of
 * admin customers (pageSize 100). Falls back gracefully if the query is still
 * loading or returns no data. Used in the dashboard live-order feed.
 */
export function useCustomerNameMap(enabled = true): Map<string, string> {
  const { data } = useAdminCustomers({ pageSize: 100 }, enabled)

  return useMemo(() => {
    const map = new Map<string, string>()
    for (const c of data?.list ?? []) {
      // Prefer displayName, then firstName+lastName, then customerCode
      const name =
        c.displayName?.trim() ||
        [c.firstName, c.lastName].filter(Boolean).join(' ').trim() ||
        c.customerCode
      map.set(c.id, name)
    }
    return map
  }, [data])
}
