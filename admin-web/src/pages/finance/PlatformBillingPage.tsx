import { Coins, TrendingUp, Building2, Receipt } from 'lucide-react'
import { usePlatformBillingSummary } from '@/hooks/useEntitlements'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { cn, formatCurrency } from '@/lib/utils'

function StatCard({
  label, value, sub, icon: Icon, color,
}: { label: string; value: string; sub?: string; icon: React.ElementType; color: string }) {
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
        {sub && <p className="mt-0.5 text-xs text-gray-400">{sub}</p>}
      </CardContent>
    </Card>
  )
}

const INVOICE_STATUS_TONE: Record<string, string> = {
  issued: 'bg-blue-50 text-blue-600',
  paid: 'bg-emerald-50 text-emerald-700',
  void: 'bg-gray-100 text-gray-500',
}

export function PlatformBillingPage() {
  const { data, isLoading, isError, error } = usePlatformBillingSummary()
  const cur = (n: number) => formatCurrency(n, data?.currency ?? 'INR')

  return (
    <div className="space-y-6">
      <PageHeader
        title="Platform billing"
        description="SaaS revenue the platform earns from brands on their tiers"
      />

      {isLoading && <LoadingState />}
      {isError && (isForbiddenError(error)
        ? <ForbiddenState message="You need saas.read to view platform billing." />
        : <ErrorState error={error as Error} />)}

      {data && (
        <>
          {/* Headline metrics */}
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard label="Monthly recurring revenue" value={cur(data.monthlyMrr)} sub="MRR across active tenants" icon={Coins} color="text-lg-green" />
            <StatCard label="Annual run-rate" value={cur(data.annualRunRate)} sub="MRR × 12" icon={TrendingUp} color="text-sky-500" />
            <StatCard label="Active tenants" value={data.activeTenants.toLocaleString('en-IN')} sub={data.cancelledTenants > 0 ? `${data.cancelledTenants} cancelled (churn)` : 'brands on a paid tier'} icon={Building2} color="text-violet-500" />
            <StatCard label="Outstanding" value={cur(data.outstandingAmount)} sub={`${cur(data.collectedAmount)} collected`} icon={Receipt} color="text-amber-500" />
          </div>

          {/* Revenue by tier */}
          <Card>
            <CardHeader>
              <CardTitle className="text-base font-semibold text-gray-900">Revenue by tier</CardTitle>
            </CardHeader>
            <CardContent>
              {data.byTier.length === 0 ? (
                <p className="py-6 text-center text-sm text-gray-400">No active paid tenants yet.</p>
              ) : (
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-gray-100 text-left text-xs font-medium uppercase text-gray-500">
                      <th className="py-2">Tier</th>
                      <th className="py-2 text-right">Active tenants</th>
                      <th className="py-2 text-right">Monthly MRR</th>
                      <th className="py-2 text-right">Share</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.byTier.map((t) => (
                      <tr key={t.bundleCode} className="border-b border-gray-50 last:border-0">
                        <td className="py-2.5 font-medium text-gray-800">{t.planName}
                          <span className="ml-1.5 text-xs text-gray-400">{t.bundleCode}</span>
                        </td>
                        <td className="py-2.5 text-right tabular-nums text-gray-600">{t.activeCount}</td>
                        <td className="py-2.5 text-right font-medium tabular-nums text-gray-900">{cur(t.monthlyMrr)}</td>
                        <td className="py-2.5 text-right tabular-nums text-gray-500">
                          {data.monthlyMrr > 0 ? `${Math.round((t.monthlyMrr / data.monthlyMrr) * 100)}%` : '—'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </CardContent>
          </Card>

          {/* Invoices by status */}
          <Card>
            <CardHeader>
              <CardTitle className="text-base font-semibold text-gray-900">Invoices by status</CardTitle>
            </CardHeader>
            <CardContent>
              {data.invoicesByStatus.length === 0 ? (
                <p className="py-6 text-center text-sm text-gray-400">No invoices issued yet.</p>
              ) : (
                <div className="flex flex-wrap gap-3">
                  {data.invoicesByStatus.map((s) => (
                    <div key={s.status} className="min-w-40 flex-1 rounded-xl border border-gray-100 px-4 py-3">
                      <span className={cn('inline-block rounded-full px-2 py-0.5 text-xs font-medium capitalize', INVOICE_STATUS_TONE[s.status] ?? 'bg-gray-100 text-gray-500')}>
                        {s.status}
                      </span>
                      <p className="mt-1.5 text-lg font-bold text-gray-900">{cur(s.totalAmount)}</p>
                      <p className="text-xs text-gray-400">{s.count} invoice{s.count === 1 ? '' : 's'}</p>
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </>
      )}
    </div>
  )
}
