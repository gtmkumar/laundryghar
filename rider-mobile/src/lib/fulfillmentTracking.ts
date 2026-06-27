/**
 * Backend-driven rider phases (multi-vertical Phase 3).
 *
 * Pure helpers (no React, no native) that turn a backend FulfillmentConfigDto into the rider
 * flow's mode-specific decisions — chiefly whether a pickup has a store-drop leg — and a
 * vertical-neutral item-summary label, replacing the hardcoded laundry assumptions. Unit-tested.
 */
import type { FulfillmentConfigDto } from '@/types/api';

export const FULFILLMENT_MODE = {
  processDeliver: 'process_deliver',
  pointToPoint: 'point_to_point',
  appointment: 'appointment',
} as const;

/**
 * Map an order's legacy jobType → fulfilment mode, mirroring the backend resolver's fallback
 * (parcel → point_to_point, else laundry's process_deliver).
 */
export function fulfillmentModeForJobType(jobType: string | null | undefined): string {
  return jobType === 'parcel'
    ? FULFILLMENT_MODE.pointToPoint
    : FULFILLMENT_MODE.processDeliver;
}

/**
 * Whether a pickup in this mode involves a store/warehouse drop after collection. Laundry
 * (process_deliver) collects then drops at the store; a point_to_point parcel goes straight to
 * delivery. Falls back to `true` (the laundry default) when the config has not loaded yet, so the
 * rider flow is unchanged until the backend answer arrives.
 */
export function pickupRequiresStoreDrop(config: FulfillmentConfigDto | undefined): boolean {
  return config?.requiresStoreDrop ?? true;
}

/**
 * A vertical-neutral item-summary label for a task (the generic successor to "N garments"). The
 * unit defaults to "items"; a vertical may pass its own ("parcels", "services").
 */
export function itemSummaryLabel(count: number, unit = 'items'): string {
  const singular = count === 1 && unit.endsWith('s') ? unit.slice(0, -1) : unit;
  return `${count} ${singular}`;
}
