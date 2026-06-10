import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Plus } from 'lucide-react'
import { useSubscriptionPlans, useCustomerSubscriptions } from '@/hooks/useSubscriptions'
import { usePermissions } from '@/hooks/usePermissions'
import {
  SubscriptionPlanDrawer,
  SubscriptionPlanDetailDrawer,
  CustomerSubscriptionDetailDrawer,
  PlanStatusBadge,
  SubscriptionStatusBadge,
  SUBSCRIPTION_STATUSES,
} from './SubscriptionDrawers'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Card } from '@/components/ui/card'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable, type FilterDef } from '@/components/shared/FilterableTable'
import type { SubscriptionPlanDto, CustomerSubscriptionDto } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

const PLAN_STATUSES = ['draft', 'active', 'paused', 'retired']
const TIERS = ['basic', 'standard', 'premium', 'custom']

type Tab = 'plans' | 'customers'

// ── Plans tab ───────────────────────────────────────────────────────────────────

function PlansTab({ canManage }: { canManage: boolean }) {
  const { data, isLoading, isError, error, refetch } = useSubscriptionPlans({ pageSize: 100 })
  const plans = useMemo(() => data?.list ?? [], [data])

  const [createOpen, setCreateOpen] = useState(false)
  const [editPlan, setEditPlan] = useState<SubscriptionPlanDto | null>(null)
  const [detailPlan, setDetailPlan] = useState<SubscriptionPlanDto | null>(null)

  const columns: Column<SubscriptionPlanDto>[] = [
    {
      header: 'Code',
      accessor: 'code',
      className: 'font-mono text-xs w-32',
      sortKey: 'code',
      sortAccessor: (r) => r.code,
    },
    {
      header: 'Name',
      accessor: (r) => <span className="font-medium text-gray-900">{r.name}</span>,
      sortKey: 'name',
      sortAccessor: (r) => r.name,
    },
    {
      header: 'Tier',
      accessor: (r) => <span className="capitalize">{r.tier}</span>,
      sortKey: 'tier',
      sortAccessor: (r) => r.tier,
    },
    {
      header: 'Price',
      accessor: (r) => (
        <span className="tabular-nums">
          {formatCurrency(r.price)}
          <span className="text-gray-400"> / {r.intervalCount > 1 ? `${r.intervalCount} ` : ''}{r.billingInterval.replace(/_/g, ' ')}</span>
        </span>
      ),
      className: 'text-right',
      sortKey: 'price',
      sortAccessor: (r) => r.price,
    },
    {
      header: 'Subscribers',
      accessor: (r) => <span className="tabular-nums">{r.currentSubscriberCount}</span>,
      className: 'text-right',
      sortKey: 'subscribers',
      sortAccessor: (r) => r.currentSubscriberCount,
    },
    {
      header: 'Status',
      accessor: (r) => <PlanStatusBadge status={r.status} />,
      sortKey: 'status',
      sortAccessor: (r) => r.status,
    },
  ]

  const filters: FilterDef<SubscriptionPlanDto>[] = [
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (r) => r.status,
      options: PLAN_STATUSES.map((s) => ({ value: s, label: s })),
    },
    {
      key: 'tier',
      allLabel: 'All tiers',
      value: (r) => r.tier,
      options: TIERS.map((t) => ({ value: t, label: t })),
    },
  ]

  return (
    <>
      {canManage && (
        <div className="mb-4 flex justify-end">
          <button
            type="button"
            onClick={() => setCreateOpen(true)}
            className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
          >
            <Plus className="h-4 w-4" /> New plan
          </button>
        </div>
      )}
      <Card className="overflow-hidden">
        {isLoading ? (
          <LoadingState message="Loading subscription plans…" />
        ) : isError ? (
          isForbiddenError(error) ? <ForbiddenState /> : <ErrorState error={error as Error} onRetry={() => void refetch()} />
        ) : (
          <FilterableTable
            columns={columns}
            data={plans}
            keyFn={(r) => r.id}
            unit="plan"
            totalCount={data?.totalCount}
            searchPlaceholder="Search code or name…"
            searchAccessor={(r) => `${r.code} ${r.name}`}
            filters={filters}
            initialSort={{ key: 'name', dir: 'asc' }}
            emptyMessage="No subscription plans yet."
            noMatchMessage="No plans match your filters."
            onRowClick={(r) => setDetailPlan(r)}
          />
        )}
      </Card>

      <SubscriptionPlanDrawer open={createOpen} plan={null} onClose={() => setCreateOpen(false)} />
      <SubscriptionPlanDrawer open={!!editPlan} plan={editPlan} onClose={() => setEditPlan(null)} />
      <SubscriptionPlanDetailDrawer
        plan={detailPlan}
        onClose={() => setDetailPlan(null)}
        onEdit={(p) => {
          setDetailPlan(null)
          setEditPlan(p)
        }}
        canManage={canManage}
      />
    </>
  )
}

// ── Customer subscriptions tab ──────────────────────────────────────────────────

function CustomerSubscriptionsTab() {
  const { data, isLoading, isError, error, refetch } = useCustomerSubscriptions({ pageSize: 100 })
  const subs = useMemo(() => data?.list ?? [], [data])
  const [detail, setDetail] = useState<CustomerSubscriptionDto | null>(null)

  const columns: Column<CustomerSubscriptionDto>[] = [
    {
      header: 'Number',
      accessor: 'subscriptionNumber',
      className: 'font-mono text-xs w-44',
      sortKey: 'number',
      sortAccessor: (r) => r.subscriptionNumber,
    },
    {
      header: 'Customer',
      accessor: (r) => <span className="font-mono text-xs text-gray-500">{r.customerId.slice(0, 8)}…</span>,
      sortKey: 'customer',
      sortAccessor: (r) => r.customerId,
    },
    {
      header: 'Price',
      accessor: (r) => (
        <span className="tabular-nums">
          {formatCurrency(r.priceSnapshot)}
          <span className="text-gray-400"> / {r.billingInterval.replace(/_/g, ' ')}</span>
        </span>
      ),
      className: 'text-right',
      sortKey: 'price',
      sortAccessor: (r) => r.priceSnapshot,
    },
    {
      header: 'Current period',
      accessor: (r) =>
        r.currentPeriodEnd ? (
          <span className="text-xs text-gray-600">
            {r.currentPeriodStart ? formatDate(r.currentPeriodStart) : '—'} – {formatDate(r.currentPeriodEnd)}
          </span>
        ) : (
          <span className="text-gray-400">—</span>
        ),
      sortKey: 'period',
      sortAccessor: (r) => r.currentPeriodEnd ?? '',
    },
    {
      header: 'Next billing',
      accessor: (r) => (r.nextBillingAt ? formatDate(r.nextBillingAt) : <span className="text-gray-400">—</span>),
      sortKey: 'next',
      sortAccessor: (r) => r.nextBillingAt ?? '',
    },
    {
      header: 'Status',
      accessor: (r) => <SubscriptionStatusBadge status={r.status} />,
      sortKey: 'status',
      sortAccessor: (r) => r.status,
    },
  ]

  const filters: FilterDef<CustomerSubscriptionDto>[] = [
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (r) => r.status,
      options: SUBSCRIPTION_STATUSES.map((s) => ({ value: s, label: s.replace(/_/g, ' ') })),
    },
  ]

  return (
    <>
      <Card className="overflow-hidden">
        {isLoading ? (
          <LoadingState message="Loading customer subscriptions…" />
        ) : isError ? (
          isForbiddenError(error) ? <ForbiddenState /> : <ErrorState error={error as Error} onRetry={() => void refetch()} />
        ) : (
          <FilterableTable
            columns={columns}
            data={subs}
            keyFn={(r) => r.id}
            unit="subscription"
            totalCount={data?.totalCount}
            searchPlaceholder="Search subscription number…"
            searchAccessor={(r) => `${r.subscriptionNumber} ${r.customerId}`}
            filters={filters}
            initialSort={{ key: 'next', dir: 'asc' }}
            emptyMessage="No customer subscriptions yet."
            noMatchMessage="No subscriptions match your filters."
            onRowClick={(r) => setDetail(r)}
          />
        )}
      </Card>

      <CustomerSubscriptionDetailDrawer subscription={detail} onClose={() => setDetail(null)} />
    </>
  )
}

// ── Page ────────────────────────────────────────────────────────────────────────

export function SubscriptionsPage() {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('subscription.manage')

  const [searchParams, setSearchParams] = useSearchParams()
  const tab = (searchParams.get('view') as Tab) || 'plans'

  const setTab = (next: Tab) => {
    setSearchParams((prev) => {
      const p = new URLSearchParams(prev)
      p.set('view', next)
      return p
    })
  }

  const tabs: { key: Tab; label: string }[] = [
    { key: 'plans', label: 'Plans' },
    { key: 'customers', label: 'Customer subscriptions' },
  ]

  return (
    <div>
      <PageHeader
        title="Subscriptions"
        description="Recurring plans customers can subscribe to, and the active subscriptions against them."
      />

      <div className="mb-5 flex gap-1 border-b border-gray-200">
        {tabs.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setTab(t.key)}
            className={`-mb-px border-b-2 px-4 py-2.5 text-sm font-medium transition-colors ${
              tab === t.key
                ? 'border-lg-green text-lg-green'
                : 'border-transparent text-gray-500 hover:text-gray-700'
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'plans' ? <PlansTab canManage={canManage} /> : <CustomerSubscriptionsTab />}
    </div>
  )
}
