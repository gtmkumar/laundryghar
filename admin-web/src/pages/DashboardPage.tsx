/**
 * DashboardPage — redesigned warm dashboard.
 *
 * Data wiring:
 *  - KPI 1 (Orders Today) + KPI 2 (Revenue Today): GET :5005/analytics/dashboard → today.*
 *  - KPI 3 (Pending Pickup): getOrders({status:'pickup_scheduled', pageSize:1}) → totalCount
 *    NOTE: there is no dedicated /count endpoint, but the list response carries a
 *    server-computed TotalCount (COUNT(*) independent of paging). We fetch a single
 *    row and read totalCount — correct at any scale, ~1/200th the payload of the old
 *    pageSize:200 + list.length approach (which silently capped each KPI at 200).
 *  - KPI 4 (In Wash): getOrders({status:'received', pageSize:1}) → totalCount
 *  - KPI 5 (Out for Delivery): getOrders({status:'out_for_delivery', pageSize:1}) → totalCount
 *  - Revenue chart: getDailyStoreRevenue (last 14 days), grouped by date, summed grossRevenue
 *  - Store leaderboard: daily-store-revenue today, joined with getStores for names
 *  - Live feed: getOrders({pageSize:8}), refetchInterval 30s, joined with getStores for names
 *  - Smart Insight: derived from chart data (today vs 7-day avg)
 */

import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import { ArrowUpRight, ArrowDownRight, RefreshCw, Zap } from 'lucide-react'
import { useAnalyticsDashboard, useDailyStoreRevenue } from '@/hooks/useAnalytics'
import { useOrders } from '@/hooks/useOrders'
import { useStores } from '@/hooks/useTenancy'
import { useCustomerNameMap } from '@/hooks/useCatalog'
import { useBrandStore } from '@/stores/brandStore'
import type { OrderDto, DailyStoreRevenueDto, StoreDto } from '@/types/api'
import { useStatusLabel, paymentTone, PAYMENT_TONE_CLASS } from './orders/orderFormat'
import { ActiveRidersPanel } from './dashboard/ActiveRidersPanel'
import { NeedsActionPanel } from './dashboard/NeedsActionPanel'

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmtINR(n: number): string {
  return `₹${n.toLocaleString('en-IN', { maximumFractionDigits: 0 })}`
}

function isoToday(): string {
  return new Date().toISOString().slice(0, 10)
}

function iso14DaysAgo(): string {
  return new Date(Date.now() - 14 * 86_400_000).toISOString().slice(0, 10)
}

function isoYesterday(): string {
  return new Date(Date.now() - 86_400_000).toISOString().slice(0, 10)
}

/** Compute sum of a numeric field across DailyStoreRevenue rows for a given date */
function sumForDate(rows: DailyStoreRevenueDto[], date: string, field: 'ordersCount' | 'grossRevenue'): number {
  return rows.filter((r) => r.revenueDate === date).reduce((s, r) => s + r[field], 0)
}

// Group DailyStoreRevenue rows by date and sum grossRevenue
function aggregateByDate(rows: DailyStoreRevenueDto[]): { date: string; revenue: number }[] {
  const map = new Map<string, number>()
  for (const r of rows) {
    map.set(r.revenueDate, (map.get(r.revenueDate) ?? 0) + r.grossRevenue)
  }
  return Array.from(map.entries())
    .map(([date, revenue]) => ({ date, revenue }))
    .sort((a, b) => a.date.localeCompare(b.date))
}

function avg(nums: number[]): number {
  if (nums.length === 0) return 0
  return nums.reduce((s, n) => s + n, 0) / nums.length
}

// ── Skeleton ──────────────────────────────────────────────────────────────────

function Skeleton({ className, style }: { className?: string; style?: React.CSSProperties }) {
  return <div className={`skeleton rounded-lg ${className ?? ''}`} style={style} />
}

// ── KPI Card ──────────────────────────────────────────────────────────────────

interface KpiCardProps {
  label: string
  value: string | number
  sub?: string
  trend?: { pct: number; up: boolean } | null
  loading?: boolean
}

function KpiCard({ label, value, sub, trend, loading }: KpiCardProps) {
  return (
    <div className="bg-white rounded-2xl p-5 shadow-sm border border-[#ede9e0]">
      <p className="text-xs font-semibold uppercase tracking-wider text-gray-400">{label}</p>
      {loading ? (
        <div className="mt-2 space-y-2">
          <Skeleton className="h-8 w-24" />
          <Skeleton className="h-3 w-16" />
        </div>
      ) : (
        <>
          <p className="text-3xl font-bold text-gray-900 mt-1.5 tabular">{value}</p>
          <div className="flex items-center gap-2 mt-1">
            {trend && (
              <span
                className="flex items-center gap-0.5 text-xs font-semibold"
                style={{ color: trend.up ? '#5C6E2E' : '#dc2626' }}
              >
                {trend.up ? <ArrowUpRight className="h-3 w-3" /> : <ArrowDownRight className="h-3 w-3" />}
                {Math.abs(trend.pct).toFixed(1)}%
              </span>
            )}
            {sub && <p className="text-xs text-gray-400">{sub}</p>}
          </div>
        </>
      )}
    </div>
  )
}

// ── Revenue bar chart (CSS-only) ──────────────────────────────────────────────

interface RevenueChartProps {
  bars: { date: string; revenue: number }[]
  loading: boolean
}

function RevenueChart({ bars, loading }: RevenueChartProps) {
  const today = isoToday()
  const maxRev = Math.max(...bars.map((b) => b.revenue), 1)
  const last7 = bars.slice(-7).map((b) => b.revenue)
  const avgRev = avg(last7)
  const todayRev = bars.find((b) => b.date === today)?.revenue ?? 0
  const trendPct = avgRev > 0 ? ((todayRev - avgRev) / avgRev) * 100 : 0

  return (
    <div className="bg-white rounded-3xl p-6 shadow-sm border border-[#ede9e0]">
      <div className="flex items-start justify-between mb-4">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wider text-gray-400">Revenue</p>
          <p className="font-semibold text-gray-800 mt-0.5">Last 14 days</p>
        </div>
        <div className="text-right text-xs text-gray-400">
          <span className="inline-flex items-center gap-1">
            <span className="w-2.5 h-2.5 rounded-sm bg-[#E6A23C] inline-block" /> Today
            <span className="ml-2 w-2.5 h-2.5 rounded-sm bg-[#5C6E2E] inline-block" /> Revenue
          </span>
        </div>
      </div>

      {loading ? (
        <div className="flex items-end gap-1 h-32">
          {Array.from({ length: 14 }).map((_, i) => (
            <Skeleton key={i} className="flex-1 h-full" style={{ height: `${30 + Math.random() * 60}%` } as React.CSSProperties} />
          ))}
        </div>
      ) : bars.length === 0 ? (
        <div className="h-32 flex items-center justify-center text-sm text-gray-400">
          No revenue data for the last 14 days
        </div>
      ) : (
        <div className="flex items-end gap-1 h-32">
          {bars.map((b) => {
            const isToday = b.date === today
            const pct = maxRev > 0 ? (b.revenue / maxRev) * 100 : 0
            const shortDate = b.date.slice(5) // MM-DD
            return (
              <div key={b.date} className="flex-1 h-full flex flex-col items-center justify-end gap-1 group relative">
                {/* Tooltip */}
                <div className="absolute bottom-full mb-1 hidden group-hover:block bg-gray-800 text-white text-[10px] rounded px-2 py-1 whitespace-nowrap z-10">
                  {b.date}: {fmtINR(b.revenue)}
                </div>
                <div
                  className="w-full rounded-t-sm transition-all"
                  style={{
                    height: `${Math.max(pct, 2)}%`,
                    background: isToday ? 'var(--lg-amber)' : 'var(--lg-green)',
                    opacity: isToday ? 1 : 0.7,
                  }}
                />
                <span className="text-[8px] text-gray-400 rotate-45 origin-left w-6 overflow-hidden">
                  {shortDate}
                </span>
              </div>
            )
          })}
        </div>
      )}

      <div className="mt-4 pt-3 border-t border-[#ede9e0] flex items-center gap-4 text-xs text-gray-500">
        <span>7-day avg {fmtINR(avgRev)}</span>
        {avgRev > 0 && (
          <span
            className="flex items-center gap-0.5 font-semibold"
            style={{ color: trendPct >= 0 ? '#5C6E2E' : '#dc2626' }}
          >
            {trendPct >= 0 ? <ArrowUpRight className="h-3 w-3" /> : <ArrowDownRight className="h-3 w-3" />}
            {Math.abs(trendPct).toFixed(1)}%
          </span>
        )}
      </div>
    </div>
  )
}

// ── Store leaderboard ─────────────────────────────────────────────────────────

interface LeaderboardProps {
  revenueRows: DailyStoreRevenueDto[]
  stores: StoreDto[]
  loading: boolean
}

function StoreLeaderboard({ revenueRows, stores, loading }: LeaderboardProps) {
  const today = isoToday()
  const storeMap = useMemo(() => {
    const m = new Map<string, string>()
    stores.forEach((s) => m.set(s.id, s.name))
    return m
  }, [stores])

  const todayByStore = useMemo(() => {
    const map = new Map<string, number>()
    revenueRows
      .filter((r) => r.revenueDate === today)
      .forEach((r) => {
        map.set(r.storeId, (map.get(r.storeId) ?? 0) + r.grossRevenue)
      })
    return Array.from(map.entries())
      .map(([storeId, revenue]) => ({ storeId, name: storeMap.get(storeId) ?? storeId.slice(-6), revenue }))
      .sort((a, b) => b.revenue - a.revenue)
  }, [revenueRows, storeMap, today])

  const maxRev = Math.max(...todayByStore.map((s) => s.revenue), 1)

  return (
    <div className="bg-white rounded-3xl p-6 shadow-sm border border-[#ede9e0] flex flex-col">
      <div className="mb-4">
        <p className="text-xs font-semibold uppercase tracking-wider text-gray-400">Store Leaderboard</p>
        <p className="font-semibold text-gray-800 mt-0.5">Revenue · today</p>
      </div>

      {loading ? (
        <div className="space-y-3">
          {[1, 2, 3].map((i) => (
            <div key={i} className="space-y-1">
              <Skeleton className="h-4 w-32" />
              <Skeleton className="h-2 w-full" />
            </div>
          ))}
        </div>
      ) : todayByStore.length === 0 ? (
        <p className="text-sm text-gray-400 flex-1 flex items-center justify-center">No revenue data for today</p>
      ) : (
        <div className="space-y-4 flex-1">
          {todayByStore.map((s, i) => {
            const pct = maxRev > 0 ? (s.revenue / maxRev) * 100 : 0
            return (
              <div key={s.storeId}>
                <div className="flex items-center justify-between text-sm mb-1">
                  <span className="font-medium text-gray-700 truncate flex items-center gap-1.5">
                    <span className="text-gray-400 text-xs tabular">#{i + 1}</span>
                    {s.name}
                  </span>
                  <span className="text-gray-800 font-semibold tabular shrink-0 ml-2">{fmtINR(s.revenue)}</span>
                </div>
                {/* Progress bar track */}
                <div className="h-2 rounded-full" style={{ background: '#ede9e0' }}>
                  <div
                    className="h-2 rounded-full transition-all"
                    style={{ width: `${pct}%`, background: 'var(--lg-green)' }}
                  />
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

// ── Order status pill ─────────────────────────────────────────────────────────

const STATUS_COLORS: Record<string, { bg: string; text: string }> = {
  draft:               { bg: '#f3f4f6', text: '#6b7280' },
  placed:              { bg: '#eff6ff', text: '#3b82f6' },
  confirmed:           { bg: '#f0fdf4', text: '#16a34a' },
  pickup_scheduled:    { bg: '#fef9c3', text: '#ca8a04' },
  pickup_assigned:     { bg: '#fef9c3', text: '#ca8a04' },
  pickup_in_progress:  { bg: '#fef9c3', text: '#ca8a04' },
  received:            { bg: '#f0f9ff', text: '#0284c7' },
  sorting:             { bg: '#f0f9ff', text: '#0284c7' },
  in_process:          { bg: '#faf5ff', text: '#7c3aed' },
  ready_for_delivery:  { bg: '#f0fdf4', text: '#16a34a' },
  out_for_delivery:    { bg: '#fff7ed', text: '#ea580c' },
  delivered:           { bg: '#f0fdf4', text: '#15803d' },
  cancelled:           { bg: '#fef2f2', text: '#dc2626' },
}

function StatusPill({ status }: { status: string }) {
  const labelFor = useStatusLabel()
  const colors = STATUS_COLORS[status] ?? { bg: '#f3f4f6', text: '#6b7280' }
  return (
    <span
      className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium whitespace-nowrap"
      style={{ background: colors.bg, color: colors.text }}
    >
      {labelFor(status)}
    </span>
  )
}

// ── Live order feed ───────────────────────────────────────────────────────────

interface LiveFeedProps {
  orders: OrderDto[]
  stores: StoreDto[]
  customerNameMap: Map<string, string>
  loading: boolean
  isRefetching: boolean
}

function LiveOrderFeed({ orders, stores, customerNameMap, loading, isRefetching }: LiveFeedProps) {
  const storeMap = useMemo(() => {
    const m = new Map<string, string>()
    stores.forEach((s) => m.set(s.id, s.name))
    return m
  }, [stores])

  return (
    <div className="bg-white rounded-3xl p-6 shadow-sm border border-[#ede9e0]">
      <div className="flex items-center justify-between mb-4">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wider text-gray-400">Live Order Feed</p>
          <p className="font-semibold text-gray-800 mt-0.5 flex items-center gap-1.5">
            Refreshes every 30s
            {isRefetching && <RefreshCw className="h-3.5 w-3.5 animate-spin text-gray-400" />}
          </p>
        </div>
        <Link
          to="/orders"
          className="text-xs font-semibold text-lg-green hover:underline flex items-center gap-0.5"
        >
          View all →
        </Link>
      </div>

      {loading ? (
        <div className="space-y-3">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-10 w-full" />
          ))}
        </div>
      ) : orders.length === 0 ? (
        <p className="text-sm text-gray-400 text-center py-6">No orders found</p>
      ) : (
        <div className="overflow-x-auto -mx-2">
          <table className="w-full text-sm">
            <thead>
              <tr>
                {['Order', 'Customer', 'Store', 'Items', 'Status', 'Amount'].map((h) => (
                  <th
                    key={h}
                    className="px-2 py-2 text-left text-[10px] font-semibold uppercase tracking-wider text-gray-400 border-b border-[#ede9e0] whitespace-nowrap"
                  >
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {orders.map((order) => (
                <tr key={order.id} className="border-b border-[#f5f2ea] hover:bg-[#faf9f5] transition-colors">
                  <td className="px-2 py-2.5 font-mono text-xs text-gray-700 whitespace-nowrap max-w-[160px] truncate">
                    #{order.orderNumber}
                  </td>
                  <td className="px-2 py-2.5 text-xs text-gray-600 whitespace-nowrap max-w-[140px] truncate">
                    {customerNameMap.get(order.customerId) ?? `…${order.customerId.slice(-6)}`}
                  </td>
                  <td className="px-2 py-2.5 text-xs text-gray-600 max-w-[140px] truncate">
                    <span className="flex items-center gap-1">
                      <span className="truncate">{storeMap.get(order.storeId) ?? `…${order.storeId.slice(-4)}`}</span>
                      {order.channel && (
                        <span className="shrink-0 rounded bg-gray-100 px-1 text-[9px] font-medium uppercase text-gray-400">
                          {order.channel}
                        </span>
                      )}
                    </span>
                  </td>
                  <td className="px-2 py-2.5 text-xs text-gray-600 text-center tabular">
                    {order.totalItems}
                  </td>
                  <td className="px-2 py-2.5">
                    <div className="flex flex-wrap items-center gap-1">
                      <StatusPill status={order.status} />
                      {order.isExpress && (
                        <span className="inline-flex items-center gap-0.5 rounded-full border border-amber-200 bg-amber-50 px-1.5 py-0.5 text-[10px] font-semibold text-amber-700">
                          <Zap className="h-2.5 w-2.5" />
                        </span>
                      )}
                      <span
                        className={`rounded-full border px-1.5 py-0.5 text-[10px] font-medium ${PAYMENT_TONE_CLASS[paymentTone(order.paymentStatus)]}`}
                      >
                        {order.paymentStatus}
                      </span>
                    </div>
                  </td>
                  <td className="px-2 py-2.5 text-xs font-semibold text-gray-800 tabular whitespace-nowrap">
                    {fmtINR(order.grandTotal)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

// ── Smart Insight card ────────────────────────────────────────────────────────

interface InsightProps {
  bars: { date: string; revenue: number }[]
  todayRevenue: number
  loading: boolean
}

function SmartInsight({ bars, todayRevenue, loading }: InsightProps) {
  const insight = useMemo(() => {
    if (bars.length < 2) return null
    const last7 = bars.slice(-8, -1).map((b) => b.revenue) // last 7 before today
    if (last7.length === 0) return null
    const sevenDayAvg = avg(last7)
    if (sevenDayAvg === 0) return null
    const pct = ((todayRevenue - sevenDayAvg) / sevenDayAvg) * 100
    const direction = pct >= 0 ? 'above' : 'below'
    return {
      text: `Today's revenue ${fmtINR(todayRevenue)} is ${Math.abs(pct).toFixed(1)}% ${direction} the 7-day average of ${fmtINR(sevenDayAvg)}.`,
      positive: pct >= 0,
    }
  }, [bars, todayRevenue])

  return (
    <div
      className="rounded-3xl p-6 shadow-sm border flex flex-col justify-between"
      style={{ background: 'var(--lg-green)', borderColor: 'var(--lg-green-hover)' }}
    >
      <div>
        <p className="text-xs font-semibold uppercase tracking-wider text-white/60">Smart Insight</p>
        <p className="font-semibold text-white mt-0.5">Operational summary</p>
      </div>

      {loading ? (
        <div className="mt-6 space-y-3">
          <Skeleton className="h-4 w-full" style={{ background: 'rgba(255,255,255,0.15)' } as React.CSSProperties} />
          <Skeleton className="h-4 w-3/4" style={{ background: 'rgba(255,255,255,0.15)' } as React.CSSProperties} />
        </div>
      ) : insight ? (
        <div className="mt-6">
          <p className="text-white text-sm leading-relaxed">{insight.text}</p>
          <p className="mt-3 text-xs text-white/50">
            Derived from daily revenue aggregates · refreshes with chart data
          </p>
        </div>
      ) : (
        <div className="mt-6">
          <p className="text-white text-sm leading-relaxed">
            Keep operations flowing — track orders, manage stores, and monitor riders from this panel.
          </p>
          <p className="mt-3 text-xs text-white/50">
            Insight will appear once revenue data is available.
          </p>
        </div>
      )}
    </div>
  )
}

// ── Dashboard Page ────────────────────────────────────────────────────────────

export function DashboardPage() {
  const { activeBrandId } = useBrandStore()
  // Gate all brand-scoped queries behind activeBrandId to avoid 401s before
  // the auto-select in AppShell resolves. The interceptor picks up activeBrandId
  // synchronously from brandStore once set, so enabled=false prevents premature fires.
  const enabled = Boolean(activeBrandId)

  // ── Analytics dashboard (orders today + revenue today)
  const dashboardQ = useAnalyticsDashboard(enabled)

  // ── Revenue chart: last 14 days
  const revenueQ = useDailyStoreRevenue({ from: iso14DaysAgo(), to: isoToday() }, enabled)
  const chartBars = useMemo(() => aggregateByDate(revenueQ.data ?? []), [revenueQ.data])

  // ── Order status counts — there is no dedicated /count endpoint, but the list
  // response carries a server-computed TotalCount (a COUNT(*) independent of
  // paging). So fetch a single row (pageSize: 1) and read totalCount instead of
  // pulling 200 rows just to take .length — same number, ~1/200th the payload,
  // and correct past 200 rows (the old approach silently capped the KPI at 200).
  const pickupStatusQ = useOrders({ status: 'pickup_scheduled', pageSize: 1 }, undefined, enabled)
  const inWashQ = useOrders({ status: 'received', pageSize: 1 }, undefined, enabled)
  const deliveryQ = useOrders({ status: 'out_for_delivery', pageSize: 1 }, undefined, enabled)

  // ── Live feed (last 8 orders, refreshes every 30s)
  const feedQ = useOrders({ pageSize: 8 }, 30_000, enabled)

  // ── Stores for joining names (not brand-scoped in the same way — platform admin can fetch all).
  // Still gated behind `enabled`: getStores rides the X-Brand-Id interceptor, so firing it before
  // the brand auto-select resolves produces 401 noise on every dashboard mount (DEF-R3-1).
  const storesQ = useStores({ pageSize: 100 }, enabled)
  const stores = storesQ.data?.list ?? []

  // ── Customer name map for live feed (pageSize 100, brand-scoped via X-Brand-Id header)
  const customerNameMap = useCustomerNameMap(enabled)

  // ── KPI derivations
  const today = dashboardQ.data?.today
  const ordersToday = today?.ordersCount ?? 0
  const revenueToday = today?.grossRevenue ?? 0

  const pendingPickupCount = pickupStatusQ.data?.totalCount ?? 0
  const inWashCount = inWashQ.data?.totalCount ?? 0
  const outForDeliveryCount = deliveryQ.data?.totalCount ?? 0

  const feedOrders = feedQ.data?.list ?? []
  const isLoading = dashboardQ.isLoading

  // ── Today-vs-yesterday trends from DailyStoreRevenue data already in cache
  const todayIso = isoToday()
  const yesterdayIso = isoYesterday()
  const revenueRows = revenueQ.data ?? []

  const ordersTodayFromRevData = useMemo(
    () => sumForDate(revenueRows, todayIso, 'ordersCount'),
    [revenueRows, todayIso],
  )
  const ordersYesterday = useMemo(
    () => sumForDate(revenueRows, yesterdayIso, 'ordersCount'),
    [revenueRows, yesterdayIso],
  )
  const revenueYesterday = useMemo(
    () => sumForDate(revenueRows, yesterdayIso, 'grossRevenue'),
    [revenueRows, yesterdayIso],
  )

  // Use analytics-dashboard ordersCount for display value (source of truth),
  // but derive trend from the daily-store-revenue table which has yesterday too.
  const ordersTrend = useMemo(() => {
    if (ordersYesterday === 0) return null
    const pct = ((ordersTodayFromRevData - ordersYesterday) / ordersYesterday) * 100
    return { pct, up: pct >= 0 }
  }, [ordersTodayFromRevData, ordersYesterday])

  const revenueTrend = useMemo(() => {
    // Fall back to 7-day avg trend if yesterday has no data (sparse seed)
    if (revenueYesterday > 0) {
      const pct = ((revenueToday - revenueYesterday) / revenueYesterday) * 100
      return { pct, up: pct >= 0 }
    }
    if (chartBars.length < 2) return null
    const prev7 = chartBars.slice(-8, -1).map((b) => b.revenue)
    if (prev7.length === 0) return null
    const a = avg(prev7)
    if (a === 0) return null
    const pct = ((revenueToday - a) / a) * 100
    return { pct, up: pct >= 0 }
  }, [revenueYesterday, revenueToday, chartBars])

  return (
    <div className="space-y-5">
      {/* KPI row */}
      <div className="grid grid-cols-2 lg:grid-cols-5 gap-4">
        <KpiCard
          label="Orders Today"
          value={ordersToday}
          sub="vs yesterday"
          trend={ordersTrend}
          loading={isLoading}
        />
        <KpiCard
          label="Revenue Today"
          value={fmtINR(revenueToday)}
          sub="vs yesterday"
          trend={revenueTrend}
          loading={isLoading}
        />
        <KpiCard
          label="Pending Pickup"
          value={pendingPickupCount}
          sub="awaiting pickup"
          loading={pickupStatusQ.isLoading}
        />
        <KpiCard
          label="In Wash"
          value={inWashCount}
          sub="in process"
          loading={inWashQ.isLoading}
        />
        <KpiCard
          label="Out for Delivery"
          value={outForDeliveryCount}
          sub="en route"
          loading={deliveryQ.isLoading}
        />
      </div>

      {/* Ops row: Active riders + Needs action — live operational panels */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <ActiveRidersPanel />
        <NeedsActionPanel />
      </div>

      {/* Row 2: Revenue chart + Store leaderboard */}
      <div className="grid grid-cols-1 lg:grid-cols-5 gap-4">
        <div className="lg:col-span-3">
          <RevenueChart bars={chartBars} loading={revenueQ.isLoading} />
        </div>
        <div className="lg:col-span-2">
          <StoreLeaderboard
            revenueRows={revenueQ.data ?? []}
            stores={stores}
            loading={revenueQ.isLoading || storesQ.isLoading}
          />
        </div>
      </div>

      {/* Row 3: Live feed + Smart Insight */}
      <div className="grid grid-cols-1 lg:grid-cols-5 gap-4">
        <div className="lg:col-span-3">
          <LiveOrderFeed
            orders={feedOrders}
            stores={stores}
            customerNameMap={customerNameMap}
            loading={feedQ.isLoading}
            isRefetching={feedQ.isFetching && !feedQ.isLoading}
          />
        </div>
        <div className="lg:col-span-2">
          <SmartInsight
            bars={chartBars}
            todayRevenue={revenueToday}
            loading={revenueQ.isLoading || isLoading}
          />
        </div>
      </div>
    </div>
  )
}
