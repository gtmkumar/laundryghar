import { useState } from 'react'
import { useDailyStoreRevenue } from '@/hooks/useAnalytics'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Card } from '@/components/ui/card'
import type { DailyStoreRevenueDto } from '@/types/api'

function fmtCurrency(n: number) {
  return `₹${n.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function fmtNum(n: number) {
  return n.toLocaleString('en-IN')
}

// Simple inline bar visualisation — no external charting dep
function MiniBar({ value, max }: { value: number; max: number }) {
  const pct = max > 0 ? Math.round((value / max) * 100) : 0
  return (
    <div className="flex items-center gap-2">
      <div className="flex-1 h-2 bg-gray-100 rounded-full overflow-hidden">
        <div className="h-full bg-blue-500 rounded-full" style={{ width: `${pct}%` }} />
      </div>
      <span className="text-xs tabular-nums text-gray-500 w-10 text-right">{pct}%</span>
    </div>
  )
}

export function DailyRevenueTab() {
  const today = new Date().toISOString().slice(0, 10)
  const thirtyAgo = new Date(Date.now() - 30 * 86400_000).toISOString().slice(0, 10)

  const [from, setFrom] = useState(thirtyAgo)
  const [to, setTo] = useState(today)

  const { data, isLoading, isError, error, refetch } = useDailyStoreRevenue({ from, to })

  const maxRevenue = Math.max(...(data ?? []).map((r) => r.grossRevenue), 1)

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap gap-4 items-end">
        <div className="space-y-1">
          <Label className="text-xs text-gray-500">From</Label>
          <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="w-40" />
        </div>
        <div className="space-y-1">
          <Label className="text-xs text-gray-500">To</Label>
          <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} className="w-40" />
        </div>
      </div>

      {isLoading && <LoadingState message="Loading daily revenue..." />}
      {isError && <ErrorState error={error as Error} onRetry={() => void refetch()} />}

      {data && (
        <Card>
          <div className="overflow-x-auto">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-gray-100 bg-gray-50">
                  {[
                    'Date',
                    'Orders',
                    'Delivered',
                    'Cancelled',
                    'Express',
                    'Gross Revenue',
                    'Collected',
                    'Outstanding',
                    'Avg Order',
                    'Customers',
                    'Revenue Bar',
                  ].map((h) => (
                    <th
                      key={h}
                      className="px-3 py-2 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide whitespace-nowrap"
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data.length === 0 ? (
                  <tr>
                    <td colSpan={11} className="px-4 py-6 text-center text-gray-400">
                      No data for the selected date range.
                    </td>
                  </tr>
                ) : (
                  data.map((r: DailyStoreRevenueDto) => (
                    <tr
                      key={`${r.storeId}-${r.revenueDate}`}
                      className="border-b border-gray-50 hover:bg-gray-50"
                    >
                      <td className="px-3 py-2 font-mono text-xs">{r.revenueDate}</td>
                      <td className="px-3 py-2 tabular-nums text-right">{fmtNum(r.ordersCount)}</td>
                      <td className="px-3 py-2 tabular-nums text-right text-green-700">
                        {fmtNum(r.deliveredOrders)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right text-red-600">
                        {fmtNum(r.cancelledOrders)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right text-purple-600">
                        {fmtNum(r.expressOrders)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right font-medium">
                        {fmtCurrency(r.grossRevenue)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right text-emerald-700">
                        {fmtCurrency(r.collectedAmount)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right text-orange-600">
                        {fmtCurrency(r.outstandingAmount)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right">
                        {fmtCurrency(r.avgOrderValue)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right">
                        {fmtNum(r.uniqueCustomers)}
                      </td>
                      <td className="px-3 py-2 w-32">
                        <MiniBar value={r.grossRevenue} max={maxRevenue} />
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </Card>
      )}
    </div>
  )
}
