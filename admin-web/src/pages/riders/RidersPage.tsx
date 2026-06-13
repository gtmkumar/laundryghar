import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import {
  Loader2,
  Plus,
  Bike,
  Search,
  ArrowUp,
  ArrowDown,
  ArrowUpDown,
  Eye,
  Pencil,
  Check,
  Ban,
  ShieldCheck,
} from 'lucide-react'
import { cn } from '@/lib/utils'
import { ActionMenu, ActionMenuItem } from '@/components/ui/ActionMenu'
import { ConfirmDialog, useConfirm } from '@/components/shared/ConfirmDialog'
import { showToast } from '@/stores/toastStore'
import { useRidersInfinite, useVerifyRider, useRejectRider } from '@/hooks/useRiders'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { useAccessFranchises } from '@/hooks/useAccessControl'
import { usePermissions } from '@/hooks/usePermissions'
import type { RiderDto, RiderSortKey } from '@/types/api'
import { OnboardRiderDrawer } from './OnboardRiderDrawer'
import { RiderDetailDrawer } from './RiderDetailDrawer'
import { RiderEditDrawer } from './RiderEditDrawer'
import { RiderOpsView } from './RiderOpsView'
import { RiderCashView } from './RiderCashView'
import { VEHICLE_LABEL, KycBadge, StatusBadge, formatDate, humanise, isKycActionable } from './riderShared'

type RidersTab = 'roster' | 'live' | 'cash'

const KYC_FILTERS = ['pending', 'submitted', 'verified', 'rejected', 'expired']
const STATUS_FILTERS = ['active', 'suspended', 'terminated']

// Map a clickable column to its server sort key.
const SORT_COLUMNS: { key: RiderSortKey; label: string; align?: 'right' }[] = [
  { key: 'name', label: 'Rider' },
  { key: 'franchise', label: 'Franchise' },
  { key: 'kyc', label: 'KYC' },
  { key: 'status', label: 'Status' },
  { key: 'created', label: 'Joined', align: 'right' },
]

const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'

export function RidersPage() {
  const { hasPermission, isFranchiseScoped } = usePermissions()
  const canManage = hasPermission('rider.manage')

  // Roster (table) vs Live map (ops board), synced to ?view= for deep-linking.
  const [searchParams, setSearchParams] = useSearchParams()
  const viewParam = searchParams.get('view')
  const view: RidersTab = viewParam === 'live' ? 'live' : viewParam === 'cash' ? 'cash' : 'roster'
  const setView = (v: RidersTab) => {
    const next = new URLSearchParams(searchParams)
    if (v === 'roster') next.delete('view')
    else next.set('view', v)
    setSearchParams(next, { replace: true })
  }

  // ── Filters & sort state ──
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [kycStatus, setKycStatus] = useState('')
  const [status, setStatus] = useState('')
  const [franchiseId, setFranchiseId] = useState('')
  // Default sort = newest first, matching the backend default (-created).
  const [sort, setSort] = useState<string>('-created')

  // Debounce the search box → `search` (which is what hits the query key).
  useEffect(() => {
    const t = setTimeout(() => setSearch(searchInput.trim()), 350)
    return () => clearTimeout(t)
  }, [searchInput])

  // Brand/platform admins get the franchise dropdown + filter; franchise-scoped
  // users only ever see their own franchise, so the filter is hidden for them.
  const showFranchiseFilter = !isFranchiseScoped
  const franchisesQ = useAccessFranchises()
  const franchises = useMemo(
    () => (showFranchiseFilter ? franchisesQ.data?.pages.flatMap((p) => p.list) ?? [] : []),
    [showFranchiseFilter, franchisesQ.data],
  )

  const { data, isLoading, isError, hasNextPage, isFetchingNextPage, fetchNextPage } = useRidersInfinite({
    search: search || undefined,
    kycStatus: kycStatus || undefined,
    status: status || undefined,
    franchiseId: showFranchiseFilter ? franchiseId || undefined : undefined,
    sort,
  })
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  const riders = data?.pages.flatMap((p) => p.list) ?? []
  const total = data?.pages[0]?.totalCount
  const hasFilters = !!(search || kycStatus || status || franchiseId)

  // ── Drawer state ──
  const [onboardOpen, setOnboardOpen] = useState(false)
  const [detailId, setDetailId] = useState<string | null>(null)
  const [editRider, setEditRider] = useState<RiderDto | null>(null)

  // Deep link: `/riders?rider=<id>` opens that rider's detail once, then drops
  // the param so closing the drawer doesn't immediately reopen it. (searchParams
  // is declared at the top of the component for the view tab.)
  useEffect(() => {
    const rid = searchParams.get('rider')
    if (!rid) return
    setDetailId(rid)
    const next = new URLSearchParams(searchParams)
    next.delete('rider')
    setSearchParams(next, { replace: true })
  }, [searchParams, setSearchParams])

  // Toggle sort on a column: first click → ascending, second → descending.
  const toggleSort = (key: RiderSortKey) => {
    setSort((current) => (current === key ? `-${key}` : key))
  }

  return (
    <div className="space-y-5">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Riders</h1>
          <p className="text-sm text-gray-400">
            Delivery riders across your franchises{typeof total === 'number' ? ` · ${total} total` : ''}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Link
            to="/riders/verification"
            className="inline-flex items-center gap-1.5 rounded-xl border border-gray-200 px-4 py-2.5 text-sm font-semibold text-gray-700 hover:bg-gray-50"
          >
            <ShieldCheck className="h-4 w-4" /> Verification queue
          </Link>
          {canManage && (
            <button
              type="button"
              onClick={() => setOnboardOpen(true)}
              className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
            >
              <Plus className="h-4 w-4" /> Onboard rider
            </button>
          )}
        </div>
      </div>

      {/* View tabs — roster table vs live ops map */}
      <div className="flex w-fit items-center gap-1 rounded-xl border border-gray-200 bg-white p-1">
        {([
          { key: 'roster', label: 'Roster' },
          { key: 'live', label: 'Live map' },
          { key: 'cash', label: 'Cash' },
        ] as const).map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setView(t.key)}
            className={cn(
              'rounded-lg px-3.5 py-1.5 text-sm font-medium transition-colors',
              view === t.key ? 'bg-lg-green text-white' : 'text-gray-600 hover:bg-gray-50',
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      {view === 'live' ? (
        <RiderOpsView />
      ) : view === 'cash' ? (
        <RiderCashView />
      ) : (
      <>
      {/* Filter bar */}
      <div className="flex flex-wrap items-center gap-3 rounded-2xl border border-gray-200 bg-white px-4 py-3">
        <div className="relative min-w-[14rem] flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-gray-400" />
          <input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            className={cn(inputCls, 'pl-9')}
            placeholder="Search name, email, phone or code…"
          />
        </div>
        {showFranchiseFilter && (
          <select
            value={franchiseId}
            onChange={(e) => setFranchiseId(e.target.value)}
            className={cn(inputCls, 'w-auto min-w-[10rem]')}
            disabled={franchisesQ.isLoading}
          >
            <option value="">All franchises</option>
            {franchises.map((f) => (
              <option key={f.id} value={f.id}>{f.name}</option>
            ))}
          </select>
        )}
        <select value={kycStatus} onChange={(e) => setKycStatus(e.target.value)} className={cn(inputCls, 'w-auto min-w-[9rem]')}>
          <option value="">All KYC</option>
          {KYC_FILTERS.map((s) => (
            <option key={s} value={s} className="capitalize">{humanise(s)}</option>
          ))}
        </select>
        <select value={status} onChange={(e) => setStatus(e.target.value)} className={cn(inputCls, 'w-auto min-w-[9rem]')}>
          <option value="">All statuses</option>
          {STATUS_FILTERS.map((s) => (
            <option key={s} value={s} className="capitalize">{humanise(s)}</option>
          ))}
        </select>
      </div>

      {/* Body */}
      {isLoading ? (
        <div className="flex items-center justify-center py-24 text-gray-400">
          <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading riders…
        </div>
      ) : isError ? (
        <div className="py-24 text-center text-sm text-red-600">Couldn’t load riders.</div>
      ) : riders.length === 0 ? (
        hasFilters ? (
          <div className="rounded-2xl border border-dashed border-gray-200 bg-white py-20 text-center text-sm text-gray-400">
            No riders match these filters.
          </div>
        ) : (
          <EmptyState canManage={canManage} onOnboard={() => setOnboardOpen(true)} />
        )
      ) : (
        <>
          <div className="overflow-hidden rounded-2xl border border-gray-200 bg-white">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-100 text-left text-[11px] font-semibold uppercase tracking-wide text-gray-400">
                  <SortableTh col={SORT_COLUMNS[0]} sort={sort} onSort={toggleSort} />
                  <SortableTh col={SORT_COLUMNS[1]} sort={sort} onSort={toggleSort} />
                  <th className="px-5 py-3">Vehicle</th>
                  <SortableTh col={SORT_COLUMNS[2]} sort={sort} onSort={toggleSort} />
                  <SortableTh col={SORT_COLUMNS[3]} sort={sort} onSort={toggleSort} />
                  <SortableTh col={SORT_COLUMNS[4]} sort={sort} onSort={toggleSort} />
                  <th className="px-5 py-3 text-right">Actions</th>
                </tr>
              </thead>
              <tbody>
                {riders.map((r) => (
                  <RiderRow
                    key={r.id}
                    rider={r}
                    canManage={canManage}
                    onView={() => setDetailId(r.id)}
                    onEdit={() => setEditRider(r)}
                  />
                ))}
              </tbody>
            </table>
          </div>

          <div ref={sentinelRef} className="h-1" />
          {isFetchingNextPage && (
            <div className="flex items-center justify-center py-4 text-gray-400">
              <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more…
            </div>
          )}
        </>
      )}

      </>
      )}

      <OnboardRiderDrawer open={onboardOpen} onClose={() => setOnboardOpen(false)} />
      <RiderDetailDrawer
        riderId={detailId}
        open={detailId !== null}
        onClose={() => setDetailId(null)}
        onEdit={(rider) => {
          setDetailId(null)
          setEditRider(rider)
        }}
      />
      <RiderEditDrawer rider={editRider} open={editRider !== null} onClose={() => setEditRider(null)} />
    </div>
  )
}

function SortableTh({
  col,
  sort,
  onSort,
}: {
  col: { key: RiderSortKey; label: string; align?: 'right' }
  sort: string
  onSort: (key: RiderSortKey) => void
}) {
  const active = sort === col.key || sort === `-${col.key}`
  const desc = sort === `-${col.key}`
  const Icon = !active ? ArrowUpDown : desc ? ArrowDown : ArrowUp
  return (
    <th className={cn('px-5 py-3', col.align === 'right' && 'text-right')}>
      <button
        type="button"
        onClick={() => onSort(col.key)}
        className={cn(
          'inline-flex items-center gap-1 uppercase tracking-wide hover:text-gray-600',
          col.align === 'right' && 'flex-row-reverse',
          active ? 'text-gray-700' : 'text-gray-400',
        )}
      >
        {col.label}
        <Icon className={cn('h-3 w-3', !active && 'opacity-50')} />
      </button>
    </th>
  )
}

function RiderRow({
  rider,
  canManage,
  onView,
  onEdit,
}: {
  rider: RiderDto
  canManage: boolean
  onView: () => void
  onEdit: () => void
}) {
  const name = rider.riderName ?? rider.email ?? rider.riderCode
  const contact = rider.email ?? rider.phone
  const vehicle = VEHICLE_LABEL[rider.vehicleType] ?? humanise(rider.vehicleType)

  return (
    <tr className="border-b border-gray-50 last:border-0 hover:bg-gray-50/60">
      <td className="px-5 py-3">
        <button type="button" onClick={onView} className="flex items-center gap-3 text-left">
          <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-lg-green/12 text-lg-green">
            <Bike className="h-4 w-4" />
          </span>
          <div className="min-w-0">
            <p className="truncate font-medium text-gray-900">{name}</p>
            <p className="truncate text-xs text-gray-400">{contact ?? rider.riderCode}</p>
          </div>
        </button>
      </td>
      <td className="px-5 py-3 text-gray-600">{rider.franchiseName ?? '—'}</td>
      <td className="px-5 py-3 text-gray-600">
        <span>{vehicle}</span>
        {rider.vehicleNumber && <span className="block text-xs text-gray-400">{rider.vehicleNumber}</span>}
      </td>
      <td className="px-5 py-3">
        <KycBadge status={rider.kycStatus} />
      </td>
      <td className="px-5 py-3">
        <StatusBadge status={rider.status} />
      </td>
      <td className="px-5 py-3 text-right text-gray-400">{formatDate(rider.createdAt)}</td>
      <td className="px-5 py-3 text-right">
        <RowActions rider={rider} canManage={canManage} onView={onView} onEdit={onEdit} />
      </td>
    </tr>
  )
}

function RowActions({
  rider,
  canManage,
  onView,
  onEdit,
}: {
  rider: RiderDto
  canManage: boolean
  onView: () => void
  onEdit: () => void
}) {
  const { hasPermission } = usePermissions()
  const verify = useVerifyRider()
  const reject = useRejectRider()
  const gate = useConfirm()

  const canVerify = hasPermission('rider.verify')
  const showKyc = canVerify && isKycActionable(rider.kycStatus)
  const busy = verify.isPending || reject.isPending
  const riderLabel = rider.riderName ?? rider.riderCode ?? 'this rider'

  const approve = () =>
    gate.confirm({
      title: 'Approve KYC?',
      description: `Mark ${riderLabel}'s KYC as verified. They can be assigned to deliveries.`,
      confirmLabel: 'Approve',
      tone: 'default',
      onConfirm: async () => {
        try {
          await verify.mutateAsync(rider.id)
          showToast('success', 'KYC approved.')
        } catch (e) {
          showToast('error', e instanceof Error ? e.message : 'Could not approve KYC.')
        }
      },
    })

  const doReject = () =>
    gate.confirm({
      title: 'Reject KYC?',
      description: `Reject ${riderLabel}'s KYC. Provide a reason so the rider can correct and resubmit.`,
      confirmLabel: 'Reject',
      tone: 'danger',
      reasonOptional: true,
      reasonLabel: 'Reason for rejection',
      reasonPlaceholder: 'e.g. DL photo unreadable',
      onConfirm: async (reason) => {
        try {
          await reject.mutateAsync({ id: rider.id, reason: reason?.trim() || undefined })
          showToast('success', 'KYC rejected.')
        } catch (e) {
          showToast('error', e instanceof Error ? e.message : 'Could not reject KYC.')
        }
      },
    })

  return (
    <>
      <ActionMenu busy={busy} label="Rider actions" width={176}>
        {(close) => (
          <>
            <ActionMenuItem icon={Eye} onClick={() => { close(); onView() }}>View</ActionMenuItem>
            {canManage && (
              <ActionMenuItem icon={Pencil} onClick={() => { close(); onEdit() }}>Edit</ActionMenuItem>
            )}
            {showKyc && (
              <>
                <div className="my-1 border-t border-gray-100" />
                <ActionMenuItem icon={Check} onClick={() => { close(); approve() }} className="text-emerald-700">Approve</ActionMenuItem>
                <ActionMenuItem icon={Ban} onClick={() => { close(); doReject() }} className="text-rose-700">Reject</ActionMenuItem>
              </>
            )}
          </>
        )}
      </ActionMenu>
      <ConfirmDialog {...gate.dialogProps} />
    </>
  )
}

function EmptyState({ canManage, onOnboard }: { canManage: boolean; onOnboard: () => void }) {
  return (
    <div className="flex flex-col items-center justify-center rounded-2xl border border-dashed border-gray-200 bg-white py-20 text-center">
      <span className="mb-3 flex h-12 w-12 items-center justify-center rounded-2xl bg-lg-green/10 text-lg-green">
        <Bike className="h-6 w-6" />
      </span>
      <p className="text-sm font-medium text-gray-900">No riders yet</p>
      <p className="mt-1 max-w-xs text-sm text-gray-400">
        Onboard a delivery rider for one of your franchises to get started.
      </p>
      {canManage && (
        <button
          type="button"
          onClick={onOnboard}
          className="mt-4 inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
        >
          <Plus className="h-4 w-4" /> Onboard rider
        </button>
      )}
    </div>
  )
}
