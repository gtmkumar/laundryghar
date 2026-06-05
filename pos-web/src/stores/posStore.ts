/**
 * POS-specific store: tracks the active store selection for walk-in sessions.
 *
 * Store staff who have store_id in their JWT will have it pre-set here.
 * Store admins (or platform admins) can switch store context.
 */
import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'
import type { StoreDto } from '@/types/api'

interface PosState {
  activeStore: StoreDto | null
  setActiveStore: (store: StoreDto) => void
  clearStore: () => void
}

export const usePosStore = create<PosState>()(
  persist(
    (set) => ({
      activeStore: null,
      setActiveStore: (store) => set({ activeStore: store }),
      clearStore: () => set({ activeStore: null }),
    }),
    {
      name: 'lg-pos-store',
      storage: createJSONStorage(() => localStorage),
    },
  ),
)
