/**
 * Cart store — local, session-scoped selection of laundry items built on the
 * "What needs washing?" screen. There is no server-side cart yet; this drives
 * the booking flow (items -> pickup -> pay -> confirm) and the estimate totals.
 */
import { create } from 'zustand';

export interface CartLine {
  /** Stable id — the price-list ROW id (or a synthetic id for demo items). Used only as the local cart key. */
  id: string;
  /** Catalog item id from the price-list entry — null for demo/fallback items. Sent to the API. */
  itemId: string | null;
  /** Catalog service id from the price-list entry — null for demo/fallback items. Sent to the API. */
  serviceId: string | null;
  /** Full display label (backend displayLabel, falling back to itemName + serviceName). */
  name: string;
  service: string;
  unitPrice: number;
  qty: number;
}

interface CartState {
  lines: Record<string, CartLine>;
  setQty: (line: Omit<CartLine, 'qty'>, qty: number) => void;
  remove: (id: string) => void;
  clear: () => void;
  // selectors
  list: () => CartLine[];
  count: () => number;
  subtotal: () => number;
}

export const useCartStore = create<CartState>()((set, get) => ({
  lines: {},

  setQty: (line, qty) =>
    set((state) => {
      const next = { ...state.lines };
      if (qty <= 0) {
        delete next[line.id];
      } else {
        next[line.id] = { ...line, qty };
      }
      return { lines: next };
    }),

  remove: (id) =>
    set((state) => {
      const next = { ...state.lines };
      delete next[id];
      return { lines: next };
    }),

  clear: () => set({ lines: {} }),

  list: () => Object.values(get().lines),
  count: () => Object.values(get().lines).reduce((n, l) => n + l.qty, 0),
  subtotal: () => Object.values(get().lines).reduce((sum, l) => sum + l.qty * l.unitPrice, 0),
}));
