import {
  term,
  itemNoun,
  bookingNoun,
  onsiteLocationNoun,
  countOfItems,
  type TerminologyPack,
} from '@/lib/terminology';

/**
 * The terminology pack is what stops a laundry word reaching a courier screen
 * (PLATFORM_STRATEGY.md §12). Two properties matter, and neither is the happy path:
 *   1. it never blanks — every read falls back, because the pack arrives asynchronously;
 *   2. it actually differs per vertical, or the whole mechanism is decoration.
 */
const laundry: TerminologyPack = {
  verticalKey: 'laundry',
  terms: {
    item: { singular: 'garment', plural: 'garments' },
    booking: { singular: 'order', plural: 'orders' },
    onsite_location: { singular: 'Warehouse', plural: 'Warehouses' },
  },
};

const logistics: TerminologyPack = {
  verticalKey: 'logistics',
  terms: {
    item: { singular: 'parcel', plural: 'parcels' },
    booking: { singular: 'shipment', plural: 'shipments' },
    onsite_location: { singular: 'Hub', plural: 'Hubs' },
  },
};

describe('terminology', () => {
  it('renders the vertical’s own words', () => {
    expect(itemNoun(laundry)).toBe('garment');
    expect(itemNoun(logistics)).toBe('parcel');
    expect(bookingNoun(logistics)).toBe('shipment');
    expect(onsiteLocationNoun(logistics)).toBe('Hub');
  });

  it('never blanks while the pack is still loading', () => {
    // The pack is fetched asynchronously; a screen must render a sensible word immediately.
    expect(itemNoun(undefined)).toBe('item');
    expect(bookingNoun(undefined)).toBe('booking');
    expect(onsiteLocationNoun(undefined)).toBe('Location');
  });

  it('falls back per-key, not all-or-nothing', () => {
    const partial: TerminologyPack = {
      verticalKey: 'salon',
      terms: { item: { singular: 'service', plural: 'services' } },
    };
    expect(itemNoun(partial)).toBe('service');   // present -> used
    expect(bookingNoun(partial)).toBe('booking'); // absent  -> neutral default
  });

  it('falls back when a term is present but empty', () => {
    const broken = { verticalKey: 'x', terms: { item: { singular: '', plural: '' } } };
    expect(itemNoun(broken)).toBe('item');
  });

  it('pluralises on the count', () => {
    expect(countOfItems(laundry, 1)).toBe('1 garment');
    expect(countOfItems(laundry, 3)).toBe('3 garments');
    expect(countOfItems(logistics, 2)).toBe('2 parcels');
    expect(countOfItems(undefined, 2)).toBe('2 items');
  });

  it('reads an arbitrary key through term()', () => {
    expect(term(laundry, 'item', 'x')).toBe('garment');
    expect(term(laundry, 'no_such_key', 'fallback')).toBe('fallback');
  });
});
