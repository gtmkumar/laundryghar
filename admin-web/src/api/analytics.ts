import { analyticsClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  AnalyticsDashboard,
  DailyStoreRevenueDto,
  MonthlyFranchiseRevenueDto,
  WarehouseThroughputDto,
  CustomerLtvDto,
  RiderPerformanceDto,
  RefreshResultItem,
  AnalyticsListParams,
} from '@/types/api'

const ADMIN = '/api/v1/admin/analytics'

// ── Dashboard ─────────────────────────────────────────────────────────────────

export async function getAnalyticsDashboard(): Promise<AnalyticsDashboard> {
  const { data } = await analyticsClient.get<ApiResponse<AnalyticsDashboard>>(`${ADMIN}/dashboard`)
  return unwrap(data)
}

// ── Daily Store Revenue ───────────────────────────────────────────────────────

export async function getDailyStoreRevenue(
  params: Pick<AnalyticsListParams, 'storeId' | 'from' | 'to'> = {},
): Promise<DailyStoreRevenueDto[]> {
  const { data } = await analyticsClient.get<ApiResponse<DailyStoreRevenueDto[]>>(
    `${ADMIN}/daily-store-revenue`,
    { params },
  )
  return unwrap(data)
}

// ── Monthly Franchise Revenue ─────────────────────────────────────────────────

export async function getMonthlyFranchiseRevenue(
  params: Pick<AnalyticsListParams, 'franchiseId' | 'year'> = {},
): Promise<MonthlyFranchiseRevenueDto[]> {
  const { data } = await analyticsClient.get<ApiResponse<MonthlyFranchiseRevenueDto[]>>(
    `${ADMIN}/monthly-franchise-revenue`,
    { params },
  )
  return unwrap(data)
}

// ── Warehouse Throughput ──────────────────────────────────────────────────────

export async function getWarehouseThroughput(
  params: Pick<AnalyticsListParams, 'warehouseId' | 'from' | 'to'> = {},
): Promise<WarehouseThroughputDto[]> {
  const { data } = await analyticsClient.get<ApiResponse<WarehouseThroughputDto[]>>(
    `${ADMIN}/warehouse-throughput`,
    { params },
  )
  return unwrap(data)
}

// ── Customer LTV (paginated) ──────────────────────────────────────────────────

export async function getCustomerLtv(
  params: Pick<AnalyticsListParams, 'page' | 'pageSize'> = {},
): Promise<PaginatedList<CustomerLtvDto>> {
  const { data } = await analyticsClient.get<ApiResponse<PaginatedList<CustomerLtvDto>>>(
    `${ADMIN}/customer-ltv`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

// ── Rider Performance (paginated) ─────────────────────────────────────────────

export async function getRiderPerformance(
  params: Pick<AnalyticsListParams, 'page' | 'pageSize'> = {},
): Promise<PaginatedList<RiderPerformanceDto>> {
  const { data } = await analyticsClient.get<ApiResponse<PaginatedList<RiderPerformanceDto>>>(
    `${ADMIN}/rider-performance`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

// ── Refresh materialized views ────────────────────────────────────────────────

export async function refreshAnalytics(): Promise<RefreshResultItem[]> {
  const { data } = await analyticsClient.post<ApiResponse<RefreshResultItem[]>>(
    `${ADMIN}/refresh`,
  )
  return unwrap(data)
}
