/**
 * useActiveVertical — the industry vertical (laundry/salon/logistics) of the brand currently in
 * context, used to drive vertical-aware terminology in user-management (see lib/verticalTerms).
 *
 * Resolution order:
 *  1. the active brand a platform_admin picked in the switcher (already in the store, carries verticalKey);
 *  2. otherwise, for a brand-scoped admin whose JWT carries brand_id but who has no store brand, fetch
 *     that brand once to learn its vertical.
 * Falls back to undefined (callers treat that as the laundry default) until an answer is available, so
 * the console is never blocked on this and behaves exactly as before for laundry brands.
 */
import { useQuery } from '@tanstack/react-query'
import { useBrandStore } from '@/stores/brandStore'
import { usePermissions } from './usePermissions'
import { useEffectiveBrandId } from './useBrandContext'
import { getBrandById } from '@/api/tenancy'

const ONE_HOUR = 60 * 60 * 1000

export function useActiveVertical(): string | undefined {
  const fromStore = useBrandStore((s) => s.activeBrand?.verticalKey)
  const brandId = useEffectiveBrandId()
  const { hasPermission } = usePermissions()

  const { data } = useQuery({
    queryKey: ['brand-vertical', brandId],
    queryFn: () => getBrandById(brandId as string),
    // Only the fallback path (brand-scoped admin, no store brand) hits the API, and only when the
    // caller can actually read brands — otherwise we'd fire 403s. Never retry on failure: a denied
    // read just means we keep the laundry default.
    enabled: !fromStore && !!brandId && hasPermission('brands.read'),
    staleTime: ONE_HOUR,
    retry: false,
    select: (b) => b.verticalKey,
  })

  return fromStore ?? data
}
