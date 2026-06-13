import { useState } from 'react'
import { useCustomerLtv } from '@/hooks/useAnalytics'
import { useCustomerNameMap } from '@/hooks/useCatalog'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Pagination } from '@/components/shared/Pagination'
import { Card } from '@/components/ui/card'
import type { CustomerLtvDto } from '@/types/api'

function fmtCurrency(n: number) {
  return `₹${n.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function fmtNum(n: number) {
  return n.toLocaleString('en-IN')
}

function formatDate(s: string) {
  return new Date(s).toLocaleDateString('en-IN', {
    day: '2-digit',
    month: 'short',
    year: '2-digit',
  })
}

export function CustomerLtvTab() {
  const [page, setPage] = useState(1)
  const { data, isLoading, isError, error, refetch } = useCustomerLtv({ page, pageSize: 20 })
  // The LTV endpoint only returns customerId (verified live) — resolve display
  // names client-side via the cached admin-customers list. Unknown ids (beyond
  // the first page of customers) fall back to the short id.
  const nameMap = useCustomerNameMap()

  return (
    <div className="space-y-4">
      {isLoading && <LoadingState message="Loading customer LTV..." />}
      {isError && (isForbiddenError(error) ? <ForbiddenState /> : <ErrorState error={error as Error} onRetry={() => void refetch()} />)}

      {data && (
        <>
          <Card>
            <div className="overflow-x-auto">
              <table className="min-w-full text-sm">
                <thead>
                  <tr className="border-b border-gray-100 bg-gray-50">
                    {[
                      'Customer',
                      'Segment',
                      'LTV Revenue',
                      'Orders',
                      'Avg Order',
                      'First Order',
                      'Last Order',
                      'Days Since',
                      'Express',
                      'Cancelled',
                      'Packages',
                      'Points',
                      'Wallet',
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
                      <td colSpan={13} className="px-4 py-6 text-center text-gray-400">
                        No customer LTV data yet.
                      </td>
                    </tr>
                  ) : (
                    data.list.map((r: CustomerLtvDto) => (
                      <tr key={r.customerId} className="border-b border-gray-50 hover:bg-gray-50">
                        <td className="px-3 py-2 whitespace-nowrap">
                          {nameMap.has(r.customerId) ? (
                            <span className="font-medium text-gray-800" title={r.customerId}>
                              {nameMap.get(r.customerId)}
                            </span>
                          ) : (
                            <span className="font-mono text-xs text-gray-500" title={r.customerId}>
                              {r.customerId.slice(0, 8)}…
                            </span>
                          )}
                        </td>
                        <td className="px-3 py-2">
                          <span className="text-xs bg-gray-100 rounded px-2 py-0.5">
                            {r.customerSegment ?? '—'}
                          </span>
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right font-semibold text-blue-700">
                          {fmtCurrency(r.lifetimeRevenue)}
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right">
                          {fmtNum(r.lifetimeOrders)}
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right">
                          {fmtCurrency(r.avgOrderValue)}
                        </td>
                        <td className="px-3 py-2 text-xs text-gray-500">
                          {formatDate(r.firstOrderAt)}
                        </td>
                        <td className="px-3 py-2 text-xs text-gray-500">
                          {formatDate(r.lastOrderAt)}
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right text-gray-500">
                          {Math.round(r.daysSinceLastOrder)}
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right text-purple-600">
                          {fmtNum(r.expressOrders)}
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right text-red-500">
                          {fmtNum(r.cancelledOrders)}
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right">
                          {fmtNum(r.activePackages)}
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right text-amber-600">
                          {fmtNum(r.loyaltyPointsBalance)}
                        </td>
                        <td className="px-3 py-2 tabular-nums text-right text-emerald-600">
                          {fmtCurrency(r.walletBalance)}
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
