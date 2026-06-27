/**
 * Backend-driven fulfilment helpers (multi-vertical Phase 3).
 *
 * Pure functions that turn a backend FulfillmentConfigDto into POS display decisions — chiefly a
 * vertical-correct status label — replacing the hardcoded laundry status formatting. No React.
 */
import type { FulfillmentConfigDto } from '@/types/api'

export const FULFILLMENT_MODE = {
  processDeliver: 'process_deliver',
  pointToPoint: 'point_to_point',
  appointment: 'appointment',
} as const

/**
 * Map an order's legacy jobType → fulfilment mode, mirroring the backend resolver's fallback
 * (parcel → point_to_point, else laundry's process_deliver).
 */
export function fulfillmentModeForJobType(jobType: string | null | undefined): string {
  return jobType === 'parcel'
    ? FULFILLMENT_MODE.pointToPoint
    : FULFILLMENT_MODE.processDeliver
}

/** The hardcoded fallback formatting the POS used before backend labels: "out_for_delivery" → "out for delivery". */
export function defaultStatusLabel(status: string): string {
  return status.replace(/_/g, ' ')
}

/**
 * The display label for a status, sourced from the backend config's stage descriptors when
 * available (e.g. "out_for_delivery" → "Out For Delivery"), falling back to the local formatting so
 * behaviour is unchanged until the config loads or for statuses off the happy path.
 */
export function statusLabel(
  config: FulfillmentConfigDto | undefined,
  status: string,
): string {
  const stage = config?.stages.find((s) => s.status === status)
  return stage?.label ?? defaultStatusLabel(status)
}
