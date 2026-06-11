/**
 * ActiveRidersPanel — live roster of on-duty riders for the dashboard.
 *
 * Data: GET /api/v1/admin/riders/live via useRidersLive (polls 20s). We render
 * only on-duty riders, sorted with the busiest/in-motion first. Header shows
 * on-duty / on-the-way / idle counts and a deep-link to the live map.
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
  idle: { dot: 'bg-gray-300', labelKey: 'dashboard.idle' },
  offline: { dot: 'bg-gray-300', labelKey: 'dashboard.idle' },
}

// Sort weight: moving riders first, then arrived, then idle.
const SORT_WEIGHT: Record<RiderOpsStatus, number> = {
  on_the_way: 0,
  to_store: 0,
  arrived: 1,
  idle: 2,
  offline: 3,
}

function Skeleton({ className }: { className?: string }) {
  return <div className={`skeleton rounded-lg ${className ?? ''}`} />
}

function RiderRow({ rider }: { rider: RiderLiveDto }) {
  const { t } = useTranslation()
  const meta = OPS_META[rider.opsStatus] ?? OPS_META.idle
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
  const onDuty = riders.filter((r) => r.isOnDuty)
  const onTheWay = onDuty.filter((r) => r.opsStatus === 'on_the_way' || r.opsStatus === 'to_store').length
  const idle = onDuty.filter((r) => r.opsStatus === 'idle' || r.opsStatus === 'offline').length

  const sorted = [...onDuty].sort((a, b) => {
    const wa = SORT_WEIGHT[a.opsStatus] ?? 9
    const wb = SORT_WEIGHT[b.opsStatus] ?? 9
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
              {onDuty.length} {t('dashboard.onDuty').toLowerCase()}
            </span>
            <span className="inline-flex items-center gap-1">
              <span className="h-1.5 w-1.5 rounded-full bg-orange-500" /> {onTheWay} {t('dashboard.onTheWay').toLowerCase()}
            </span>
            <span className="inline-flex items-center gap-1">
              <span className="h-1.5 w-1.5 rounded-full bg-gray-300" /> {idle} {t('dashboard.idle').toLowerCase()}
            </span>
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
