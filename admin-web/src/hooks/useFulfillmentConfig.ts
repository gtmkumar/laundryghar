/**
 * useFulfillmentConfig — backend-driven status labels (multi-vertical Phase 3).
 *
 * Fetches the per-mode fulfilment config and exposes a status labeler for an order, so the admin
 * labels statuses per the order's vertical. The local label (orderStatus.statusLabel) remains the
 * fallback, so behaviour is unchanged until the config loads. Cached aggressively.
 */
import { useCallback } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  getFulfillmentConfig,
  getFulfillmentConfigForMode,
} from '@/api/fulfillment'
import { fulfillmentModeForJobType, resolveStatusLabel } from '@/lib/fulfillment'

export const fulfillmentConfigKeys = {
  all: ['fulfillment-config'] as const,
  mode: (mode: string) => ['fulfillment-config', mode] as const,
}

const ONE_HOUR = 60 * 60 * 1000

/** All fulfilment modes' config. */
export function useFulfillmentConfig() {
  return useQuery({
    queryKey: fulfillmentConfigKeys.all,
    queryFn: getFulfillmentConfig,
    staleTime: ONE_HOUR,
  })
}

/** Config for a single fulfilment mode. */
export function useFulfillmentConfigForMode(mode: string | undefined) {
  return useQuery({
    queryKey: fulfillmentConfigKeys.mode(mode ?? ''),
    queryFn: () => getFulfillmentConfigForMode(mode as string),
    enabled: !!mode,
    staleTime: ONE_HOUR,
  })
}

/**
 * Returns a `(status, fallback) => label` function for an order, sourced from the backend config
 * for the order's fulfilment mode, falling back to the admin's local label.
 */
export function useStatusLabeler(
  jobType: string | null | undefined,
): (status: string, fallback: string) => string {
  const mode = fulfillmentModeForJobType(jobType)
  const { data: config } = useFulfillmentConfigForMode(mode)
  return useCallback(
    (status: string, fallback: string) => resolveStatusLabel(config, status, fallback),
    [config],
  )
}
