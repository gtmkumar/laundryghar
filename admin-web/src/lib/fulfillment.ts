/**
 * Backend-driven fulfilment helpers (multi-vertical Phase 3).
 *
 * Pure functions that turn a backend FulfillmentConfigDto into admin display decisions — chiefly a
 * vertical-correct status label — layered over the existing local status formatting. No React.
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

/**
 * The display label for a status, sourced from the backend config's stage descriptors when
 * available, otherwise the supplied `fallback` (the admin's existing local i18n label). This keeps
 * the rich local labelling as the safety net while letting the backend drive vertical-correct text.
 */
export function resolveStatusLabel(
  config: FulfillmentConfigDto | undefined,
  status: string,
  fallback: string,
): string {
  const stage = config?.stages.find((s) => s.status === status)
  return stage?.label ?? fallback
}
