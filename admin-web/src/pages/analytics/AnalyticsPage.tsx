import { useState } from 'react'
import { PageHeader } from '@/components/shared/PageHeader'
import { Button } from '@/components/ui/button'
import { useRefreshAnalytics } from '@/hooks/useAnalytics'
import { AnalyticsDashboardTab } from './AnalyticsDashboardTab'
import { DailyRevenueTab } from './DailyRevenueTab'
import { MonthlyRevenueTab } from './MonthlyRevenueTab'
import { WarehouseThroughputTab } from './WarehouseThroughputTab'
import { CustomerLtvTab } from './CustomerLtvTab'
import { RiderPerformanceTab } from './RiderPerformanceTab'
import { cn } from '@/lib/utils'

type Tab =
  | 'dashboard'
  | 'daily-revenue'
  | 'monthly-revenue'
  | 'warehouse'
  | 'customer-ltv'
  | 'rider-perf'

const TABS: { id: Tab; label: string }[] = [
  { id: 'dashboard', label: 'Overview' },
  { id: 'daily-revenue', label: 'Daily Revenue' },
  { id: 'monthly-revenue', label: 'Monthly Revenue' },
  { id: 'warehouse', label: 'Warehouse' },
  { id: 'customer-ltv', label: 'Customer LTV' },
  { id: 'rider-perf', label: 'Rider Performance' },
]

export function AnalyticsPage() {
  const [activeTab, setActiveTab] = useState<Tab>('dashboard')
  const refreshMutation = useRefreshAnalytics()
  const [refreshMsg, setRefreshMsg] = useState<string | null>(null)

  async function handleRefresh() {
    setRefreshMsg(null)
    try {
      const results = await refreshMutation.mutateAsync()
      const failed = results.filter((r) => !r.success)
      setRefreshMsg(
        failed.length === 0
          ? 'All materialized views refreshed successfully.'
          : `${results.length - failed.length}/${results.length} views refreshed. Failures: ${failed.map((r) => r.view).join(', ')}`,
      )
    } catch {
      setRefreshMsg('Refresh failed. Check server logs.')
    }
  }

  return (
    <div>
      <PageHeader
        title="Analytics"
        description="Materialized-view reports across revenue, warehouse, customers, and riders."
        action={
          <Button
            size="sm"
            variant="outline"
            onClick={() => void handleRefresh()}
            disabled={refreshMutation.isPending}
          >
            {refreshMutation.isPending ? 'Refreshing…' : 'Refresh data'}
          </Button>
        }
      />

      {refreshMsg && (
        <div
          className={cn(
            'mb-4 rounded-md px-4 py-2 text-sm',
            refreshMsg.includes('successfully')
              ? 'bg-green-50 text-green-700 border border-green-200'
              : 'bg-yellow-50 text-yellow-700 border border-yellow-200',
          )}
        >
          {refreshMsg}
        </div>
      )}

      {/* Tab bar */}
      <div className="border-b border-gray-200 mb-6">
        <nav className="-mb-px flex gap-1 overflow-x-auto">
          {TABS.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={cn(
                'whitespace-nowrap px-4 py-2 text-sm font-medium border-b-2 transition-colors',
                activeTab === tab.id
                  ? 'border-blue-600 text-blue-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300',
              )}
            >
              {tab.label}
            </button>
          ))}
        </nav>
      </div>

      {activeTab === 'dashboard' && <AnalyticsDashboardTab />}
      {activeTab === 'daily-revenue' && <DailyRevenueTab />}
      {activeTab === 'monthly-revenue' && <MonthlyRevenueTab />}
      {activeTab === 'warehouse' && <WarehouseThroughputTab />}
      {activeTab === 'customer-ltv' && <CustomerLtvTab />}
      {activeTab === 'rider-perf' && <RiderPerformanceTab />}
    </div>
  )
}
