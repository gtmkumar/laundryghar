import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Plus } from 'lucide-react'
import { usePlatformPlans, useFranchiseSubscriptions } from '@/hooks/useFinance'
import { useFranchises } from '@/hooks/useTenancy'
import { usePermissions } from '@/hooks/usePermissions'
import { useBrandStore } from '@/stores/brandStore'
import {
  PlatformPlanDrawer,
  PlatformPlanDetailDrawer,
  AssignFranchisePlanDrawer,
  FranchiseSubscriptionDetailDrawer,
  PlatformPlanStatusBadge,
  FranchiseSubscriptionStatusBadge,
  FRANCHISE_SUB_STATUSES,
} from './PlatformPlanDrawers'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Card } from '@/components/ui/card'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable, type FilterDef } from '@/components/shared/FilterableTable'
import type { PlatformPlanDto, FranchiseSubscriptionDto } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

const PLAN_STATUSES = ['draft', 'active', 'retired']
const TIERS = ['starter', 'growth', 'pro', 'enterprise', 'custom']

type Tab = 'plans' | 'franchises'

// ── Plans tab ───────────────────────────────────────────────────────────────────

function PlatformPlansTab({ canManage }: { canManage: boolean }) {
  const { data, isLoading, isError, error, refetch } = usePlatformPlans({ pageSize: 100 })
  const plans = useMemo(() => data?.list ?? [], [data])

  const [createOpen, setCreateOpen] = useState(false)
  const [editPlan, setEditPlan] = useState<PlatformPlanDto | null>(null)
  const [detailPlan, setDetailPlan] = useState<PlatformPlanDto | null>(null)

  const columns: Column<PlatformPlanDto>[] = [
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
          <span className="text-gray-400"> / {r.billingInterval}</span>
        </span>
      ),
      className: 'text-right',
      sortKey: 'price',
      sortAccessor: (r) => r.price,
    },
    {
      header: 'Status',
      accessor: (r) => <PlatformPlanStatusBadge status={r.status} />,
      sortKey: 'status',
      sortAccessor: (r) => r.status,
    },
  ]

  const filters: FilterDef<PlatformPlanDto>[] = [
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
          <LoadingState message="Loading platform plans…" />
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
            emptyMessage="No platform plans yet."
            noMatchMessage="No plans match your filters."
            onRowClick={(r) => setDetailPlan(r)}
          />
        )}
      </Card>

      <PlatformPlanDrawer open={createOpen} plan={null} onClose={() => setCreateOpen(false)} />
      <PlatformPlanDrawer open={!!editPlan} plan={editPlan} onClose={() => setEditPlan(null)} />
      <PlatformPlanDetailDrawer
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

// ── Franchise subscriptions tab ─────────────────────────────────────────────────

function FranchiseSubscriptionsTab({ canManage }: { canManage: boolean }) {
  const { activeBrandId } = useBrandStore()
  const { data, isLoading, isError, error, refetch } = useFranchiseSubscriptions({ pageSize: 100 })
  const subs = useMemo(() => data?.list ?? [], [data])

  const plansQ = usePlatformPlans({ pageSize: 100 })
  const plans = useMemo(() => plansQ.data?.list ?? [], [plansQ.data])
  const planMap = useMemo(
    () => Object.fromEntries(plans.map((p) => [p.id, p.name])),
    [plans],
  )

  const franchisesQ = useFranchises({ brandId: activeBrandId ?? undefined, pageSize: 100 })
  const franchises = useMemo(() => franchisesQ.data?.list ?? [], [franchisesQ.data])
  const franchiseMap = useMemo(
    () => Object.fromEntries(franchises.map((f) => [f.id, `${f.legalName} (${f.code})`])),
    [franchises],
  )

  const [assignOpen, setAssignOpen] = useState(false)
  const [detail, setDetail] = useState<FranchiseSubscriptionDto | null>(null)

  const columns: Column<FranchiseSubscriptionDto>[] = [
    {
      header: 'Number',
      accessor: 'subscriptionNumber',
      className: 'font-mono text-xs w-44',
      sortKey: 'number',
      sortAccessor: (r) => r.subscriptionNumber,
    },
    {
      header: 'Franchise',
      accessor: (r) =>
        franchiseMap[r.franchiseId] ?? (
          <span className="font-mono text-xs text-gray-500">{r.franchiseId.slice(0, 8)}…</span>
        ),
      sortKey: 'franchise',
      sortAccessor: (r) => franchiseMap[r.franchiseId] ?? r.franchiseId,
    },
    {
      header: 'Plan',
      accessor: (r) =>
        planMap[r.platformPlanId] ?? (
          <span className="font-mono text-xs text-gray-500">{r.platformPlanId.slice(0, 8)}…</span>
        ),
      sortKey: 'plan',
      sortAccessor: (r) => planMap[r.platformPlanId] ?? r.platformPlanId,
    },
    {
      header: 'Price',
      accessor: (r) => (
        <span className="tabular-nums">
          {formatCurrency(r.priceSnapshot)}
          <span className="text-gray-400"> / {r.billingInterval}</span>
        </span>
      ),
      className: 'text-right',
      sortKey: 'price',
      sortAccessor: (r) => r.priceSnapshot,
    },
    {
      header: 'Next billing',
      accessor: (r) => (r.nextBillingAt ? formatDate(r.nextBillingAt) : <span className="text-gray-400">—</span>),
      sortKey: 'next',
      sortAccessor: (r) => r.nextBillingAt ?? '',
    },
    {
      header: 'Status',
      accessor: (r) => <FranchiseSubscriptionStatusBadge status={r.status} />,
      sortKey: 'status',
      sortAccessor: (r) => r.status,
    },
  ]

  const filters: FilterDef<FranchiseSubscriptionDto>[] = [
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (r) => r.status,
      options: FRANCHISE_SUB_STATUSES.map((s) => ({ value: s, label: s.replace(/_/g, ' ') })),
    },
  ]

  return (
    <>
      {canManage && (
        <div className="mb-4 flex justify-end">
          <button
            type="button"
            onClick={() => setAssignOpen(true)}
            className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
          >
            <Plus className="h-4 w-4" /> Assign plan
          </button>
        </div>
      )}
      <Card className="overflow-hidden">
        {isLoading ? (
          <LoadingState message="Loading franchise subscriptions…" />
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
            searchAccessor={(r) => `${r.subscriptionNumber} ${franchiseMap[r.franchiseId] ?? r.franchiseId}`}
            filters={filters}
            initialSort={{ key: 'next', dir: 'asc' }}
            emptyMessage="No franchise subscriptions yet."
            noMatchMessage="No subscriptions match your filters."
            onRowClick={(r) => setDetail(r)}
          />
        )}
      </Card>

      <AssignFranchisePlanDrawer open={assignOpen} plans={plans} onClose={() => setAssignOpen(false)} />
      <FranchiseSubscriptionDetailDrawer
        subscription={detail}
        franchiseLabel={detail ? franchiseMap[detail.franchiseId] ?? detail.franchiseId : ''}
        planLabel={detail ? planMap[detail.platformPlanId] ?? detail.platformPlanId : ''}
        onClose={() => setDetail(null)}
        canManage={canManage}
      />
    </>
  )
}

// ── Page ────────────────────────────────────────────────────────────────────────

export function PlatformPlansPage() {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('saas.manage')

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
    { key: 'plans', label: 'Platform plans' },
    { key: 'franchises', label: 'Franchise subscriptions' },
  ]

  return (
    <div>
      <PageHeader
        title="Platform plans"
        description="SaaS subscription tiers franchises pay for, and the active franchise subscriptions against them."
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

      {tab === 'plans' ? (
        <PlatformPlansTab canManage={canManage} />
      ) : (
        <FranchiseSubscriptionsTab canManage={canManage} />
      )}
    </div>
  )
}
