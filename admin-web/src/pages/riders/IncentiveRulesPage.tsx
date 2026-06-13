import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Plus, Pencil, Trash2, Bike } from 'lucide-react'
import { useIncentiveRules, useDeleteIncentiveRule } from '@/hooks/useIncentives'
import { usePermissions } from '@/hooks/usePermissions'
import { IncentiveRuleDrawer } from './IncentiveRuleDrawer'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { ActionMenu, ActionMenuItem } from '@/components/ui/ActionMenu'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable, type FilterDef } from '@/components/shared/FilterableTable'
import { ConfirmDialog, useConfirm } from '@/components/shared/ConfirmDialog'
import { showToast } from '@/stores/toastStore'
import type { IncentiveRuleDto, IncentiveRuleType } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

const RULE_TYPE_LABEL: Record<IncentiveRuleType, string> = {
  trips_target: 'Trips target',
  surge_bonus: 'Surge bonus',
}

export function IncentiveRulesPage() {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('rider.manage')

  const { data, isLoading, isError, error, refetch } = useIncentiveRules()
  const rules = useMemo(() => data ?? [], [data])

  const remove = useDeleteIncentiveRule()
  const gate = useConfirm()
  const [busyId, setBusyId] = useState<string | null>(null)

  const [drawerOpen, setDrawerOpen] = useState(false)
  const [editing, setEditing] = useState<IncentiveRuleDto | null>(null)

  const openCreate = () => {
    setEditing(null)
    setDrawerOpen(true)
  }
  const openEdit = (rule: IncentiveRuleDto) => {
    setEditing(rule)
    setDrawerOpen(true)
  }

  const confirmDelete = (rule: IncentiveRuleDto) =>
    gate.confirm({
      title: 'Delete incentive rule?',
      description: `Permanently delete "${rule.name}". Riders will no longer earn this incentive.`,
      confirmLabel: 'Delete',
      tone: 'danger',
      onConfirm: async () => {
        setBusyId(rule.id)
        try {
          await remove.mutateAsync(rule.id)
          showToast('success', 'Incentive rule deleted.')
        } catch (e) {
          showToast('error', e instanceof Error ? e.message : 'Could not delete the rule.')
        } finally {
          setBusyId(null)
        }
      },
    })

  const columns: Column<IncentiveRuleDto>[] = [
    {
      header: 'Name',
      accessor: (r) => <span className="font-medium text-gray-900">{r.name}</span>,
      sortKey: 'name',
      sortAccessor: (r) => r.name,
    },
    {
      header: 'Type',
      accessor: (r) => RULE_TYPE_LABEL[r.ruleType],
      sortKey: 'type',
      sortAccessor: (r) => r.ruleType,
    },
    {
      header: 'Threshold',
      accessor: (r) =>
        r.ruleType === 'trips_target' ? (
          <span className="tabular-nums">{r.threshold}</span>
        ) : (
          <span className="text-gray-400">—</span>
        ),
      className: 'text-right',
      sortKey: 'threshold',
      sortAccessor: (r) => r.threshold,
    },
    {
      header: 'Reward',
      accessor: (r) => <span className="tabular-nums">{formatCurrency(r.rewardAmount)}</span>,
      className: 'text-right',
      sortKey: 'reward',
      sortAccessor: (r) => r.rewardAmount,
    },
    {
      header: 'Valid until',
      accessor: (r) =>
        r.validUntil ? formatDate(r.validUntil) : <span className="text-gray-400">—</span>,
      sortKey: 'validUntil',
      sortAccessor: (r) => r.validUntil ?? '',
    },
    {
      header: 'Status',
      accessor: (r) => (
        <Badge variant={r.isActive ? 'success' : 'secondary'}>
          {r.isActive ? 'Active' : 'Inactive'}
        </Badge>
      ),
      sortKey: 'status',
      sortAccessor: (r) => (r.isActive ? 1 : 0),
    },
    ...(canManage
      ? [
          {
            header: '',
            className: 'w-12 text-right',
            accessor: (r: IncentiveRuleDto) => (
              <div onClick={(e) => e.stopPropagation()}>
                <ActionMenu busy={busyId === r.id} label="Rule actions" width={160}>
                  {(close) => (
                    <>
                      <ActionMenuItem
                        icon={Pencil}
                        onClick={() => {
                          close()
                          openEdit(r)
                        }}
                      >
                        Edit
                      </ActionMenuItem>
                      <ActionMenuItem
                        icon={Trash2}
                        danger
                        onClick={() => {
                          close()
                          confirmDelete(r)
                        }}
                      >
                        Delete
                      </ActionMenuItem>
                    </>
                  )}
                </ActionMenu>
              </div>
            ),
          } as Column<IncentiveRuleDto>,
        ]
      : []),
  ]

  const filters: FilterDef<IncentiveRuleDto>[] = [
    {
      key: 'type',
      allLabel: 'All types',
      value: (r) => r.ruleType,
      options: [
        { value: 'trips_target', label: 'Trips target' },
        { value: 'surge_bonus', label: 'Surge bonus' },
      ],
    },
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (r) => (r.isActive ? 'active' : 'inactive'),
      options: [
        { value: 'active', label: 'Active' },
        { value: 'inactive', label: 'Inactive' },
      ],
    },
  ]

  return (
    <div>
      <PageHeader
        title="Incentive rules"
        description="Reward riders for hitting delivery targets and working surge windows."
        action={
          <div className="flex items-center gap-2">
            <Link
              to="/riders"
              className="inline-flex items-center gap-1.5 rounded-xl border border-gray-200 px-4 py-2.5 text-sm font-semibold text-gray-700 hover:bg-gray-50"
            >
              <Bike className="h-4 w-4" /> Riders
            </Link>
            {canManage && (
              <button
                type="button"
                onClick={openCreate}
                className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
              >
                <Plus className="h-4 w-4" /> New rule
              </button>
            )}
          </div>
        }
      />

      <Card className="overflow-hidden">
        {isLoading ? (
          <LoadingState message="Loading incentive rules..." />
        ) : isError ? (
          isForbiddenError(error) ? (
            <ForbiddenState />
          ) : (
            <ErrorState error={error as Error} onRetry={() => void refetch()} />
          )
        ) : (
          <FilterableTable
            columns={columns}
            data={rules}
            keyFn={(r) => r.id}
            unit="rule"
            searchPlaceholder="Search rule name…"
            searchAccessor={(r) => r.name}
            filters={filters}
            initialSort={{ key: 'name', dir: 'asc' }}
            emptyMessage="No incentive rules yet."
            noMatchMessage="No rules match your filters."
          />
        )}
      </Card>

      <IncentiveRuleDrawer rule={editing} open={drawerOpen} onClose={() => setDrawerOpen(false)} />
      <ConfirmDialog {...gate.dialogProps} />
    </div>
  )
}
