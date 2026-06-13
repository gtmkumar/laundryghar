/**
 * Rider earnings + COD cash + withdrawable-balance self-service API.
 *
 *   GET  /api/v1/rider/payouts?days=7|30   → SingleResponse<RiderPayoutSummaryDto>
 *   GET  /api/v1/rider/cash/summary        → SingleResponse<RiderCashSummaryDto>
 *   GET  /api/v1/rider/balance             → SingleResponse<RiderBalanceDto>
 *   POST /api/v1/rider/payout-requests     → SingleResponse<RiderPayoutRequestDto>
 *   GET  /api/v1/rider/payout-requests     → ListResponse<RiderPayoutRequestDto>
 *   GET  /api/v1/rider/incentives?days=30  → ListResponse<RiderIncentiveDto>
 */
import axios from 'axios';
import { ApiError, logisticsClient, unwrapList, unwrapSingle } from '@/api/client';
import type {
  ListResponse,
  RiderBalanceDto,
  RiderCashSummaryDto,
  RiderIncentiveDto,
  RiderPayoutRequestDto,
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

// ---------------------------------------------------------------------------
// Withdrawable balance, withdrawal (payout) requests + incentives
// ---------------------------------------------------------------------------

/** Fetch the rider's withdrawable-balance breakdown. */
export async function fetchMyBalance(): Promise<RiderBalanceDto> {
  try {
    const res = await logisticsClient.get<SingleResponse<RiderBalanceDto>>(
      `/rider/balance`,
    );
    return unwrapSingle(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not load your balance. Try again.');
  }
}

/** Fetch the rider's withdrawal-request history (newest first). */
export async function fetchMyPayoutRequests(): Promise<RiderPayoutRequestDto[]> {
  try {
    const res = await logisticsClient.get<ListResponse<RiderPayoutRequestDto>>(
      `/rider/payout-requests`,
    );
    return unwrapList(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not load your withdrawals. Try again.');
  }
}

/**
 * Request a withdrawal of `amount` (₹) from the available balance.
 * The backend rejects amounts above the available balance with HTTP 422 —
 * that surfaces here as an ApiError whose message is the server's explanation
 * (e.g. "Amount exceeds available balance"); we fall back to a friendly default.
 */
export async function requestPayout(amount: number): Promise<RiderPayoutRequestDto> {
  try {
    const res = await logisticsClient.post<SingleResponse<RiderPayoutRequestDto>>(
      `/rider/payout-requests`,
      { amount },
    );
    return unwrapSingle(res.data);
  } catch (e) {
    if (axios.isAxiosError(e) && e.response?.status === 422) {
      const env = e.response.data as SingleResponse<unknown> | undefined;
      throw new ApiError(
        env?.message?.responseMessage ?? 'Amount exceeds your available balance.',
        { status: false, errorCode: 422 },
      );
    }
    throw toApiError(e, 'Could not submit your withdrawal request. Try again.');
  }
}

/** Fetch the rider's incentive/bonus awards over the last `days` days. */
export async function fetchMyIncentives(days: number): Promise<RiderIncentiveDto[]> {
  try {
    const res = await logisticsClient.get<ListResponse<RiderIncentiveDto>>(
      `/rider/incentives`,
      { params: { days } },
    );
    return unwrapList(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not load incentives. Try again.');
  }
}
