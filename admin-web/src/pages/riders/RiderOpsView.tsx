import { useMemo, useState } from 'react'
import { Loader2, Bike, MapPin, RefreshCw, Package, Truck, Wifi, WifiOff } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useRidersLive, useRiderTrack, useRiderStats } from '@/hooks/useRiders'
import type { RiderLiveDto } from '@/types/api'
import { RiderMap } from '@/components/map/RiderMap'
import { OPS_COLOR, OPS_LABEL } from '@/components/map/mapConfig'

const OPS_ORDER = ['on_the_way', 'arrived', 'idle', 'offline'] as const

/** "3m ago" / "just now" / "—" from an ISO timestamp. */
function ago(iso: string | null): string {
  if (!iso) return '—'
  const t = new Date(iso).getTime()
  if (Number.isNaN(t)) return '—'
  const s = Math.max(0, Math.round((Date.now() - t) / 1000))
  if (s < 30) return 'just now'
  if (s < 90) return '1m ago'
  if (s < 3600) return `${Math.round(s / 60)}m ago`
  if (s < 7200) return '1h ago'
  return `${Math.round(s / 3600)}h ago`
}

export function RiderOpsView() {
  const { data: riders = [], isLoading, isError, isFetching, refetch } = useRidersLive()
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const { data: trail = [] } = useRiderTrack(selectedId)
  const { data: stats } = useRiderStats(selectedId)

  const selected = useMemo(() => riders.find((r) => r.id === selectedId) ?? null, [riders, selectedId])

  // Summary counts per ops status for the legend chips.
  const counts = useMemo(() => {
    const c: Record<string, number> = { on_the_way: 0, arrived: 0, idle: 0, offline: 0 }
    for (const r of riders) c[r.opsStatus] = (c[r.opsStatus] ?? 0) + 1
    return c
  }, [riders])

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-24 text-gray-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading live board…
      </div>
    )
  }
  if (isError) {
    return <div className="py-24 text-center text-sm text-red-600">Couldn’t load the live board.</div>
  }

  return (
    <div className="space-y-4">
      {/* Legend + live indicator */}
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-gray-200 bg-white px-4 py-3">
        <div className="flex flex-wrap items-center gap-3">
          {OPS_ORDER.map((k) => (
            <span key={k} className="inline-flex items-center gap-1.5 text-xs text-gray-600">
              <span className="h-2.5 w-2.5 rounded-full" style={{ background: OPS_COLOR[k] }} />
              {OPS_LABEL[k]} <span className="font-semibold text-gray-900">{counts[k] ?? 0}</span>
            </span>
          ))}
        </div>
        <button
          type="button"
          onClick={() => refetch()}
          className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-2.5 py-1.5 text-xs font-medium text-gray-600 hover:bg-gray-50"
        >
          <RefreshCw className={cn('h-3.5 w-3.5', isFetching && 'animate-spin')} />
          {isFetching ? 'Refreshing' : 'Auto · 20s'}
        </button>
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        {/* Map */}
        <div className="lg:col-span-2">
          <div className="h-[560px] overflow-hidden rounded-2xl border border-gray-200 bg-white">
            <RiderMap riders={riders} selectedId={selectedId} trail={trail} onSelect={setSelectedId} />
          </div>
        </div>

        {/* Roster / selected detail */}
        <div className="space-y-3">
          {selected ? (
            <SelectedPanel rider={selected} stats={stats} onBack={() => setSelectedId(null)} />
          ) : (
            <Roster riders={riders} onSelect={setSelectedId} />
          )}
        </div>
      </div>
    </div>
  )
}

function Roster({ riders, onSelect }: { riders: RiderLiveDto[]; onSelect: (id: string) => void }) {
  return (
    <div className="overflow-hidden rounded-2xl border border-gray-200 bg-white">
      <div className="border-b border-gray-100 px-4 py-2.5 text-xs font-semibold uppercase tracking-wide text-gray-400">
        Riders · {riders.length}
      </div>
      <div className="max-h-[504px] divide-y divide-gray-50 overflow-y-auto">
        {riders.map((r) => (
          <button
            key={r.id}
            type="button"
            onClick={() => onSelect(r.id)}
            className="flex w-full items-center gap-3 px-4 py-2.5 text-left hover:bg-gray-50"
          >
            <span className="relative flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-gray-100 text-gray-500">
              <Bike className="h-4 w-4" />
              <span
                className="absolute -bottom-0.5 -right-0.5 h-3 w-3 rounded-full border-2 border-white"
                style={{ background: OPS_COLOR[r.opsStatus] }}
                title={OPS_LABEL[r.opsStatus]}
              />
            </span>
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium text-gray-900">{r.riderName ?? r.riderCode}</p>
              <p className="truncate text-xs text-gray-400">
                {OPS_LABEL[r.opsStatus]}
                {r.activeOrderNumber ? ` · ${r.activeOrderNumber}` : ''}
              </p>
            </div>
            <div className="shrink-0 text-right">
              <p className="text-xs text-gray-500">{r.lat != null ? ago(r.lastPingAt) : 'no GPS'}</p>
              <p className="text-[11px] text-gray-400">{r.pickupsToday}p · {r.deliveriesToday}d</p>
            </div>
          </button>
        ))}
        {riders.length === 0 && (
          <div className="px-4 py-10 text-center text-sm text-gray-400">No riders in scope.</div>
        )}
      </div>
    </div>
  )
}

function SelectedPanel({
  rider,
  stats,
  onBack,
}: {
  rider: RiderLiveDto
  stats: import('@/types/api').RiderStatsDto | undefined
  onBack: () => void
}) {
  const located = rider.lat != null && rider.lng != null
  return (
    <div className="space-y-3 rounded-2xl border border-gray-200 bg-white p-4">
      <button type="button" onClick={onBack} className="text-xs font-medium text-lg-green hover:underline">
        ← Back to all riders
      </button>

      <div className="flex items-center gap-3">
        <span className="relative flex h-11 w-11 items-center justify-center rounded-full bg-lg-green/12 text-lg-green">
          <Bike className="h-5 w-5" />
          <span
            className="absolute -bottom-0.5 -right-0.5 h-3.5 w-3.5 rounded-full border-2 border-white"
            style={{ background: OPS_COLOR[rider.opsStatus] }}
          />
        </span>
        <div className="min-w-0">
          <p className="truncate text-base font-bold text-gray-900">{rider.riderName ?? rider.riderCode}</p>
          <p className="text-xs text-gray-400">{rider.riderCode}{rider.phone ? ` · ${rider.phone}` : ''}</p>
        </div>
      </div>

      <div className="flex flex-wrap gap-2 text-xs">
        <span className="inline-flex items-center gap-1 rounded-full px-2.5 py-1 font-medium"
              style={{ background: `${OPS_COLOR[rider.opsStatus]}1a`, color: OPS_COLOR[rider.opsStatus] }}>
          {OPS_LABEL[rider.opsStatus]}
        </span>
        <span className={cn('inline-flex items-center gap-1 rounded-full px-2.5 py-1',
          located && !rider.isStale ? 'bg-emerald-50 text-emerald-700' : 'bg-gray-100 text-gray-500')}>
          {located ? <Wifi className="h-3 w-3" /> : <WifiOff className="h-3 w-3" />}
          {located ? (rider.isStale ? 'GPS stale' : 'GPS live') : 'No GPS'}
        </span>
        {rider.activeOrderNumber && (
          <span className="inline-flex items-center gap-1 rounded-full bg-blue-50 px-2.5 py-1 text-blue-700">
            <MapPin className="h-3 w-3" /> {rider.activeLegType} · {rider.activeOrderNumber}
          </span>
        )}
      </div>

      <div className="grid grid-cols-2 gap-2 pt-1">
        <Metric icon={Package} label="Pickups today" value={rider.pickupsToday} />
        <Metric icon={Truck} label="Deliveries today" value={rider.deliveriesToday} />
        <Metric icon={Bike} label="Current load" value={rider.currentLoad} />
        <Metric label="Last ping" valueText={located ? ago(rider.lastPingAt) : '—'} />
      </div>

      {stats && (
        <div className="rounded-xl bg-gray-50 p-3">
          <p className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-gray-400">Today</p>
          <div className="grid grid-cols-2 gap-y-1.5 text-sm">
            <Stat label="Completed" value={`${stats.pickupsDone + stats.deliveriesDone}`} />
            <Stat label="Failed" value={`${stats.assignmentsFailed}`} />
            <Stat label="Distance" value={`${stats.totalKm.toFixed(1)} km`} />
            <Stat label="Assignments" value={`${stats.assignmentsTotal}`} />
          </div>
          <p className="mt-2 text-[11px] text-gray-400">
            COD collected &amp; earnings arrive with the cash-settlement and payout phases.
          </p>
        </div>
      )}
    </div>
  )
}

function Metric({
  icon: Icon, label, value, valueText,
}: { icon?: React.ElementType; label: string; value?: number; valueText?: string }) {
  return (
    <div className="rounded-xl border border-gray-100 px-3 py-2">
      <p className="flex items-center gap-1 text-[11px] text-gray-400">
        {Icon && <Icon className="h-3 w-3" />} {label}
      </p>
      <p className="text-lg font-bold text-gray-900">{valueText ?? value ?? 0}</p>
    </div>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between pr-3">
      <span className="text-gray-500">{label}</span>
      <span className="font-semibold text-gray-900">{value}</span>
    </div>
  )
}
