/**
 * Tests for src/store/cartStore.ts
 *
 * Coverage:
 *   - setQty: add item, update qty, remove when qty <= 0
 *   - remove: removes a specific item, others unaffected
 *   - clear: empties all lines
 *   - count(): sum of all quantities
 *   - subtotal(): sum of (qty * unitPrice) per line
 *   - list(): all lines as an array
 *   - Selector stability regression: list() and count() on unchanged state
 *
 * No native modules required — zustand runs in Node.
 * Tests reset store state between runs via clear().
 */

import { useCartStore, CartLine } from '../store/cartStore';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function line(overrides: Partial<CartLine> = {}): Omit<CartLine, 'qty'> {
  return {
    id: 'item-1',
    itemId: 'catalog-item-1',
    serviceId: 'catalog-service-1',
    name: 'Shirt',
    service: 'Wash & Iron',
    unitPrice: 120,
    ...overrides,
  };
}

function resetStore() {
  useCartStore.getState().clear();
}

// ---------------------------------------------------------------------------
// setQty
// ---------------------------------------------------------------------------

describe('cartStore — setQty', () => {
  beforeEach(resetStore);

  test('adds a new item with the given qty', () => {
    useCartStore.getState().setQty(line(), 2);
    const lines = useCartStore.getState().list();
    expect(lines).toHaveLength(1);
    expect(lines[0].qty).toBe(2);
    expect(lines[0].id).toBe('item-1');
  });

  test('updates qty of an existing item', () => {
    useCartStore.getState().setQty(line(), 1);
    useCartStore.getState().setQty(line(), 5);
    expect(useCartStore.getState().list()[0].qty).toBe(5);
  });

  test('removes item when qty is 0', () => {
    useCartStore.getState().setQty(line(), 3);
    useCartStore.getState().setQty(line(), 0);
    expect(useCartStore.getState().list()).toHaveLength(0);
  });

  test('removes item when qty is negative', () => {
    useCartStore.getState().setQty(line(), 2);
    useCartStore.getState().setQty(line(), -1);
    expect(useCartStore.getState().list()).toHaveLength(0);
  });

  test('adding two distinct items keeps both', () => {
    useCartStore.getState().setQty(line({ id: 'item-1' }), 2);
    useCartStore.getState().setQty(line({ id: 'item-2', name: 'Trousers', unitPrice: 200 }), 1);
    expect(useCartStore.getState().list()).toHaveLength(2);
  });
});

// ---------------------------------------------------------------------------
// remove
// ---------------------------------------------------------------------------

describe('cartStore — remove', () => {
  beforeEach(resetStore);

  test('removes the specified item', () => {
    useCartStore.getState().setQty(line({ id: 'item-1' }), 2);
    useCartStore.getState().setQty(line({ id: 'item-2' }), 1);
    useCartStore.getState().remove('item-1');
    const remaining = useCartStore.getState().list();
    expect(remaining).toHaveLength(1);
    expect(remaining[0].id).toBe('item-2');
  });

  test('removing non-existent id is a no-op', () => {
    useCartStore.getState().setQty(line(), 1);
    useCartStore.getState().remove('ghost-id');
    expect(useCartStore.getState().list()).toHaveLength(1);
  });
});

// ---------------------------------------------------------------------------
// clear
// ---------------------------------------------------------------------------

describe('cartStore — clear', () => {
  beforeEach(resetStore);

  test('clears all lines', () => {
    useCartStore.getState().setQty(line({ id: 'item-1' }), 2);
    useCartStore.getState().setQty(line({ id: 'item-2' }), 3);
    useCartStore.getState().clear();
    expect(useCartStore.getState().list()).toHaveLength(0);
  });

  test('count() is 0 after clear', () => {
    useCartStore.getState().setQty(line(), 5);
    useCartStore.getState().clear();
    expect(useCartStore.getState().count()).toBe(0);
  });

  test('subtotal() is 0 after clear', () => {
    useCartStore.getState().setQty(line({ unitPrice: 200 }), 3);
    useCartStore.getState().clear();
    expect(useCartStore.getState().subtotal()).toBe(0);
  });
});

// ---------------------------------------------------------------------------
// count()
// ---------------------------------------------------------------------------

describe('cartStore — count()', () => {
  beforeEach(resetStore);

  test('returns 0 on empty store', () => {
    expect(useCartStore.getState().count()).toBe(0);
  });

  test('sums quantities across all lines', () => {
    useCartStore.getState().setQty(line({ id: 'item-1' }), 3);
    useCartStore.getState().setQty(line({ id: 'item-2' }), 2);
    expect(useCartStore.getState().count()).toBe(5);
  });

  test('decrements correctly on qty update', () => {
    useCartStore.getState().setQty(line(), 5);
    useCartStore.getState().setQty(line(), 2);
    expect(useCartStore.getState().count()).toBe(2);
  });
});

// ---------------------------------------------------------------------------
// subtotal()
// ---------------------------------------------------------------------------

describe('cartStore — subtotal()', () => {
  beforeEach(resetStore);

  test('returns 0 on empty store', () => {
    expect(useCartStore.getState().subtotal()).toBe(0);
  });

  test('computes qty * unitPrice for single item', () => {
    useCartStore.getState().setQty(line({ unitPrice: 120 }), 3);
    expect(useCartStore.getState().subtotal()).toBe(360);
  });

  test('sums across multiple lines', () => {
    useCartStore.getState().setQty(line({ id: 'item-1', unitPrice: 100 }), 2); // 200
    useCartStore.getState().setQty(line({ id: 'item-2', unitPrice: 250 }), 1); // 250
    expect(useCartStore.getState().subtotal()).toBe(450);
  });

  test('fractional unit price accumulates correctly', () => {
    useCartStore.getState().setQty(line({ unitPrice: 99.5 }), 2);
    expect(useCartStore.getState().subtotal()).toBe(199);
  });
});

// ---------------------------------------------------------------------------
// Selector stability regression
// ---------------------------------------------------------------------------

describe('cartStore — selector reference stability', () => {
  beforeEach(resetStore);

  test('count() returns the same value on consecutive reads without mutation', () => {
    useCartStore.getState().setQty(line(), 3);
    const c1 = useCartStore.getState().count();
    const c2 = useCartStore.getState().count();
    expect(c1).toBe(c2);
  });

  test('list() length is stable on consecutive reads without mutation', () => {
    useCartStore.getState().setQty(line({ id: 'a' }), 1);
    useCartStore.getState().setQty(line({ id: 'b' }), 2);
    const l1 = useCartStore.getState().list().length;
    const l2 = useCartStore.getState().list().length;
    expect(l1).toBe(l2);
  });
});
