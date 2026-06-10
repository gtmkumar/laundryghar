/**
 * Rider earnings + COD cash self-service API.
 *
 *   GET /api/v1/rider/payouts?days=7|30  → SingleResponse<RiderPayoutSummaryDto>
 *   GET /api/v1/rider/cash/summary       → SingleResponse<RiderCashSummaryDto>
 */
import axios from 'axios';
import { ApiError, logisticsClient, unwrapSingle } from '@/api/client';
import type {
  RiderCashSummaryDto,
  RiderPayoutSummaryDto,
  SingleResponse,
} from '@/types/api';

function toApiError(e: unknown, fallback: string): ApiError {
  if (axios.isAxiosError(e) && e.response?.data) {
    const env = e.response.data as SingleResponse<unknown>;
    return new ApiError(env.message?.responseMessage ?? fallback, { status: false });
  }
  if (e instanceof ApiError) return e;
  return new ApiError(fallback);
}

/** Fetch the rider's earnings breakdown for the last `days` calendar days (7 or 30). */
export async function fetchMyPayouts(days: 7 | 30): Promise<RiderPayoutSummaryDto> {
  try {
    const res = await logisticsClient.get<SingleResponse<RiderPayoutSummaryDto>>(
      `/rider/payouts`,
      { params: { days } },
    );
    return unwrapSingle(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not load earnings. Try again.');
  }
}

/** Fetch the rider's COD cash summary (outstanding + recent settlements). */
export async function fetchMyCashSummary(): Promise<RiderCashSummaryDto> {
  try {
    const res = await logisticsClient.get<SingleResponse<RiderCashSummaryDto>>(
      `/rider/cash/summary`,
    );
    return unwrapSingle(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not load cash summary. Try again.');
  }
}
