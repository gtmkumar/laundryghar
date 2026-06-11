/**
 * In-progress walk-in order (cart) store — survives reloads.
 *
 * POS-1: the basket, selected customer, express flag and coupon used to live in
 * NewOrderPage component state and were destroyed on any reload (the single
 * biggest POS reliability risk). They now live here with persist middleware so
 * an accidental refresh / tab crash / device sleep doesn't lose a half-built
 * order at the counter.
 *
 * Scoping: the persisted cart is keyed per active store. A POS device that
 * switches store context should not resurrect another store's basket, so the
 * store id is captured alongside the cart and the selectors treat a cart whose
 * `storeId` no longer matches the active store as empty.
 */
import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'
import type { AdminCustomerDto } from '@/types/api'

export interface CartLine {
  itemId: string
  itemName: string
  serviceId: string
  serviceName: string
  /** Pieces (integer) for per-item services, kg (decimal) for per_kg services. */
  quantity: number
  isWeight: boolean
}

interface CartState {
  /** Store the cart was built against; null until the first mutation. */
  storeId: string | null
  customer: AdminCustomerDto | null
  isExpress: boolean
  coupon: string
  lines: CartLine[]

  setStoreId: (storeId: string | null) => void
  setCustomer: (customer: AdminCustomerDto | null) => void
  setIsExpress: (isExpress: boolean) => void
  setCoupon: (coupon: string) => void
  setLines: (updater: CartLine[] | ((prev: CartLine[]) => CartLine[])) => void
  /** Wipe the basket after a successful submit (or an explicit "discard"). */
  clearCart: () => void
}

const EMPTY = {
  customer: null,
  isExpress: false,
  coupon: '',
  lines: [] as CartLine[],
}

export const useCartStore = create<CartState>()(
  persist(
    (set) => ({
      storeId: null,
      ...EMPTY,

      setStoreId: (storeId) => set({ storeId }),
      setCustomer: (customer) => set({ customer }),
      setIsExpress: (isExpress) => set({ isExpress }),
      setCoupon: (coupon) => set({ coupon }),
      setLines: (updater) =>
        set((s) => ({
          lines: typeof updater === 'function' ? updater(s.lines) : updater,
        })),
      clearCart: () => set({ ...EMPTY }),
    }),
    {
      name: 'lg-pos-cart',
      storage: createJSONStorage(() => localStorage),
    },
  ),
)

/** True when the persisted cart belongs to a different store than the active one. */
export function isCartForeign(cartStoreId: string | null, activeStoreId: string | null): boolean {
  return cartStoreId !== null && activeStoreId !== null && cartStoreId !== activeStoreId
}
