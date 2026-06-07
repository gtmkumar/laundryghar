import { useQuery } from '@tanstack/react-query'
import {
  getServiceCategories,
  getServices,
  getItems,
  resolvePrice,
} from '@/api/catalog'
import { useBrandStore } from '@/stores/brandStore'
import { useAuthStore } from '@/stores/authStore'
import type { PaginationParams } from '@/types/api'

/**
 * Returns the effective brand id for the current session:
 *  - For brand-scoped users (brand_id in JWT), taken from the token.
 *  - For platform_admin, taken from the manual brand selection in brandStore.
 * Catalog queries MUST not fire without a brand context; gating on this prevents
 * guaranteed 401s when platform_admin logs in before selecting a brand.
 */
function useEffectiveBrandId(): string | null {
  const { accessToken } = useAuthStore()
  const { activeBrandId } = useBrandStore()

  if (accessToken) {
    try {
      const payload = JSON.parse(
        atob(accessToken.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')),
      ) as { brand_id?: string }
      if (payload.brand_id) return payload.brand_id
    } catch {
      // ignore parse errors
    }
  }

  return activeBrandId
}

export const catalogKeys = {
  categories: (params?: object) => ['catalog', 'categories', params] as const,
  services: (params?: object) => ['catalog', 'services', params] as const,
  items: (params?: object) => ['catalog', 'items', params] as const,
  priceResolve: (itemId: string, serviceId: string, storeId?: string, variantId?: string) =>
    ['catalog', 'price-resolve', itemId, serviceId, storeId, variantId] as const,
}

export function useServiceCategories(params: PaginationParams = {}) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: catalogKeys.categories(params),
    queryFn: () => getServiceCategories(params),
    enabled: !!brandId,
    staleTime: 5 * 60_000,
  })
}

export function useServices(params: PaginationParams & { categoryId?: string } = {}) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: catalogKeys.services(params),
    queryFn: () => getServices(params),
    enabled: !!brandId,
    staleTime: 5 * 60_000,
  })
}

export function useItems(params: PaginationParams & { itemGroupId?: string } = {}) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: catalogKeys.items(params),
    queryFn: () => getItems(params),
    enabled: !!brandId,
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
