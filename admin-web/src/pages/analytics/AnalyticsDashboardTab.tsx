import { useAnalyticsDashboard } from '@/hooks/useAnalytics'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { ShoppingCart, DollarSign, Users, TrendingUp } from 'lucide-react'

function StatCard({
  label,
  value,
  sub,
  icon: Icon,
  color,
}: {
  label: string
  value: string
  sub?: string
  icon: React.ElementType
  color: string
}) {
  return (
    <Card>
      <CardHeader className="pb-2">
        <div className="flex items-center justify-between">
          <CardTitle className="text-sm font-medium text-gray-500">{label}</CardTitle>
          <Icon className={`h-4 w-4 ${color}`} />
        </div>
      </CardHeader>
      <CardContent>
        <p className="text-2xl font-bold text-gray-900">{value}</p>
        {sub && <p className="text-xs text-gray-400 mt-0.5">{sub}</p>}
      </CardContent>
    </Card>
  )
}

function fmt(n: number) {
  return n.toLocaleString('en-IN', { maximumFractionDigits: 0 })
}

function fmtCurrency(n: number) {
  return `₹${n.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

export function AnalyticsDashboardTab() {
  const { data, isLoading, isError, error, refetch } = useAnalyticsDashboard()

  if (isLoading) return <LoadingState message="Loading dashboard..." />
  if (isError) return isForbiddenError(error) ? <ForbiddenState /> : <ErrorState error={error as Error} onRetry={() => void refetch()} />
  if (!data) return null

  const { today, thisMonth, topCustomersByLtv } = data

  return (
    <div className="space-y-6">
      {/* Today */}
      <section>
        <h2 className="text-sm font-semibold text-gray-500 uppercase tracking-wide mb-3">Today</h2>
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <StatCard
            label="Orders"
            value={fmt(today.ordersCount)}
            icon={ShoppingCart}
            color="text-blue-500"
          />
          <StatCard
            label="Gross Revenue"
            value={fmtCurrency(today.grossRevenue)}
            icon={DollarSign}
            color="text-green-500"
          />
          <StatCard
            label="Collected"
            value={fmtCurrency(today.collectedAmount)}
            icon={TrendingUp}
            color="text-emerald-500"
          />
          <StatCard
            label="Unique Customers"
            value={fmt(today.uniqueCustomers)}
            icon={Users}
            color="text-purple-500"
          />
        </div>
      </section>

      {/* This Month */}
      <section>
        <h2 className="text-sm font-semibold text-gray-500 uppercase tracking-wide mb-3">
          This Month
        </h2>
        <div className="grid grid-cols-2 lg:grid-cols-3 gap-4">
          <StatCard
            label="Orders"
            value={fmt(thisMonth.ordersCount)}
            icon={ShoppingCart}
            color="text-blue-500"
          />
          <StatCard
            label="Gross Revenue"
            value={fmtCurrency(thisMonth.grossRevenue)}
            icon={DollarSign}
            color="text-green-500"
          />
          <StatCard
            label="Net Revenue"
            value={fmtCurrency(thisMonth.netRevenue)}
            sub="After refunds &amp; discounts"
            icon={TrendingUp}
            color="text-emerald-500"
          />
        </div>
      </section>

      {/* Top Customers */}
      <section>
        <h2 className="text-sm font-semibold text-gray-500 uppercase tracking-wide mb-3">
          Top 5 Customers by Lifetime Value
        </h2>
        <Card>
          <div className="overflow-x-auto">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-gray-100 bg-gray-50">
                  <th className="px-4 py-2 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide">
                    #
                  </th>
                  <th className="px-4 py-2 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide">
                    Customer ID
                  </th>
                  <th className="px-4 py-2 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide">
                    Segment
                  </th>
                  <th className="px-4 py-2 text-right text-xs font-semibold text-gray-500 uppercase tracking-wide">
                    Lifetime Revenue
                  </th>
                  <th className="px-4 py-2 text-right text-xs font-semibold text-gray-500 uppercase tracking-wide">
                    Orders
                  </th>
                </tr>
              </thead>
              <tbody>
                {topCustomersByLtv.length === 0 ? (
                  <tr>
                    <td colSpan={5} className="px-4 py-6 text-center text-gray-400">
                      No data yet.
                    </td>
                  </tr>
                ) : (
                  topCustomersByLtv.map((c, i) => (
                    <tr key={c.customerId} className="border-b border-gray-50 hover:bg-gray-50">
                      <td className="px-4 py-2 text-gray-400 tabular-nums">{i + 1}</td>
                      <td className="px-4 py-2 font-mono text-xs text-gray-600 truncate max-w-[180px]">
                        {c.customerId}
                      </td>
                      <td className="px-4 py-2">
                        <span className="text-xs bg-gray-100 rounded px-2 py-0.5">
                          {c.customerSegment ?? '—'}
                        </span>
                      </td>
                      <td className="px-4 py-2 text-right tabular-nums font-medium">
                        {fmtCurrency(c.lifetimeRevenue)}
                      </td>
                      <td className="px-4 py-2 text-right tabular-nums text-gray-600">
                        {fmt(c.lifetimeOrders)}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </Card>
      </section>
    </div>
  )
}
