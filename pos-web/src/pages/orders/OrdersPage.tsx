/**
 * Orders — searchable order history for the active POS store (R3-POS-1).
 *
 * Replaces the old today-only list so the counter can find a past order to view
 * and reprint a receipt.
 *
 * Server-side filters (the backend GET /api/v1/admin/orders supports these):
 *   - dateFrom / dateTo  → driven by the date-range presets (Today / Yesterday /
 *     Last 7 days) or a custom range.
 *   - storeId            → the active POS store.
 *
 * The backend orders list has NO text-search parameter, so number/phone search
 * is applied CLIENT-SIDE over the loaded date range:
 *   - A digit-y query is treated as a customer phone: we resolve it to customer
 *     id(s) via the admin customer search (which IS server-side) and keep orders
 *     for those customers.
 *   - Anything else is matched as an order-number substring.
 * This is why the search box notes it filters "within the selected dates".
 *
 * Tapping a result opens the detail view, where Receipt / Garment-tag reprint and
 * the tax-invoice PDF already work for any order regardless of age.
 */
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { RefreshCw, Search, X } from 'lucide-react'
import { useOrders } from '@/hooks/useOrders'
import { useCustomerSearch } from '@/hooks/useCustomers'
import { useDebounce } from '@/hooks/useDebounce'
import { usePosStore } from '@/stores/posStore'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { formatDateTime, formatCurrency, orderStatusColor, todayLocalDate } from '@/lib/utils'

// ── Date-range presets ────────────────────────────────────────────────────────

type PresetKey = 'today' | 'yesterday' | 'last7'

/** Returns a "YYYY-MM-DD" string for `daysAgo` days before today (Asia/Kolkata). */
function localDateMinus(daysAgo: number): string {
  const d = new Date()
  d.setDate(d.getDate() - daysAgo)
  return d.toLocaleDateString('en-CA', { timeZone: 'Asia/Kolkata' })
}

function rangeForPreset(preset: PresetKey): { from: string; to: string } {
  const today = todayLocalDate()
  switch (preset) {
    case 'yesterday': {
      const y = localDateMinus(1)
      return { from: y, to: y }
    }
    case 'last7':
      return { from: localDateMinus(6), to: today }
    case 'today':
    default:
      return { from: today, to: today }
  }
}

const PRESETS: { key: PresetKey; label: string }[] = [
  { key: 'today', label: 'Today' },
  { key: 'yesterday', label: 'Yesterday' },
  { key: 'last7', label: 'Last 7 days' },
]

/** A query made only of digits / phone punctuation is treated as a phone search. */
function looksLikePhone(q: string): boolean {
  const digits = q.replace(/[\s\-()+]/g, '')
  return digits.length >= 4 && /^\d+$/.test(digits)
}

export function OrdersPage() {
  const { activeStore } = usePosStore()

  const [preset, setPreset] = useState<PresetKey>('today')
  // A custom range overrides the preset when both ends are set.
  const [customFrom, setCustomFrom] = useState('')
  const [customTo, setCustomTo] = useState('')
  const [searchTerm, setSearchTerm] = useState('')
  const debouncedSearch = useDebounce(searchTerm, 300)

  const useCustom = customFrom !== '' && customTo !== ''
  const { from: dateFrom, to: dateTo } = useCustom
    ? { from: customFrom, to: customTo }
    : rangeForPreset(preset)

  // Larger page than the old 50 so a multi-day range isn't silently truncated
  // before the client-side number/phone filter runs.
  const { data, isLoading, isError, refetch, isFetching } = useOrders({
    storeId: activeStore?.id,
    dateFrom,
    dateTo,
    pageSize: 200,
  })

  // Phone search resolves to customer id(s) via the server-side customer search.
  const phoneQuery = looksLikePhone(debouncedSearch) ? debouncedSearch.trim() : ''
  const { data: customerMatches } = useCustomerSearch(phoneQuery, phoneQuery.length > 0)
  const matchedCustomerIds = useMemo(
    () => new Set((customerMatches?.list ?? []).map((c) => c.id)),
    [customerMatches],
  )

  // Client-side text filter within the loaded date range (the backend has no
  // search param). Number search = order-number substring; phone search =
  // membership in the resolved customer-id set.
  const orders = useMemo(() => {
    const allOrders = data?.list ?? []
    const q = debouncedSearch.trim().toLowerCase()
    if (!q) return allOrders
    if (phoneQuery) {
      return allOrders.filter((o) => matchedCustomerIds.has(o.customerId))
    }
    return allOrders.filter((o) => o.orderNumber.toLowerCase().includes(q))
  }, [data, debouncedSearch, phoneQuery, matchedCustomerIds])

  const isSearching = debouncedSearch.trim().length > 0
  const rangeLabel = dateFrom === dateTo ? dateFrom : `${dateFrom} → ${dateTo}`

  return (
    <div className="p-4 lg:p-6 space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-gray-900">Orders</h1>
          <p className="text-sm text-gray-500 mt-0.5">
            {activeStore ? activeStore.name : 'All stores'} · {rangeLabel}
          </p>
        </div>
        <Button
          variant="outline"
          size="sm"
          onClick={() => refetch()}
          className="gap-2"
          aria-label="Refresh"
        >
          <RefreshCw className={`h-4 w-4 ${isFetching ? 'animate-spin' : ''}`} />
          Refresh
        </Button>
      </div>

      {/* Search */}
      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400" />
        <Input
          type="search"
          inputMode="search"
          placeholder="Search order number or customer phone…"
          className="pl-9 pr-9"
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          aria-label="Search orders by number or customer phone"
        />
        {searchTerm && (
          <button
            type="button"
            onClick={() => setSearchTerm('')}
            className="absolute right-2 top-1/2 -translate-y-1/2 p-1 text-gray-400 hover:text-gray-700 rounded-lg hover:bg-gray-100"
            aria-label="Clear search"
          >
            <X className="h-4 w-4" />
          </button>
        )}
      </div>
      {isSearching && (
        <p className="text-[11px] text-gray-400 -mt-2">
          Filtering within the selected dates. Widen the range to search older orders.
        </p>
      )}

      {/* Date range presets + custom */}
      <div className="space-y-3">
        <div className="flex flex-wrap gap-2">
          {PRESETS.map((p) => (
            <button
              key={p.key}
              type="button"
              onClick={() => {
                setPreset(p.key)
                setCustomFrom('')
                setCustomTo('')
              }}
              className={`px-3.5 py-1.5 rounded-full text-sm font-medium border transition-colors ${
                !useCustom && preset === p.key
                  ? 'bg-blue-600 text-white border-blue-600'
                  : 'bg-white text-gray-700 border-gray-200 hover:border-blue-300'
              }`}
            >
              {p.label}
            </button>
          ))}
        </div>
        <div className="flex flex-wrap items-end gap-3">
          <label className="text-xs text-gray-500">
            <span className="block mb-1">From</span>
            <Input
              type="date"
              value={customFrom}
              max={customTo || undefined}
              onChange={(e) => setCustomFrom(e.target.value)}
              className="h-10 w-40"
            />
          </label>
          <label className="text-xs text-gray-500">
            <span className="block mb-1">To</span>
            <Input
              type="date"
              value={customTo}
              min={customFrom || undefined}
              onChange={(e) => setCustomTo(e.target.value)}
              className="h-10 w-40"
            />
          </label>
          {useCustom && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                setCustomFrom('')
                setCustomTo('')
              }}
            >
              Clear dates
            </Button>
          )}
        </div>
      </div>

      {isLoading && <LoadingState message="Loading orders…" />}
      {isError && (
        <ErrorState message="Failed to load orders." onRetry={() => refetch()} />
      )}

      {!isLoading && !isError && orders.length === 0 && (
        <div className="text-center py-16 text-gray-400">
          {isSearching ? (
            <>
              <p className="text-lg">No matching orders.</p>
              <p className="text-sm mt-1">
                Check the order number / phone, or widen the date range.
              </p>
            </>
          ) : (
            <>
              <p className="text-lg">No orders in this range.</p>
              <p className="text-sm mt-1">Walk-in orders will appear here.</p>
            </>
          )}
        </div>
      )}

      {/* Order list */}
      <div className="space-y-3">
        {orders.map((order) => (
          <Link
            key={order.id}
            to={`/orders/${order.id}`}
            className="block bg-white rounded-2xl border border-gray-200 p-4 hover:border-blue-300 hover:shadow-sm transition-all active:scale-[0.99]"
          >
            <div className="flex items-start justify-between gap-3">
              <div className="flex-1 min-w-0">
                <p className="font-semibold text-gray-900">{order.orderNumber}</p>
                <p className="text-sm text-gray-500 mt-0.5">{formatDateTime(order.placedAt)}</p>
                <p className="text-xs text-gray-400 mt-0.5">
                  {order.totalItems} item{order.totalItems !== 1 ? 's' : ''}
                  {order.isExpress && (
                    <span className="ml-2 text-orange-600 font-medium">Express</span>
                  )}
                </p>
              </div>
              <div className="flex flex-col items-end gap-2">
                <span className="font-bold text-gray-900">{formatCurrency(order.grandTotal)}</span>
                <span
                  className={`px-2.5 py-0.5 rounded-full text-xs font-semibold ${orderStatusColor(order.status)}`}
                >
                  {order.status.replace(/_/g, ' ')}
                </span>
              </div>
            </div>
          </Link>
        ))}
      </div>

      {!isSearching && data?.hasNextPage && (
        <p className="text-center text-xs text-gray-400">
          Showing the most recent 200 orders in this range. Narrow the dates to see older entries.
        </p>
      )}
    </div>
  )
}
