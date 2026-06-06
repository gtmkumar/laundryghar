import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getAnalyticsDashboard,
  getDailyStoreRevenue,
  getMonthlyFranchiseRevenue,
  getWarehouseThroughput,
  getCustomerLtv,
  getRiderPerformance,
  refreshAnalytics,
} from '@/api/analytics'
import type { AnalyticsListParams } from '@/types/api'

// ── Query key factory ─────────────────────────────────────────────────────────

export const analyticsKeys = {
  dashboard: () => ['analytics', 'dashboard'] as const,
  dailyStoreRevenue: (params?: object) => ['analytics', 'dailyStoreRevenue', params] as const,
  monthlyFranchiseRevenue: (params?: object) =>
    ['analytics', 'monthlyFranchiseRevenue', params] as const,
  warehouseThroughput: (params?: object) => ['analytics', 'warehouseThroughput', params] as const,
  customerLtv: (params?: object) => ['analytics', 'customerLtv', params] as const,
  riderPerformance: (params?: object) => ['analytics', 'riderPerformance', params] as const,
}

// ── Dashboard ─────────────────────────────────────────────────────────────────

export function useAnalyticsDashboard() {
  return useQuery({
    queryKey: analyticsKeys.dashboard(),
    queryFn: getAnalyticsDashboard,
  })
}

// ── Daily Store Revenue ───────────────────────────────────────────────────────

export function useDailyStoreRevenue(
  params: Pick<AnalyticsListParams, 'storeId' | 'from' | 'to'> = {},
) {
  return useQuery({
    queryKey: analyticsKeys.dailyStoreRevenue(params),
    queryFn: () => getDailyStoreRevenue(params),
  })
}

// ── Monthly Franchise Revenue ─────────────────────────────────────────────────

export function useMonthlyFranchiseRevenue(
  params: Pick<AnalyticsListParams, 'franchiseId' | 'year'> = {},
) {
  return useQuery({
    queryKey: analyticsKeys.monthlyFranchiseRevenue(params),
    queryFn: () => getMonthlyFranchiseRevenue(params),
  })
}

// ── Warehouse Throughput ──────────────────────────────────────────────────────

export function useWarehouseThroughput(
  params: Pick<AnalyticsListParams, 'warehouseId' | 'from' | 'to'> = {},
) {
  return useQuery({
    queryKey: analyticsKeys.warehouseThroughput(params),
    queryFn: () => getWarehouseThroughput(params),
  })
}

// ── Customer LTV ──────────────────────────────────────────────────────────────

export function useCustomerLtv(params: Pick<AnalyticsListParams, 'page' | 'pageSize'> = {}) {
  return useQuery({
    queryKey: analyticsKeys.customerLtv(params),
    queryFn: () => getCustomerLtv(params),
  })
}

// ── Rider Performance ─────────────────────────────────────────────────────────

export function useRiderPerformance(params: Pick<AnalyticsListParams, 'page' | 'pageSize'> = {}) {
  return useQuery({
    queryKey: analyticsKeys.riderPerformance(params),
    queryFn: () => getRiderPerformance(params),
  })
}

// ── Refresh ───────────────────────────────────────────────────────────────────

export function useRefreshAnalytics() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: refreshAnalytics,
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['analytics'] })
    },
  })
}
