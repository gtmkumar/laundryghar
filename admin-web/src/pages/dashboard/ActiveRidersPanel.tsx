/**
 * ActiveRidersPanel — live roster of on-duty riders for the dashboard.
 *
 * Data: GET /api/v1/admin/riders/live via useRidersLive (polls 20s). We render
 * only on-duty riders, sorted with the busiest/in-motion first. Header shows
 * on-duty / on-the-way / idle counts and a deep-link to the live map.
 *
 * `isOnDuty` alone is NOT availability. It is a stored flag that a rider sets when
 * they start a shift and that nothing clears when they simply stop — the live board
 * read "2 on duty" for two riders whose last GPS ping was 73 days old. The API
 * already tells us this (`isStale` = no ping inside the server's 10-minute window);
 * it was arriving and being spent on the opacity of a 2px dot while the counts —
 * the only part a dispatcher acts on — ignored it.
 *
 * So availability here is `isOnDuty && !isStale`. Riders flagged on-duty but out of
 * contact are still LISTED (someone has to chase them) and counted separately as
 * "no signal"; they are never folded into the on-duty number, and never sit under
 * "Idle", which reads as ready-for-work.
 */
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Bike, MapPin, ArrowUpRight } from 'lucide-react'
import { useRidersLive } from '@/hooks/useRiders'
import type { RiderLiveDto, RiderOpsStatus } from '@/types/api'
import { formatDurationMinutes, minutesSince } from '@/pages/orders/orderFormat'
import { ErrorState } from '@/components/shared/ErrorState'

// Status dot colour + i18n key per ops status.
const OPS_META: Record<RiderOpsStatus, { dot: string; labelKey: string }> = {
  on_the_way: { dot: 'bg-orange-500', labelKey: 'dashboard.onTheWay' },
  to_store: { dot: 'bg-orange-500', labelKey: 'dashboard.onTheWay' },
  arrived: { dot: 'bg-emerald-500', labelKey: 'dashboard.onTheWay' },
  assigned: { dot: 'bg-orange-400', labelKey: 'dashboard.assigned' },
  idle: { dot: 'bg-gray-300', labelKey: 'dashboard.idle' },
  offline: { dot: 'bg-gray-300', labelKey: 'dashboard.idle' },
}

// Sort weight: moving riders first, then arrived, then idle.
const SORT_WEIGHT: Record<RiderOpsStatus, number> = {
  on_the_way: 0,
  to_store: 0,
  arrived: 1,
  assigned: 2,
  idle: 3,
  offline: 4,
}

// Out-of-contact riders sort below every reachable one regardless of their last
// known status: they are the bottom of the list to chase, not the top to dispatch.
const NO_SIGNAL_WEIGHT = 10

function Skeleton({ className }: { className?: string }) {
  return <div className={`skeleton rounded-lg ${className ?? ''}`} />
}

function RiderRow({ rider }: { rider: RiderLiveDto }) {
  const { t } = useTranslation()
  // A stale rider's opsStatus is the last thing we knew, not what is true now, so it
  // must not be shown as a work state — "Idle" on a rider who vanished 73 days ago is
  // the single most misleading cell on this panel.
  const meta = rider.isStale
    ? { dot: 'bg-gray-300', labelKey: 'dashboard.noSignal' }
    : OPS_META[rider.opsStatus] ?? OPS_META.idle
  const pingAgo =
    rider.lastPingAt != null
      ? t('dashboard.lastSeen', { age: formatDurationMinutes(minutesSince(rider.lastPingAt)) })
      : t('dashboard.neverPinged')

  return (
    <div className="flex items-center gap-3 rounded-xl px-3 py-2.5 hover:bg-[#faf9f5] transition-colors">
      <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-lg-green/10 text-lg-green">
        <Bike className="h-4 w-4" />
      </span>
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-1.5">
          <span className={`h-2 w-2 shrink-0 rounded-full ${meta.dot} ${rider.isStale ? 'opacity-50' : ''}`} />
          <p className="truncate text-sm font-medium text-gray-800">
            {rider.riderName ?? rider.riderCode}
          </p>
        </div>
        <p className="mt-0.5 flex items-center gap-1 text-xs text-gray-400">
          <MapPin className="h-3 w-3 shrink-0" />
          <span className="truncate">{pingAgo}</span>
        </p>
      </div>
      <div className="shrink-0 text-right">
        <p className="text-sm font-semibold text-gray-800">
          {t('dashboard.currentLoad', { count: rider.currentLoad })}
        </p>
        <p className="text-[11px] capitalize text-gray-400">{t(meta.labelKey)}</p>
      </div>
    </div>
  )
}

export function ActiveRidersPanel() {
  const { t } = useTranslation()
  const { data, isLoading, isError, error, refetch } = useRidersLive()

  const riders = data ?? []
  // Flagged on duty — the roster. Split it by whether we have actually heard from them.
  const flagged = riders.filter((r) => r.isOnDuty)
  const available = flagged.filter((r) => !r.isStale)
  const noSignal = flagged.length - available.length
  // Work states are counted over `available` only: an unreachable rider is not on the
  // way to anything, and is certainly not idle-and-ready.
  const onTheWay = available.filter((r) => r.opsStatus === 'on_the_way' || r.opsStatus === 'to_store').length
  // 'assigned' is deliberately in NEITHER bucket: the rider is not in motion, but they are not
  // free either. Folding them into "idle" is the exact error this status was added to end.
  const idle = available.filter((r) => r.opsStatus === 'idle' || r.opsStatus === 'offline').length
  const assigned = available.filter((r) => r.opsStatus === 'assigned').length

  const sorted = [...flagged].sort((a, b) => {
    const wa = a.isStale ? NO_SIGNAL_WEIGHT : SORT_WEIGHT[a.opsStatus] ?? 9
    const wb = b.isStale ? NO_SIGNAL_WEIGHT : SORT_WEIGHT[b.opsStatus] ?? 9
    if (wa !== wb) return wa - wb
    return b.currentLoad - a.currentLoad
  })

  return (
    <div className="flex h-full flex-col rounded-3xl border border-[#ede9e0] bg-white p-6 shadow-sm">
      <div className="mb-4 flex items-start justify-between">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wider text-gray-400">
            {t('dashboard.activeRiders')}
          </p>
          <div className="mt-1 flex items-center gap-3 text-xs text-gray-500">
            <span className="font-semibold text-gray-800">
              {available.length} {t('dashboard.onDuty').toLowerCase()}
            </span>
            <span className="inline-flex items-center gap-1">
              <span className="h-1.5 w-1.5 rounded-full bg-orange-500" /> {onTheWay} {t('dashboard.onTheWay').toLowerCase()}
            </span>
            <span className="inline-flex items-center gap-1">
              <span className="h-1.5 w-1.5 rounded-full bg-gray-300" /> {idle} {t('dashboard.idle').toLowerCase()}
            </span>
            {assigned > 0 && (
              <span className="inline-flex items-center gap-1">
                <span className="h-1.5 w-1.5 rounded-full bg-orange-400" /> {assigned}{' '}
                {t('dashboard.assigned').toLowerCase()}
              </span>
            )}
            {noSignal > 0 && (
              <span className="inline-flex items-center gap-1 text-amber-700">
                <span className="h-1.5 w-1.5 rounded-full bg-amber-500" /> {noSignal}{' '}
                {t('dashboard.noSignal').toLowerCase()}
              </span>
            )}
          </div>
        </div>
        <Link
          to="/riders?view=map"
          className="flex items-center gap-0.5 text-xs font-semibold text-lg-green hover:underline"
        >
          {t('dashboard.openLiveMap')} <ArrowUpRight className="h-3.5 w-3.5" />
        </Link>
      </div>

      {isLoading ? (
        <div className="space-y-2">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      ) : isError ? (
        <ErrorState error={error as Error} onRetry={() => void refetch()} />
      ) : sorted.length === 0 ? (
        <div className="flex flex-1 flex-col items-center justify-center py-8 text-center">
          <Bike className="h-8 w-8 text-gray-200" />
          <p className="mt-2 text-sm text-gray-400">{t('dashboard.noRidersOnDuty')}</p>
        </div>
      ) : (
        <div className="-mx-1 space-y-0.5 overflow-y-auto" style={{ maxHeight: 320 }}>
          {sorted.map((r) => (
            <RiderRow key={r.id} rider={r} />
          ))}
        </div>
      )}
    </div>
  )
}
