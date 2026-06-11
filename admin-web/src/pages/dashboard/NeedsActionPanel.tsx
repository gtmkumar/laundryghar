/**
 * NeedsActionPanel — the dashboard "needs action" queue.
 *
 * Two data sources merged into one list, oldest-first:
 *   1. Orders stuck in 'placed' (no pickup scheduled) — the `unactioned` bucket
 *      of GET /admin/orders/ops-queues (polls 30s).
 *   2. Pickup requests stuck 'pending' (unassigned) — GET /admin/pickup-requests.
 *
 * Each row shows number, customer, store, and AGE since created with an amber
 * (<2h) / red (≥2h) badge. Clicking an order deep-links to the Orders page with
 * the detail drawer open (?order=<id>); a pickup deep-links to ?tab=pickups.
 */
import { useMemo } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { AlertCircle, Package, Truck, ChevronRight } from 'lucide-react'
import { useOpsQueues } from '@/hooks/useOrders'
import { usePickupRequests } from '@/hooks/usePickups'
import { useStores } from '@/hooks/useTenancy'
import { useCustomerNameMap } from '@/hooks/useCatalog'
import { useBrandStore } from '@/stores/brandStore'
import { ErrorState } from '@/components/shared/ErrorState'
import {
  formatDurationMinutes,
  minutesSince,
  ageUrgency,
  AGE_URGENCY_CLASS,
} from '@/pages/orders/orderFormat'

interface ActionRow {
  key: string
  kind: 'order' | 'pickup'
  number: string
  customer: string
  store: string
  ageMinutes: number
  navigate: () => void
}

function Skeleton({ className }: { className?: string }) {
  return <div className={`skeleton rounded-lg ${className ?? ''}`} />
}

function AgeBadge({ minutes }: { minutes: number }) {
  const tone = ageUrgency(minutes)
  return (
    <span className={`rounded-full border px-2 py-0.5 text-[11px] font-semibold tabular ${AGE_URGENCY_CLASS[tone]}`}>
      {formatDurationMinutes(minutes)}
    </span>
  )
}

export function NeedsActionPanel() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { activeBrandId } = useBrandStore()
  const enabled = Boolean(activeBrandId)

  // 30s poll matches the dashboard cadence.
  const opsQ = useOpsQueues({ pageSize: 50 }, 30_000)
  const pickupsQ = usePickupRequests({ status: 'pending', pageSize: 50 })
  const storesQ = useStores({ pageSize: 100 })
  const customerNameMap = useCustomerNameMap(enabled)

  const storeMap = useMemo(() => {
    const m = new Map<string, string>()
    for (const s of storesQ.data?.list ?? []) m.set(s.id, s.name)
    return m
  }, [storesQ.data])

  const rows = useMemo<ActionRow[]>(() => {
    const orderRows: ActionRow[] = (opsQ.data?.unactioned.list ?? []).map((o) => ({
      key: `order-${o.id}`,
      kind: 'order',
      number: o.orderNumber,
      customer: o.customerName?.trim() || `…${o.id.slice(-6)}`,
      store: storeMap.get(o.storeId) ?? `…${o.storeId.slice(-4)}`,
      ageMinutes: o.ageMinutes,
      navigate: () => navigate(`/orders?order=${o.id}`),
    }))

    const pickupRows: ActionRow[] = (pickupsQ.data?.list ?? []).map((p) => ({
      key: `pickup-${p.id}`,
      kind: 'pickup',
      number: p.requestNumber,
      customer: customerNameMap.get(p.customerId) ?? `…${p.customerId.slice(-6)}`,
      store: p.storeId ? storeMap.get(p.storeId) ?? `…${p.storeId.slice(-4)}` : '—',
      ageMinutes: minutesSince(p.createdAt),
      navigate: () => navigate('/orders?tab=pickups'),
    }))

    return [...orderRows, ...pickupRows].sort((a, b) => b.ageMinutes - a.ageMinutes)
  }, [opsQ.data, pickupsQ.data, storeMap, customerNameMap, navigate])

  const isLoading = opsQ.isLoading || pickupsQ.isLoading
  // Either source failing must surface — a half-loaded list would falsely read
  // as "all clear" during an outage, the exact WEB-1 failure mode.
  const isError = opsQ.isError || pickupsQ.isError
  const error = (opsQ.error ?? pickupsQ.error) as Error | null
  const retry = () => {
    if (opsQ.isError) void opsQ.refetch()
    if (pickupsQ.isError) void pickupsQ.refetch()
  }

  return (
    <div className="flex h-full flex-col rounded-3xl border border-[#ede9e0] bg-white p-6 shadow-sm">
      <div className="mb-4 flex items-center gap-2">
        <AlertCircle className="h-4 w-4 text-amber-500" />
        <p className="text-xs font-semibold uppercase tracking-wider text-gray-400">
          {t('dashboard.needsAction')}
        </p>
        {rows.length > 0 && (
          <span className="ml-auto rounded-full bg-amber-100 px-2 py-0.5 text-xs font-bold text-amber-700 tabular">
            {rows.length}
          </span>
        )}
      </div>

      {isLoading ? (
        <div className="space-y-2">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      ) : isError ? (
        <ErrorState error={error} onRetry={retry} />
      ) : rows.length === 0 ? (
        <div className="flex flex-1 flex-col items-center justify-center py-8 text-center">
          <AlertCircle className="h-8 w-8 text-gray-200" />
          <p className="mt-2 text-sm text-gray-400">{t('dashboard.needsActionEmpty')}</p>
        </div>
      ) : (
        <div className="-mx-1 space-y-0.5 overflow-y-auto" style={{ maxHeight: 320 }}>
          {rows.map((row) => (
            <button
              key={row.key}
              type="button"
              onClick={row.navigate}
              className="flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-left transition-colors hover:bg-[#faf9f5]"
            >
              <span
                className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full ${
                  row.kind === 'order' ? 'bg-blue-50 text-blue-600' : 'bg-violet-50 text-violet-600'
                }`}
              >
                {row.kind === 'order' ? <Package className="h-4 w-4" /> : <Truck className="h-4 w-4" />}
              </span>
              <div className="min-w-0 flex-1">
                <p className="truncate font-mono text-xs font-semibold text-gray-800">{row.number}</p>
                <p className="mt-0.5 truncate text-xs text-gray-500">
                  {row.customer} · {row.store}
                </p>
                <p className="text-[11px] text-gray-400">
                  {row.kind === 'order' ? t('dashboard.unassignedOrder') : t('dashboard.pendingPickup')}
                </p>
              </div>
              <AgeBadge minutes={row.ageMinutes} />
              <ChevronRight className="h-4 w-4 shrink-0 text-gray-300" />
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
