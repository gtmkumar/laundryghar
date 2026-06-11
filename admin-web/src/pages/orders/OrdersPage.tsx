import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Loader2, ChevronRight, Volume2, VolumeX } from 'lucide-react'
import { useOrders, useOrdersInfinite } from '@/hooks/useOrders'
import { usePickupRequests } from '@/hooks/usePickups'
import { useStores } from '@/hooks/useTenancy'
import { useCustomerNameMap } from '@/hooks/useCatalog'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { useOrderChime } from '@/hooks/useOrderChime'
import { useNewItemDetector } from '@/hooks/useNewItemDetector'
import { showToast } from '@/stores/toastStore'
import { PageHeader } from '@/components/shared/PageHeader'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { DataTable, type Column } from '@/components/shared/DataTable'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { cn, formatCurrency, formatDateTime } from '@/lib/utils'
import type { OrderDto } from '@/types/api'
import { OrderDetailDrawer } from './OrderDetailDrawer'
import { OrderCard } from './OrderCard'
import { PickupRequestsTab } from './PickupRequestsTab'
import { OpsQueuesTab } from './OpsQueuesTab'
import { ORDER_STATUS_LIST } from './orderStatus'
import { useStatusLabel, formatStatusLabel } from './orderFormat'

const LIVE_POLL_MS = 20_000

// ── Helpers ───────────────────────────────────────────────────────────────────

function PaymentBadge({ status }: { status: string }) {
  const { t } = useTranslation()
  return (
    <Badge
      variant={status === 'paid' ? 'success' : status === 'pending' ? 'warning' : 'secondary'}
      className="capitalize"
    >
      {t(`orders.payment.${status}`, { defaultValue: status })}
    </Badge>
  )
}

// ── History table columns ───────────────────────────────────────────────────────

function useOrderColumns(): Column<OrderDto>[] {
  const labelFor = useStatusLabel()
  return useMemo(
    () => [
      {
        header: 'Order #',
        accessor: (r) => (
          <span className="font-mono text-xs font-medium text-blue-700">{r.orderNumber}</span>
        ),
        className: 'w-36',
      },
      { header: 'Placed At', accessor: (r) => formatDateTime(r.placedAt), className: 'whitespace-nowrap' },
      { header: 'Channel', accessor: (r) => <span className="capitalize text-gray-600">{r.channel}</span> },
      {
        header: 'Items',
        accessor: (r) => <span className="tabular-nums">{r.totalItems}</span>,
        className: 'text-right w-16',
      },
      {
        header: 'Total',
        accessor: (r) => (
          <span className="tabular-nums font-medium">{formatCurrency(r.grandTotal, r.currencyCode)}</span>
        ),
        className: 'text-right whitespace-nowrap',
      },
      {
        header: 'Status',
        accessor: (r) => <Badge variant="secondary" className="capitalize">{labelFor(r.status)}</Badge>,
      },
      { header: 'Payment', accessor: (r) => <PaymentBadge status={r.paymentStatus} /> },
      {
        header: 'Express',
        accessor: (r) => (r.isExpress ? <Badge variant="warning">Express</Badge> : null),
      },
      {
        header: '',
        accessor: () => <ChevronRight className="h-4 w-4 text-gray-300" />,
        className: 'w-8 text-right',
      },
    ],
    [labelFor],
  )
}

// ── Filters bar ───────────────────────────────────────────────────────────────

function OrderFilters({
  status,
  onStatusChange,
  soundEnabled,
  onToggleSound,
}: {
  status: string
  onStatusChange: (s: string) => void
  soundEnabled: boolean
  onToggleSound: () => void
}) {
  const { t } = useTranslation()
  const labelFor = useStatusLabel()
  return (
    <div className="mb-4 flex items-center gap-3">
      <span className="text-sm text-gray-500">Filter by status:</span>
      <Select value={status} onValueChange={onStatusChange}>
        <SelectTrigger className="h-8 w-44 text-sm">
          <SelectValue placeholder="All statuses" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">All statuses</SelectItem>
          {ORDER_STATUS_LIST.map((s) => (
            <SelectItem key={s} value={s}>
              {labelFor(s)}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <button
        type="button"
        onClick={onToggleSound}
        aria-label={soundEnabled ? t('orders.soundOn') : t('orders.soundOff')}
        title={soundEnabled ? t('orders.soundOn') : t('orders.soundOff')}
        aria-pressed={soundEnabled}
        className={cn(
          'ml-auto inline-flex h-8 w-8 items-center justify-center rounded-lg border transition-colors',
          soundEnabled
            ? 'border-lg-green/30 bg-lg-green/10 text-lg-green'
            : 'border-gray-200 bg-white text-gray-400 hover:bg-gray-50',
        )}
      >
        {soundEnabled ? <Volume2 className="h-4 w-4" /> : <VolumeX className="h-4 w-4" />}
      </button>
    </div>
  )
}

// ── Orders tab — Active card grid + Order history table ─────────────────────────

function OrdersTab({ onOpenOrder, selectedId }: { onOpenOrder: (id: string | null) => void; selectedId: string | null }) {
  const { t } = useTranslation()
  const [statusFilter, setStatusFilter] = useState('all')
  const { soundEnabled, toggleSound, playChime } = useOrderChime()

  const filterActive = statusFilter !== 'all'

  // Active board: non-terminal orders, live-polled. Skipped when a specific
  // status is chosen (the single filtered table covers that case).
  const activeQ = useOrders(
    { statusGroup: 'active', pageSize: 100 },
    filterActive ? undefined : LIVE_POLL_MS,
    !filterActive,
  )

  // Pickup requests (pending) — polled alongside so a new booking chimes too.
  const pickupsQ = usePickupRequests(
    { status: 'pending', pageSize: 100 },
    filterActive ? undefined : LIVE_POLL_MS,
  )

  // History (terminal) or, when a specific status is picked, that single status.
  const historyParams = filterActive ? { status: statusFilter } : { statusGroup: 'history' as const }
  const historyQ = useOrdersInfinite(historyParams)
  const sentinelRef = useInfiniteScroll({
    hasNextPage: historyQ.hasNextPage,
    isFetchingNextPage: historyQ.isFetchingNextPage,
    fetchNextPage: historyQ.fetchNextPage,
  })

  // Name maps for cards.
  const customerNameMap = useCustomerNameMap()
  const storesQ = useStores({ pageSize: 100 })
  const storeMap = useMemo(() => {
    const m = new Map<string, string>()
    for (const s of storesQ.data?.list ?? []) m.set(s.id, s.name)
    return m
  }, [storesQ.data])

  const activeOrders = activeQ.data?.list ?? []
  const pendingPickups = pickupsQ.data?.list ?? []
  const historyOrders = historyQ.data?.pages.flatMap((p) => p.list) ?? []

  // ── New-booking detection → chime + toast (never on first load) ──────────────
  const orderIds = useMemo(() => activeOrders.map((o) => o.id), [activeOrders])
  const pickupIds = useMemo(() => pendingPickups.map((p) => p.id), [pendingPickups])

  const orderById = useMemo(() => new Map(activeOrders.map((o) => [o.id, o])), [activeOrders])
  const pickupById = useMemo(() => new Map(pendingPickups.map((p) => [p.id, p])), [pendingPickups])

  const { isHighlighted } = useNewItemDetector(orderIds, {
    onNew: (id) => {
      const o = orderById.get(id)
      if (!o) return
      playChime()
      showToast(
        'success',
        t('orders.newOrderToast', {
          number: o.orderNumber,
          customer: customerNameMap.get(o.customerId) ?? '—',
        }),
      )
    },
  })

  useNewItemDetector(pickupIds, {
    onNew: (id) => {
      const p = pickupById.get(id)
      if (!p) return
      playChime()
      showToast('info', t('orders.newPickupToast', { number: p.requestNumber }))
    },
  })

  return (
    <>
      <OrderFilters
        status={statusFilter}
        onStatusChange={setStatusFilter}
        soundEnabled={soundEnabled}
        onToggleSound={toggleSound}
      />

      {/* Active orders — card grid (hidden when a specific status is filtered) */}
      {!filterActive && (
        <section className="mb-6">
          <div className="mb-3 flex items-center gap-2">
            <h2 className="text-sm font-semibold text-gray-800">{t('orders.activeOrders')}</h2>
            {activeOrders.length > 0 && (
              <span className="rounded-full bg-lg-green/10 px-2 py-0.5 text-xs font-bold text-lg-green tabular">
                {activeOrders.length}
              </span>
            )}
            {activeQ.isFetching && !activeQ.isLoading && (
              <Loader2 className="h-3.5 w-3.5 animate-spin text-gray-300" />
            )}
          </div>

          {activeQ.isLoading ? (
            <LoadingState message="Loading active orders..." />
          ) : activeQ.isError ? (
            <ErrorState error={activeQ.error as Error} onRetry={() => void activeQ.refetch()} />
          ) : activeOrders.length === 0 ? (
            <Card className="py-10 text-center text-sm text-gray-400">{t('orders.noActiveOrders')}</Card>
          ) : (
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-3">
              {activeOrders.map((o) => (
                <OrderCard
                  key={o.id}
                  order={o}
                  customerName={customerNameMap.get(o.customerId) ?? `…${o.customerId.slice(-6)}`}
                  storeName={storeMap.get(o.storeId) ?? `…${o.storeId.slice(-4)}`}
                  isNew={isHighlighted(o.id)}
                  onClick={() => onOpenOrder(o.id)}
                />
              ))}
            </div>
          )}
        </section>
      )}

      {/* Order history — table (or the single-status view when filtered) */}
      <section>
        <h2 className="mb-3 text-sm font-semibold text-gray-800">
          {filterActive ? formatStatusLabel(statusFilter) : t('orders.orderHistory')}
        </h2>
        <Card className="overflow-hidden">
          {historyQ.isLoading && <LoadingState message="Loading orders..." />}
          {historyQ.isError && (
            <ErrorState error={historyQ.error as Error} onRetry={() => void historyQ.refetch()} />
          )}
          {!historyQ.isLoading && !historyQ.isError && (
            <HistoryTable orders={historyOrders} onRowClick={(r) => onOpenOrder(r.id)} emptyKey="orders.noHistory" />
          )}
        </Card>

        <div ref={sentinelRef} className="h-1" />
        {historyQ.isFetchingNextPage && (
          <div className="flex items-center justify-center py-4 text-gray-400">
            <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more orders…
          </div>
        )}
      </section>

      <OrderDetailDrawer orderId={selectedId} onClose={() => onOpenOrder(null)} />
    </>
  )
}

function HistoryTable({
  orders,
  onRowClick,
  emptyKey,
}: {
  orders: OrderDto[]
  onRowClick: (r: OrderDto) => void
  emptyKey: string
}) {
  const { t } = useTranslation()
  const columns = useOrderColumns()
  return (
    <DataTable
      columns={columns}
      data={orders}
      keyFn={(r) => r.id}
      onRowClick={onRowClick}
      emptyMessage={t(emptyKey)}
    />
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

type OrdersView = 'orders' | 'pickups' | 'ops'

export function OrdersPage() {
  // Tab synced to ?tab= for deep-linking (mirrors the Riders page ?view= pattern).
  const [searchParams, setSearchParams] = useSearchParams()
  const tabParam = searchParams.get('tab')
  const view: OrdersView =
    tabParam === 'pickups' ? 'pickups' : tabParam === 'ops' ? 'ops' : 'orders'

  // Deep-link: ?order=<id> opens the detail drawer (used by the dashboard
  // "needs action" panel). Kept in the URL so the drawer survives a refresh.
  const orderParam = searchParams.get('order')
  const [selectedId, setSelectedId] = useState<string | null>(orderParam)

  // Sync state ← URL when the param changes from outside (e.g. navigation).
  useEffect(() => {
    setSelectedId(orderParam)
  }, [orderParam])

  const openOrder = (id: string | null) => {
    setSelectedId(id)
    const next = new URLSearchParams(searchParams)
    if (id) next.set('order', id)
    else next.delete('order')
    setSearchParams(next, { replace: true })
  }

  const setView = (v: OrdersView) => {
    const next = new URLSearchParams(searchParams)
    if (v === 'orders') next.delete('tab')
    else next.set('tab', v)
    next.delete('order')
    setSearchParams(next, { replace: true })
  }

  const tabs: { key: OrdersView; label: string }[] = [
    { key: 'orders', label: 'Orders' },
    { key: 'pickups', label: 'Pickup requests' },
    { key: 'ops', label: 'Ops queues' },
  ]

  return (
    <div>
      <PageHeader
        title="Orders"
        description="View and manage customer orders, pickup requests, and overdue ops queues."
      />

      {/* Tab bar — Orders list | Pickup requests | Ops queues */}
      <div className="mb-5 flex w-fit items-center gap-1 rounded-xl border border-gray-200 bg-white p-1">
        {tabs.map((tab) => (
          <button
            key={tab.key}
            type="button"
            onClick={() => setView(tab.key)}
            className={cn(
              'rounded-lg px-3.5 py-1.5 text-sm font-medium transition-colors',
              view === tab.key ? 'bg-lg-green text-white' : 'text-gray-600 hover:bg-gray-50',
            )}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {view === 'pickups' ? (
        <Card className="overflow-hidden">
          <PickupRequestsTab />
        </Card>
      ) : view === 'ops' ? (
        <div className="space-y-4">
          <OpsQueuesTab />
        </div>
      ) : (
        <OrdersTab onOpenOrder={openOrder} selectedId={selectedId} />
      )}
    </div>
  )
}
