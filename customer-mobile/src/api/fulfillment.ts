/**
 * Fulfilment-config API — backend-driven tracking (multi-vertical Phase 3).
 *
 * The backend exposes each fulfilment mode's stage descriptors + leg topology, built live from
 * its registered strategies, so the client renders the right tracking ladder per order WITHOUT a
 * hardcoded laundry status enum. A new vertical (e.g. salon) appears here automatically.
 */
import { ordersClient, unwrapList, unwrapSingle } from '@/api/client';
import type {
  FulfillmentConfigDto,
  ListResponse,
  SingleResponse,
} from '@/types/api';

/** GET /api/v1/fulfillment-config — config for every registered fulfilment mode. */
export async function getFulfillmentConfig(): Promise<FulfillmentConfigDto[]> {
  const res = await ordersClient.get<ListResponse<FulfillmentConfigDto>>(
    '/fulfillment-config/',
  );
  return unwrapList(res.data);
}

/** GET /api/v1/fulfillment-config/{mode} — config for a single fulfilment mode. */
export async function getFulfillmentConfigForMode(
  mode: string,
): Promise<FulfillmentConfigDto> {
  const res = await ordersClient.get<SingleResponse<FulfillmentConfigDto>>(
    `/fulfillment-config/${encodeURIComponent(mode)}`,
  );
  return unwrapSingle(res.data);
}
