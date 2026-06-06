import { useState } from 'react'
import { useRiderPerformance } from '@/hooks/useAnalytics'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { Pagination } from '@/components/shared/Pagination'
import { Card } from '@/components/ui/card'
import type { RiderPerformanceDto } from '@/types/api'

function fmtNum(n: number, decimals = 0) {
  return n.toLocaleString('en-IN', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  })
}

function CompletionBadge({ rate }: { rate: number }) {
  const pct = Math.round(rate * 100)
  const color =
    pct >= 90
      ? 'bg-green-100 text-green-700'
      : pct >= 70
        ? 'bg-yellow-100 text-yellow-700'
        : 'bg-red-100 text-red-600'
  return (
    <span className={`text-xs font-semibold px-2 py-0.5 rounded-full tabular-nums ${color}`}>
      {pct}%
    </span>
  )
}

export function RiderPerformanceTab() {
  const [page, setPage] = useState(1)
  const { data, isLoading, isError, error, refetch } = useRiderPerformance({ page, pageSize: 20 })

  return (
    <div className="space-y-4">
      {isLoading && <LoadingState message="Loading rider performance..." />}
      {isError && <ErrorState error={error as Error} onRetry={() => void refetch()} />}

      {data && (
        <>
          <Card>
            <div className="overflow-x-auto">
              <table className="min-w-full text-sm">
                <thead>
                  <tr className="border-b border-gray-100 bg-gray-50">
                    {[
                      'Date',
                      'Rider Code',
                      'Franchise',
                      'Assignments',
                      'Completed',
                      'Failed',
                      'Pickups',
                      'Deliveries',
                      'Total Km',
                      'Avg Duration',
                      'Rating',
                      'Completion',
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
                  {data.list.length === 0 ? (
                    <tr>
                      <td colSpan={12} className="px-4 py-6 text-center text-gray-400">
                        No rider performance data yet.
                      </td>
                    </tr>
                  ) : (
                    data.list.map((r: RiderPerformanceDto) => (
                      <tr
                        key={`${r.riderId}-${r.perfDate}`}
                        className="border-b border-gray-50 hover:bg-gray-50"
                      >
                        <td className="px-3 py-2 font-mono text-xs">{r.perfDate}</td>
                        <td className="px-3 py-2 font-semibold text-gray-700">{r.riderCode}</td>
                        <td className="px-3 py-2 font-mono text-xs text-gray-400">
                          {r.franchiseId.slice(0, 8)}…
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right">
                          {fmtNum(r.assignmentsTotal)}
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right text-green-700">
                          {fmtNum(r.assignmentsCompleted)}
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right text-red-500">
                          {fmtNum(r.assignmentsFailed)}
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right">
                          {fmtNum(r.pickupsDone)}
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right">
                          {fmtNum(r.deliveriesDone)}
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right">
                          {fmtNum(r.totalKm, 1)} km
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right">
                          {fmtNum(r.avgDurationMin, 1)} min
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right text-amber-600 font-medium">
                          {r.ratingAverage.toFixed(1)}
                        </td>
                        <td className="px-3 py-2">
                          <CompletionBadge rate={r.completionRate} />
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </Card>

          <Pagination
            page={page}
            hasPrevious={data.hasPreviousPage}
            hasNext={data.hasNextPage}
            onPrevious={() => setPage((p) => Math.max(1, p - 1))}
            onNext={() => setPage((p) => p + 1)}
          />
        </>
      )}
    </div>
  )
}
