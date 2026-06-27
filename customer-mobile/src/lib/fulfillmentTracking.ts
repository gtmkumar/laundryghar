/**
 * Backend-driven order tracking (multi-vertical Phase 3).
 *
 * Turns a backend FulfillmentConfigDto + an order's current status into a renderable stage list —
 * replacing the hardcoded laundry status ladder so the same screen serves any vertical. Pure
 * functions (no React, no native modules) so they are trivially unit-tested.
 */
import type { FulfillmentConfigDto } from '@/types/api';

/** Legacy job kinds the customer app knows about. */
export const FULFILLMENT_MODE = {
  processDeliver: 'process_deliver',
  pointToPoint: 'point_to_point',
  appointment: 'appointment',
} as const;

/**
 * Map an order's legacy jobType → fulfilment mode, mirroring the backend resolver's fallback
 * (parcel → point_to_point, else laundry's process_deliver). Used until OrderDto carries the
 * mode directly.
 */
export function fulfillmentModeForJobType(jobType: string | null | undefined): string {
  return jobType === 'parcel'
    ? FULFILLMENT_MODE.pointToPoint
    : FULFILLMENT_MODE.processDeliver;
}

export interface OrderStageView {
  status: string;
  label: string;
  order: number;
  /** A stage strictly before the current status — already passed. */
  completed: boolean;
  /** The order's current stage on the happy path. */
  current: boolean;
}

/**
 * Project the order's current status onto the mode's happy-path stages. A status that is off the
 * happy path (e.g. a terminal `cancelled`) marks no stage current and none completed — callers can
 * render such states separately.
 */
export function resolveOrderStages(
  config: FulfillmentConfigDto,
  currentStatus: string,
): OrderStageView[] {
  const currentIndex = config.stages.findIndex((s) => s.status === currentStatus);
  return config.stages.map((s, i) => ({
    status: s.status,
    label: s.label,
    order: s.order,
    completed: currentIndex >= 0 && i < currentIndex,
    current: i === currentIndex,
  }));
}

/** True if the status is a terminal (no-further-transition) status for the mode. */
export function isTerminalStatus(config: FulfillmentConfigDto, status: string): boolean {
  return config.terminalStatuses.includes(status);
}
