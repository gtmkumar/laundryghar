/**
 * Fulfilment-config API — backend-driven phases (multi-vertical Phase 3).
 *
 * The operations host (served via logisticsClient) exposes each fulfilment mode's stage
 * descriptors + leg topology, built live from its registered strategies. The rider app uses this
 * to drive mode-specific phases (e.g. whether a pickup has a store-drop leg) instead of assuming
 * the laundry flow for every order.
 */
import { logisticsClient, unwrapList, unwrapSingle } from '@/api/client';
import type {
  FulfillmentConfigDto,
  ListResponse,
  SingleResponse,
} from '@/types/api';

/** GET /api/v1/fulfillment-config — config for every registered fulfilment mode. */
export async function getFulfillmentConfig(): Promise<FulfillmentConfigDto[]> {
  const res = await logisticsClient.get<ListResponse<FulfillmentConfigDto>>(
    '/fulfillment-config/',
  );
  return unwrapList(res.data);
}

/** GET /api/v1/fulfillment-config/{mode} — config for a single fulfilment mode. */
export async function getFulfillmentConfigForMode(
  mode: string,
): Promise<FulfillmentConfigDto> {
  const res = await logisticsClient.get<SingleResponse<FulfillmentConfigDto>>(
    `/fulfillment-config/${encodeURIComponent(mode)}`,
  );
  return unwrapSingle(res.data);
}
