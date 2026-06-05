import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'
import type { BrandDto } from '@/types/api'

/**
 * Tracks the currently active brand for platform admins.
 * The selected brandId is sent as the X-Brand-Id header on all brand-scoped requests.
 * Brand/store-scoped staff (store_staff, store_admin) have their brand_id embedded
 * in the JWT — the interceptor reads brand_id from the JWT claims first, then falls
 * back to this store.
 */
interface BrandState {
  activeBrandId: string | null
  activeBrand: BrandDto | null
  setActiveBrand: (brand: BrandDto) => void
  setActiveBrandId: (id: string) => void
  clearBrand: () => void
}

export const useBrandStore = create<BrandState>()(
  persist(
    (set) => ({
      activeBrandId: null,
      activeBrand: null,

      setActiveBrand: (brand) =>
        set({ activeBrand: brand, activeBrandId: brand.id }),

      setActiveBrandId: (id) =>
        set({ activeBrandId: id }),

      clearBrand: () =>
        set({ activeBrand: null, activeBrandId: null }),
    }),
    {
      name: 'lg-pos-brand',
      storage: createJSONStorage(() => localStorage),
    },
  ),
)
