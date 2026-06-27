/**
 * Tests for src/lib/fulfillmentTracking.ts — backend-driven rider phases (Phase 3).
 *
 * Pure logic only — no axios, no native modules. Verifies that a task's fulfilment mode drives the
 * store-drop decision and that item summaries are vertical-neutral.
 */
import {
  fulfillmentModeForJobType,
  pickupRequiresStoreDrop,
  itemSummaryLabel,
  FULFILLMENT_MODE,
} from '../lib/fulfillmentTracking';
import type { FulfillmentConfigDto } from '../types/api';

const laundryConfig: FulfillmentConfigDto = {
  fulfillmentMode: 'process_deliver',
  initialStatus: 'placed',
  stages: [],
  terminalStatuses: ['delivered', 'cancelled', 'closed'],
  requiresStoreDrop: true,
  requiresPickup: true,
  requiresDelivery: true,
};

const parcelConfig: FulfillmentConfigDto = {
  ...laundryConfig,
  fulfillmentMode: 'point_to_point',
  requiresStoreDrop: false,
};

describe('fulfillmentModeForJobType', () => {
  it('maps parcel → point_to_point, else process_deliver', () => {
    expect(fulfillmentModeForJobType('parcel')).toBe(FULFILLMENT_MODE.pointToPoint);
    expect(fulfillmentModeForJobType('laundry')).toBe(FULFILLMENT_MODE.processDeliver);
    expect(fulfillmentModeForJobType(undefined)).toBe(FULFILLMENT_MODE.processDeliver);
  });
});

describe('pickupRequiresStoreDrop', () => {
  it('is true for laundry, false for point_to_point', () => {
    expect(pickupRequiresStoreDrop(laundryConfig)).toBe(true);
    expect(pickupRequiresStoreDrop(parcelConfig)).toBe(false);
  });
  it('defaults to true (laundry) when config has not loaded', () => {
    expect(pickupRequiresStoreDrop(undefined)).toBe(true);
  });
});

describe('itemSummaryLabel', () => {
  it('renders a neutral count + unit', () => {
    expect(itemSummaryLabel(3)).toBe('3 items');
    expect(itemSummaryLabel(5, 'garments')).toBe('5 garments');
    expect(itemSummaryLabel(2, 'parcels')).toBe('2 parcels');
  });
  it('singularises a count of one', () => {
    expect(itemSummaryLabel(1, 'garments')).toBe('1 garment');
    expect(itemSummaryLabel(1)).toBe('1 item');
  });
});
