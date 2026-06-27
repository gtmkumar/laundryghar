/**
 * useFulfillmentConfig — backend-driven phase config (multi-vertical Phase 3).
 *
 * Fetches the per-mode fulfilment config and exposes the rider-relevant decision (does a pickup
 * have a store-drop leg) for a task, so the task flow stops assuming the laundry round-trip for
 * every order. The config rarely changes, so it is cached aggressively.
 */
import { useQuery } from '@tanstack/react-query';
import {
  getFulfillmentConfig,
  getFulfillmentConfigForMode,
} from '@/api/fulfillment';
import {
  fulfillmentModeForJobType,
  pickupRequiresStoreDrop,
} from '@/lib/fulfillmentTracking';

export const fulfillmentConfigKeys = {
  all: ['fulfillment-config'] as const,
  mode: (mode: string) => ['fulfillment-config', mode] as const,
};

const ONE_HOUR = 60 * 60 * 1000;

/** All fulfilment modes' config. */
export function useFulfillmentConfig() {
  return useQuery({
    queryKey: fulfillmentConfigKeys.all,
    queryFn: getFulfillmentConfig,
    staleTime: ONE_HOUR,
  });
}

/** Config for a single fulfilment mode. */
export function useFulfillmentConfigForMode(mode: string | undefined) {
  return useQuery({
    queryKey: fulfillmentConfigKeys.mode(mode ?? ''),
    queryFn: () => getFulfillmentConfigForMode(mode as string),
    enabled: !!mode,
    staleTime: ONE_HOUR,
  });
}

/**
 * Whether a pickup task has a store-drop step, derived from the task's jobType against the backend
 * config. Defaults to true (laundry) until the config loads, so the flow is unchanged meanwhile.
 */
export function useTaskRequiresStoreDrop(jobType: string | null | undefined): boolean {
  const mode = fulfillmentModeForJobType(jobType);
  const { data: config } = useFulfillmentConfigForMode(mode);
  return pickupRequiresStoreDrop(config);
}
