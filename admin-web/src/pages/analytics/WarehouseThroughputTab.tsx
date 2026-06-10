import { useState } from 'react'
import { useWarehouseThroughput } from '@/hooks/useAnalytics'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Card } from '@/components/ui/card'
import type { WarehouseThroughputDto } from '@/types/api'

function fmtNum(n: number) {
  return n.toLocaleString('en-IN')
}

export function WarehouseThroughputTab() {
  const today = new Date().toISOString().slice(0, 10)
  const thirtyAgo = new Date(Date.now() - 30 * 86400_000).toISOString().slice(0, 10)

  const [from, setFrom] = useState(thirtyAgo)
  const [to, setTo] = useState(today)

  const { data, isLoading, isError, error, refetch } = useWarehouseThroughput({ from, to })

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

      {isLoading && <LoadingState message="Loading warehouse throughput..." />}
      {isError && (isForbiddenError(error) ? <ForbiddenState /> : <ErrorState error={error as Error} onRetry={() => void refetch()} />)}

      {data && (
        <Card>
          <div className="overflow-x-auto">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-gray-100 bg-gray-50">
                  {[
                    'Date',
                    'Warehouse',
                    'Received',
                    'Delivered',
                    'Issues',
                    'Rewash',
                    'Avg TAT (hrs)',
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
                    <td colSpan={7} className="px-4 py-6 text-center text-gray-400">
                      No data for the selected date range.
                    </td>
                  </tr>
                ) : (
                  data.map((r: WarehouseThroughputDto) => (
                    <tr
                      key={`${r.warehouseId}-${r.throughputDate}`}
                      className="border-b border-gray-50 hover:bg-gray-50"
                    >
                      <td className="px-3 py-2 font-mono text-xs">{r.throughputDate}</td>
                      <td className="px-3 py-2 font-mono text-xs text-gray-400">
                        {r.warehouseId.slice(0, 8)}…
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right">
                        {fmtNum(r.garmentsReceived)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right text-green-700">
                        {fmtNum(r.garmentsDelivered)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right text-red-500">
                        {fmtNum(r.issuesCount)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right text-orange-500">
                        {fmtNum(r.rewashCount)}
                      </td>
                      <td className="px-3 py-2 tabular-nums text-right">
                        {r.avgTatHours != null ? r.avgTatHours.toFixed(1) : '—'}
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
