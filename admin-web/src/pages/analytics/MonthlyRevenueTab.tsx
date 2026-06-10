import { useState } from 'react'
import { useMonthlyFranchiseRevenue } from '@/hooks/useAnalytics'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Card } from '@/components/ui/card'
import type { MonthlyFranchiseRevenueDto } from '@/types/api'

function fmtCurrency(n: number) {
  return `₹${n.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function fmtNum(n: number) {
  return n.toLocaleString('en-IN')
}

function MiniBar({ value, max }: { value: number; max: number }) {
  const pct = max > 0 ? Math.round((value / max) * 100) : 0
  return (
    <div className="flex items-center gap-2">
      <div className="flex-1 h-2 bg-gray-100 rounded-full overflow-hidden">
        <div className="h-full bg-emerald-500 rounded-full" style={{ width: `${pct}%` }} />
      </div>
      <span className="text-xs tabular-nums text-gray-500 w-10 text-right">{pct}%</span>
    </div>
  )
}

export function MonthlyRevenueTab() {
  const [year, setYear] = useState(new Date().getFullYear())

  const { data, isLoading, isError, error, refetch } = useMonthlyFranchiseRevenue({ year })

  const maxRevenue = Math.max(...(data ?? []).map((r) => r.grossRevenue), 1)

  return (
    <div className="space-y-4">
      <div className="flex gap-4 items-end">
        <div className="space-y-1">
          <Label className="text-xs text-gray-500">Year</Label>
          <Input
            type="number"
            value={year}
            min={2020}
            max={2100}
            onChange={(e) => setYear(Number(e.target.value))}
            className="w-28"
          />
        </div>
      </div>

      {isLoading && <LoadingState message="Loading monthly revenue..." />}
      {isError && (isForbiddenError(error) ? <ForbiddenState /> : <ErrorState error={error as Error} onRetry={() => void refetch()} />)}

      {data && (
        <Card>
          <div className="overflow-x-auto">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-gray-100 bg-gray-50">
                  {[
                    'Month',
                    'Franchise',
                    'Orders',
                    'Customers',
                    'Gross Revenue',
                    'Net Revenue',
                    'Collected',
                    'Refunds',
                    'Tax',
                    'Avg Order',
                    'Express',
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
                    <td colSpan={12} className="px-4 py-6 text-center text-gray-400">
                      No data for the selected year.
                    </td>
                  </tr>
                ) : (
                  data.map((r: MonthlyFranchiseRevenueDto) => (
                    <tr
                      key={`${r.franchiseId}-${r.revenueMonth}`}
                      className="border-b border-gray-50 hover:bg-gray-50"
                    >
                      <td className="px-3 py-2 font-mono text-xs">{r.revenueMonth.slice(0, 7)}</td>
                      <td className="px-3 py-2 font-mono text-xs text-gray-400 truncate max-w-[100px]">
                        {r.franchiseId.slice(0, 8)}…
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right">{fmtNum(r.ordersCount)}</td>
                      <td className="px-3 py-2 tabular-nums text-right">
                        {fmtNum(r.uniqueCustomers)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right font-medium">
                        {fmtCurrency(r.grossRevenue)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right text-emerald-700">
                        {fmtCurrency(r.netRevenue)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right">
                        {fmtCurrency(r.collectedAmount)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right text-red-500">
                        {fmtCurrency(r.refundAmount)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right text-gray-500">
                        {fmtCurrency(r.totalTax)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right">
                        {fmtCurrency(r.avgOrderValue)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right text-purple-600">
                        {fmtNum(r.expressOrders)}
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
