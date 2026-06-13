/**
 * useEarnings — React Query wrappers for the rider self-service earnings,
 * cash, withdrawable-balance, payout-request and incentive endpoints.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  fetchMyBalance,
  fetchMyCashSummary,
  fetchMyIncentives,
  fetchMyPayoutRequests,
  fetchMyPayouts,
  requestPayout,
} from '@/api/earnings';
import { useAuthStore } from '@/store/authStore';
import type { RiderPayoutRequestDto } from '@/types/api';

export const earningsKeys = {
  payouts:        (days: 7 | 30) => ['rider', 'payouts', days] as const,
  cash:           ()             => ['rider', 'cash', 'summary'] as const,
  balance:        ()             => ['rider', 'balance'] as const,
  payoutRequests: ()             => ['rider', 'payout-requests'] as const,
  incentives:     (days: number) => ['rider', 'incentives', days] as const,
};

export function useMyPayouts(days: 7 | 30) {
  const accessToken = useAuthStore((s) => s.accessToken);
  return useQuery({
    queryKey: earningsKeys.payouts(days),
    queryFn:  () => fetchMyPayouts(days),
    enabled:  !!accessToken,
    staleTime: 30_000,
  });
}

export function useMyCashSummary() {
  const accessToken = useAuthStore((s) => s.accessToken);
  return useQuery({
    queryKey: earningsKeys.cash(),
    queryFn:  fetchMyCashSummary,
    enabled:  !!accessToken,
    staleTime: 30_000,
  });
}

/** Withdrawable-balance breakdown (GET /rider/balance). */
export function useBalance() {
  const accessToken = useAuthStore((s) => s.accessToken);
  return useQuery({
    queryKey: earningsKeys.balance(),
    queryFn:  fetchMyBalance,
    enabled:  !!accessToken,
    staleTime: 15_000,
  });
}

/** Withdrawal-request history (GET /rider/payout-requests). */
export function usePayoutRequests() {
  const accessToken = useAuthStore((s) => s.accessToken);
  return useQuery({
    queryKey: earningsKeys.payoutRequests(),
    queryFn:  fetchMyPayoutRequests,
    enabled:  !!accessToken,
    staleTime: 15_000,
  });
}

/** Incentive/bonus awards over the last `days` days (GET /rider/incentives). */
export function useIncentives(days = 30) {
  const accessToken = useAuthStore((s) => s.accessToken);
  return useQuery({
    queryKey: earningsKeys.incentives(days),
    queryFn:  () => fetchMyIncentives(days),
    enabled:  !!accessToken,
    staleTime: 30_000,
  });
}

/**
 * Submit a withdrawal request (POST /rider/payout-requests). On success,
 * invalidates the balance + request-history queries so the screen reflects the
 * new pending request and the reduced available balance immediately.
 */
export function useRequestPayout() {
  const queryClient = useQueryClient();
  return useMutation<RiderPayoutRequestDto, Error, number>({
    mutationFn: (amount: number) => requestPayout(amount),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: earningsKeys.balance() });
      void queryClient.invalidateQueries({ queryKey: earningsKeys.payoutRequests() });
    },
  });
}
