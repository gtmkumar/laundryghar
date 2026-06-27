/**
 * Tests for src/lib/fulfillmentTracking.ts — backend-driven order tracking (Phase 3).
 *
 * Pure logic only — no axios, no native modules. Verifies that an order's status projects onto
 * the backend-provided stage descriptors so the tracking UI never hardcodes a laundry ladder.
 */
import {
  fulfillmentModeForJobType,
  resolveOrderStages,
  isTerminalStatus,
  FULFILLMENT_MODE,
} from '../lib/fulfillmentTracking';
import type { FulfillmentConfigDto } from '../types/api';

const laundryConfig: FulfillmentConfigDto = {
  fulfillmentMode: 'process_deliver',
  initialStatus: 'placed',
  stages: [
    { status: 'placed', label: 'Placed', order: 0, lifecycleState: 'created' },
    { status: 'received', label: 'Received', order: 1, lifecycleState: 'active' },
    { status: 'ready', label: 'Ready', order: 2, lifecycleState: 'active' },
    { status: 'delivered', label: 'Delivered', order: 3, lifecycleState: 'completed' },
  ],
  terminalStatuses: ['delivered', 'cancelled', 'closed'],
  requiresStoreDrop: true,
  requiresPickup: true,
  requiresDelivery: true,
};

describe('fulfillmentModeForJobType', () => {
  it('maps parcel → point_to_point', () => {
    expect(fulfillmentModeForJobType('parcel')).toBe(FULFILLMENT_MODE.pointToPoint);
  });
  it('maps laundry / unknown / null → process_deliver', () => {
    expect(fulfillmentModeForJobType('laundry')).toBe(FULFILLMENT_MODE.processDeliver);
    expect(fulfillmentModeForJobType(undefined)).toBe(FULFILLMENT_MODE.processDeliver);
    expect(fulfillmentModeForJobType(null)).toBe(FULFILLMENT_MODE.processDeliver);
  });
});

describe('resolveOrderStages', () => {
  it('marks passed stages completed and the current one current', () => {
    const stages = resolveOrderStages(laundryConfig, 'ready');
    expect(stages.map((s) => [s.status, s.completed, s.current])).toEqual([
      ['placed', true, false],
      ['received', true, false],
      ['ready', false, true],
      ['delivered', false, false],
    ]);
  });

  it('preserves the backend labels (no client-side status strings)', () => {
    const stages = resolveOrderStages(laundryConfig, 'received');
    expect(stages.map((s) => s.label)).toEqual(['Placed', 'Received', 'Ready', 'Delivered']);
  });

  it('marks no stage current for an off-happy-path status (e.g. cancelled)', () => {
    const stages = resolveOrderStages(laundryConfig, 'cancelled');
    expect(stages.some((s) => s.current)).toBe(false);
    expect(stages.some((s) => s.completed)).toBe(false);
  });

  it('marks the first stage current at the initial status', () => {
    const stages = resolveOrderStages(laundryConfig, 'placed');
    expect(stages[0].current).toBe(true);
    expect(stages[0].completed).toBe(false);
  });
});

describe('isTerminalStatus', () => {
  it('reflects the backend terminal set', () => {
    expect(isTerminalStatus(laundryConfig, 'delivered')).toBe(true);
    expect(isTerminalStatus(laundryConfig, 'ready')).toBe(false);
  });
});
