/**
 * Fulfilment-config API — backend-driven status labels (multi-vertical Phase 3).
 *
 * The operations host exposes each fulfilment mode's stage descriptors, built live from its
 * registered strategies, so the admin labels an order's status per its vertical instead of a
 * hardcoded laundry ladder. A new vertical (e.g. salon) appears here automatically.
 */
import { ordersClient, unwrap } from './client'
import type { ApiResponse, FulfillmentConfigDto } from '@/types/api'

/** GET /api/v1/fulfillment-config — config for every registered fulfilment mode. */
export async function getFulfillmentConfig(): Promise<FulfillmentConfigDto[]> {
  const { data } = await ordersClient.get<ApiResponse<FulfillmentConfigDto[]>>(
    '/api/v1/fulfillment-config/',
  )
  return unwrap(data)
}

/** GET /api/v1/fulfillment-config/{mode} — config for a single fulfilment mode. */
export async function getFulfillmentConfigForMode(
  mode: string,
): Promise<FulfillmentConfigDto> {
  const { data } = await ordersClient.get<ApiResponse<FulfillmentConfigDto>>(
    `/api/v1/fulfillment-config/${encodeURIComponent(mode)}`,
  )
  return unwrap(data)
}
