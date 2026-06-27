/**
 * useFulfillmentConfig — backend-driven tracking config (multi-vertical Phase 3).
 *
 * Fetches the per-mode fulfilment config and exposes the resolved stage list for an order, so
 * tracking screens render the backend's stage descriptors instead of a hardcoded laundry ladder.
 * The config rarely changes, so it is cached aggressively.
 */
import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  getFulfillmentConfig,
  getFulfillmentConfigForMode,
} from '@/api/fulfillment';
import {
  fulfillmentModeForJobType,
  resolveOrderStages,
  type OrderStageView,
} from '@/lib/fulfillmentTracking';
import type { FulfillmentConfigDto } from '@/types/api';

export const fulfillmentConfigKeys = {
  all: ['fulfillment-config'] as const,
  mode: (mode: string) => ['fulfillment-config', mode] as const,
};

const ONE_HOUR = 60 * 60 * 1000;

/** All fulfilment modes' config (lets a client mount the right pack). */
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
 * The resolved, renderable tracking stages for an order — derived from the order's jobType + its
 * current status against the backend config. Returns an empty list until the config loads.
 */
export function useOrderStages(
  jobType: string | null | undefined,
  currentStatus: string | null | undefined,
): { stages: OrderStageView[]; config?: FulfillmentConfigDto; isLoading: boolean } {
  const mode = fulfillmentModeForJobType(jobType);
  const { data: config, isLoading } = useFulfillmentConfigForMode(mode);

  const stages = useMemo(
    () => (config && currentStatus ? resolveOrderStages(config, currentStatus) : []),
    [config, currentStatus],
  );

  return { stages, config, isLoading };
}
