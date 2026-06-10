/**
 * useEarnings — React Query wrapper for GET /api/v1/rider/payouts?days=7|30
 * and GET /api/v1/rider/cash/summary.
 */
import { useQuery } from '@tanstack/react-query';
import { fetchMyPayouts, fetchMyCashSummary } from '@/api/earnings';
import { useAuthStore } from '@/store/authStore';

export const earningsKeys = {
  payouts: (days: 7 | 30) => ['rider', 'payouts', days] as const,
  cash:    ()              => ['rider', 'cash', 'summary'] as const,
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
