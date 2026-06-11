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
export function useEffectiveBrandId(): string | null {
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

/**
 * POS-5: brandId is part of every catalog query key, not just the `enabled`
 * gate. Catalog data (categories/services/items/price) is brand-scoped via the
 * X-Brand-Id header, so without brandId in the key a platform_admin switching
 * brands would be served the previous brand's cached catalog — wrong names and,
 * critically, wrong prices on real orders. Keying on brandId gives each brand
 * its own cache entry and forces a refetch on switch.
 */
export const catalogKeys = {
  categories: (brandId: string | null, params?: object) =>
    ['catalog', 'categories', brandId, params] as const,
  services: (brandId: string | null, params?: object) =>
    ['catalog', 'services', brandId, params] as const,
  items: (brandId: string | null, params?: object) =>
    ['catalog', 'items', brandId, params] as const,
  priceResolve: (
    brandId: string | null,
    itemId: string,
    serviceId: string,
    storeId?: string,
    variantId?: string,
  ) => ['catalog', 'price-resolve', brandId, itemId, serviceId, storeId, variantId] as const,
}

export function useServiceCategories(params: PaginationParams = {}) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: catalogKeys.categories(brandId, params),
    queryFn: () => getServiceCategories(params),
    enabled: !!brandId,
    staleTime: 5 * 60_000,
  })
}

export function useServices(params: PaginationParams & { categoryId?: string } = {}) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: catalogKeys.services(brandId, params),
    queryFn: () => getServices(params),
    enabled: !!brandId,
    staleTime: 5 * 60_000,
  })
}

export function useItems(params: PaginationParams & { itemGroupId?: string } = {}) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: catalogKeys.items(brandId, params),
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
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: catalogKeys.priceResolve(brandId, itemId, serviceId, storeId, variantId),
    queryFn: () => resolvePrice(itemId, serviceId, storeId, variantId),
    enabled: Boolean(itemId) && Boolean(serviceId) && !!brandId,
    staleTime: 5 * 60_000,
  })
}
