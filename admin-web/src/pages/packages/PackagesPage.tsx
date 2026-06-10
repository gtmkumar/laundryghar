import { useMemo, useState } from 'react'
import { Plus, Star } from 'lucide-react'
import { usePackages } from '@/hooks/useCommerce'
import { usePermissions } from '@/hooks/usePermissions'
import { PackageEditDrawer, PackageViewDrawer, ArchivePackageDrawer, TIERS } from './PackageDrawers'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable, type FilterDef } from '@/components/shared/FilterableTable'
import type { PackageDto } from '@/types/api'
import { formatCurrency } from '@/lib/utils'

function PackageStatusBadge({ status }: { status: string }) {
  const variant =
    status === 'active'
      ? 'success'
      : status === 'archived' || status === 'disabled'
        ? 'destructive'
        : 'secondary'
  return (
    <Badge variant={variant} className="capitalize">
      {status.replace(/_/g, ' ')}
    </Badge>
  )
}

function validityText(p: PackageDto): string {
  if (p.isUnlimitedValidity) return 'Unlimited'
  return p.validityDays != null ? `${p.validityDays}d` : '—'
}

export function PackagesPage() {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('packages.manage')
  const { data, isLoading, isError, error, refetch } = usePackages()

  const packages = useMemo(() => data?.list ?? [], [data])
  const total = data?.totalCount

  const [editing, setEditing] = useState<PackageDto | null>(null)
  const [creating, setCreating] = useState(false)
  const [viewing, setViewing] = useState<PackageDto | null>(null)
  const [archiving, setArchiving] = useState<PackageDto | null>(null)

  const columns: Column<PackageDto>[] = [
    {
      header: 'Name',
      accessor: (p) => (
        <span className="flex items-center gap-1.5">
          {p.isFeatured && <Star className="h-3.5 w-3.5 fill-amber-400 text-amber-400" />}
          {p.name}
        </span>
      ),
      sortKey: 'name',
      sortAccessor: (p) => p.name,
    },
    { header: 'Code', accessor: 'code', className: 'font-mono text-xs', sortKey: 'code' },
    {
      header: 'Tier',
      accessor: (p) => <span className="capitalize">{p.tier}</span>,
      sortKey: 'tier',
      sortAccessor: (p) => p.tier,
    },
    {
      header: 'Price',
      accessor: (p) => <span className="tabular-nums">{formatCurrency(p.price)}</span>,
      className: 'text-right',
      sortKey: 'price',
      sortAccessor: (p) => p.price,
    },
    {
      header: 'Credit value',
      accessor: (p) => <span className="tabular-nums text-lg-green">{formatCurrency(p.creditValue)}</span>,
      className: 'text-right',
      sortKey: 'creditValue',
      sortAccessor: (p) => p.creditValue,
    },
    {
      header: 'Validity',
      accessor: (p) => validityText(p),
      className: 'text-right',
      sortKey: 'validity',
      sortAccessor: (p) => (p.isUnlimitedValidity ? Number.MAX_SAFE_INTEGER : p.validityDays ?? 0),
    },
    {
      header: 'Status',
      accessor: (p) => <PackageStatusBadge status={p.status} />,
      sortKey: 'status',
      sortAccessor: (p) => p.status,
    },
  ]

  const filters: FilterDef<PackageDto>[] = [
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (p) => p.status,
      options: [...new Set(packages.map((p) => p.status))].sort().map((s) => ({ value: s, label: s.replace(/_/g, ' ') })),
    },
    {
      key: 'tier',
      allLabel: 'All tiers',
      value: (p) => p.tier,
      options: TIERS.map((t) => ({ value: t.value, label: t.label })),
    },
  ]

  return (
    <div>
      <PageHeader
        title="Packages"
        description="Prepaid credit packages customers can buy for discounted wallet value."
        action={
          canManage ? (
            <button
              type="button"
              onClick={() => setCreating(true)}
              className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
            >
              <Plus className="h-4 w-4" /> New package
            </button>
          ) : undefined
        }
      />
      <Card className="overflow-hidden">
        {isLoading ? (
          <LoadingState message="Loading packages..." />
        ) : isError ? (
          isForbiddenError(error) ? <ForbiddenState /> : <ErrorState error={error as Error} onRetry={() => void refetch()} />
        ) : (
          <FilterableTable
            columns={columns}
            data={packages}
            keyFn={(p) => p.id}
            unit="package"
            totalCount={total}
            searchPlaceholder="Search name or code…"
            searchAccessor={(p) => `${p.name} ${p.code} ${p.tier}`}
            filters={filters}
            initialSort={{ key: 'price', dir: 'asc' }}
            onRowClick={(p) => setViewing(p)}
            emptyMessage="No packages yet."
            noMatchMessage="No packages match your filters."
          />
        )}
      </Card>

      <PackageEditDrawer open={creating} onClose={() => setCreating(false)} />
      <PackageEditDrawer open={!!editing} pkg={editing} onClose={() => setEditing(null)} />
      <PackageViewDrawer
        pkg={viewing}
        onClose={() => setViewing(null)}
        canManage={canManage}
        onEdit={(p) => { setViewing(null); setEditing(p) }}
        onArchive={(p) => { setViewing(null); setArchiving(p) }}
      />
      <ArchivePackageDrawer pkg={archiving} onClose={() => setArchiving(null)} />
    </div>
  )
}
