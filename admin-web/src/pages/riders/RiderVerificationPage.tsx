import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Loader2, Search, ShieldCheck, FileText } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useRidersInfinite } from '@/hooks/useRiders'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { useAccessFranchises } from '@/hooks/useAccessControl'
import { usePermissions } from '@/hooks/usePermissions'
import type { RiderDto } from '@/types/api'
import { RiderVerificationDrawer } from './RiderVerificationDrawer'
import {
  KycBadge,
  VehicleBadge,
  formatDate,
  needsVerification,
} from './riderShared'

type QueueFilter = 'queue' | 'all'

const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'

export function RiderVerificationPage() {
  const { isFranchiseScoped } = usePermissions()

  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [franchiseId, setFranchiseId] = useState('')
  // Default to the review queue; 'all' shows every rider regardless of status.
  const [scope, setScope] = useState<QueueFilter>('queue')

  useEffect(() => {
    const t = setTimeout(() => setSearch(searchInput.trim()), 350)
    return () => clearTimeout(t)
  }, [searchInput])

  const showFranchiseFilter = !isFranchiseScoped
  const franchisesQ = useAccessFranchises()
  const franchises = useMemo(
    () => (showFranchiseFilter ? franchisesQ.data?.pages.flatMap((p) => p.list) ?? [] : []),
    [showFranchiseFilter, franchisesQ.data],
  )

  // The list endpoint filters by a single kycStatus; the queue predicate is an OR
  // across KYC *and* vehicle status, so we pull the roster (sorted oldest-upload
  // first via -created proxy → newest joined) and filter client-side.
  const { data, isLoading, isError, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useRidersInfinite({
      search: search || undefined,
      franchiseId: showFranchiseFilter ? franchiseId || undefined : undefined,
      sort: '-created',
    })
  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  const allRiders = data?.pages.flatMap((p) => p.list) ?? []
  const riders =
    scope === 'queue'
      ? allRiders.filter((r) => needsVerification(r.kycStatus, r.vehicleVerificationStatus))
      : allRiders
  const queueCount = allRiders.filter((r) =>
    needsVerification(r.kycStatus, r.vehicleVerificationStatus),
  ).length

  const [reviewRider, setReviewRider] = useState<RiderDto | null>(null)

  // Deep link: /riders/verification?rider=<id> opens that rider's review packet.
  const [params, setParams] = useSearchParams()
  useEffect(() => {
    const rid = params.get('rider')
    if (!rid) return
    const match = allRiders.find((r) => r.id === rid)
    if (match) {
      setReviewRider(match)
      const next = new URLSearchParams(params)
      next.delete('rider')
      setParams(next, { replace: true })
    }
  }, [params, allRiders, setParams])

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Driver verification</h1>
        <p className="text-sm text-gray-400">
          Review KYC documents and vehicle details before riders go live
          {scope === 'queue' ? ` · ${queueCount} awaiting review` : ''}
        </p>
      </div>

      {/* Scope toggle */}
      <div className="flex w-fit items-center gap-1 rounded-xl border border-gray-200 bg-white p-1">
        {([
          { key: 'queue', label: 'Needs review' },
          { key: 'all', label: 'All riders' },
        ] as const).map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setScope(t.key)}
            className={cn(
              'rounded-lg px-3.5 py-1.5 text-sm font-medium transition-colors',
              scope === t.key ? 'bg-lg-green text-white' : 'text-gray-600 hover:bg-gray-50',
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

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
      </div>

      {/* Body */}
      {isLoading ? (
        <div className="flex items-center justify-center py-24 text-gray-400">
          <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading riders…
        </div>
      ) : isError ? (
        <div className="py-24 text-center text-sm text-red-600">Couldn’t load riders.</div>
      ) : riders.length === 0 ? (
        <EmptyState scope={scope} />
      ) : (
        <>
          <div className="overflow-hidden rounded-2xl border border-gray-200 bg-white">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-100 text-left text-[11px] font-semibold uppercase tracking-wide text-gray-400">
                  <th className="px-5 py-3">Rider</th>
                  <th className="px-5 py-3">Franchise</th>
                  <th className="px-5 py-3">KYC</th>
                  <th className="px-5 py-3">Vehicle</th>
                  <th className="px-5 py-3 text-right">Joined</th>
                </tr>
              </thead>
              <tbody>
                {riders.map((r) => (
                  <VerificationRow key={r.id} rider={r} onReview={() => setReviewRider(r)} />
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

      <RiderVerificationDrawer
        riderId={reviewRider?.id ?? null}
        riderLabel={reviewRider?.riderName ?? reviewRider?.email ?? reviewRider?.riderCode}
        open={reviewRider !== null}
        onClose={() => setReviewRider(null)}
      />
    </div>
  )
}

function VerificationRow({ rider, onReview }: { rider: RiderDto; onReview: () => void }) {
  const name = rider.riderName ?? rider.email ?? rider.riderCode
  const contact = rider.email ?? rider.phone ?? rider.riderCode

  return (
    <tr className="cursor-pointer border-b border-gray-50 last:border-0 hover:bg-gray-50/60" onClick={onReview}>
      <td className="px-5 py-3">
        <div className="flex items-center gap-3 text-left">
          <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-lg-green/12 text-lg-green">
            <FileText className="h-4 w-4" />
          </span>
          <div className="min-w-0">
            <p className="truncate font-medium text-gray-900">{name}</p>
            <p className="truncate text-xs text-gray-400">{contact}</p>
          </div>
        </div>
      </td>
      <td className="px-5 py-3 text-gray-600">{rider.franchiseName ?? '—'}</td>
      <td className="px-5 py-3"><KycBadge status={rider.kycStatus} /></td>
      <td className="px-5 py-3"><VehicleBadge status={rider.vehicleVerificationStatus} /></td>
      <td className="px-5 py-3 text-right text-gray-400">{formatDate(rider.createdAt)}</td>
    </tr>
  )
}

function EmptyState({ scope }: { scope: QueueFilter }) {
  return (
    <div className="flex flex-col items-center justify-center rounded-2xl border border-dashed border-gray-200 bg-white py-20 text-center">
      <span className="mb-3 flex h-12 w-12 items-center justify-center rounded-2xl bg-lg-green/10 text-lg-green">
        <ShieldCheck className="h-6 w-6" />
      </span>
      <p className="text-sm font-medium text-gray-900">
        {scope === 'queue' ? 'Nothing to review' : 'No riders found'}
      </p>
      <p className="mt-1 max-w-xs text-sm text-gray-400">
        {scope === 'queue'
          ? 'Every rider’s KYC and vehicle have been reviewed. New uploads will appear here.'
          : 'No riders match the current filters.'}
      </p>
    </div>
  )
}
