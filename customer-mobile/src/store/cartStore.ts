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
  /**
   * Pricing mode from the catalog (GH #22). 'value_slab' items are priced from a
   * customer-declared value at order time — their `unitPrice` is a placeholder and
   * is excluded from {@link CartState.subtotal}. Undefined ⇒ standard pricing.
   */
  pricingMode?: 'standard' | 'value_slab';
  /** Customer-declared garment value for a value_slab line (GH #22). Undefined until set. */
  declaredValue?: number;
}

interface CartState {
  lines: Record<string, CartLine>;
  setQty: (line: Omit<CartLine, 'qty'>, qty: number) => void;
  /** Set/replace the declared garment value for an existing value_slab line (GH #22). */
  setDeclaredValue: (id: string, declaredValue: number) => void;
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
        // Merge over any existing line so a plain quantity change (the incoming
        // `line` meta carries no declaredValue key) preserves a previously
        // declared garment value; an explicit declaredValue in `line` still wins.
        next[line.id] = { ...state.lines[line.id], ...line, qty };
      }
      return { lines: next };
    }),

  setDeclaredValue: (id, declaredValue) =>
    set((state) => {
      const existing = state.lines[id];
      if (!existing) return state;
      return { lines: { ...state.lines, [id]: { ...existing, declaredValue } } };
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
  // value_slab lines carry a placeholder unitPrice (priced from the declared value
  // server-side), so they are excluded from the money subtotal — see GH #22.
  subtotal: () =>
    Object.values(get().lines).reduce(
      (sum, l) => (l.pricingMode === 'value_slab' ? sum : sum + l.qty * l.unitPrice),
      0,
    ),
}));
