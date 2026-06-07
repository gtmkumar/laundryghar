import { useEffect } from 'react'
import { useAuthStore } from '@/stores/authStore'
import { useBrandStore } from '@/stores/brandStore'
import { getBrands } from '@/api/tenancy'

/**
 * The brand id that brand-scoped requests will actually resolve to:
 *  - brand-scoped users (store/warehouse/franchise staff) carry it in their JWT;
 *  - platform_admin has none, so it relies on the active brand selection.
 * Mirrors the resolution order used by the axios X-Brand-Id interceptor.
 */
export function useEffectiveBrandId(): string | null {
  const jwtBrand = useAuthStore((s) => s.user?.brand_id)
  const activeBrandId = useBrandStore((s) => s.activeBrandId)
  return jwtBrand ?? activeBrandId ?? null
}

/**
 * Auto-selects the first active brand for a platform_admin that has none yet,
 * so brand-scoped endpoints receive X-Brand-Id. No-op for brand-scoped users.
 * Safe to call from any authenticated screen — including full-screen routes
 * that render outside <AppShell> (e.g. the warehouse board).
 */
export function useEnsureBrandContext(): void {
  const userType = useAuthStore((s) => s.user?.user_type)
  const jwtBrand = useAuthStore((s) => s.user?.brand_id)
  const activeBrandId = useBrandStore((s) => s.activeBrandId)
  const setActiveBrand = useBrandStore((s) => s.setActiveBrand)

  useEffect(() => {
    if (userType !== 'platform_admin' || jwtBrand || activeBrandId) return
    void getBrands({ page: 1, pageSize: 50, status: 'active' })
      .then((result) => {
        const first = result.list[0]
        if (first) setActiveBrand(first)
      })
      .catch(() => {
        // Silently fall through — user can pick a brand via the switcher.
      })
  }, [userType, jwtBrand, activeBrandId, setActiveBrand])
}
