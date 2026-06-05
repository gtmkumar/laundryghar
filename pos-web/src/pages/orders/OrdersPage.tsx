/**
 * Today's Orders — lists all orders for the active POS store placed today.
 * Tapping an order opens the detail view.
 */
import { Link } from 'react-router-dom'
import { RefreshCw } from 'lucide-react'
import { useOrders } from '@/hooks/useOrders'
import { usePosStore } from '@/stores/posStore'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { Button } from '@/components/ui/button'
import { formatDateTime, formatCurrency, orderStatusColor, todayLocalDate } from '@/lib/utils'

export function OrdersPage() {
  const { activeStore } = usePosStore()
  const today = todayLocalDate()

  const { data, isLoading, isError, refetch } = useOrders({
    storeId: activeStore?.id,
    dateFrom: today,
    dateTo: today,
    pageSize: 50,
  })

  const orders = data?.list ?? []

  return (
    <div className="p-4 lg:p-6 space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-gray-900">Today's Orders</h1>
          <p className="text-sm text-gray-500 mt-0.5">
            {activeStore ? activeStore.name : 'All stores'} · {today}
          </p>
        </div>
        <Button
          variant="outline"
          size="sm"
          onClick={() => refetch()}
          className="gap-2"
          aria-label="Refresh"
        >
          <RefreshCw className="h-4 w-4" />
          Refresh
        </Button>
      </div>

      {isLoading && <LoadingState message="Loading orders…" />}
      {isError && (
        <ErrorState message="Failed to load orders." onRetry={() => refetch()} />
      )}

      {!isLoading && !isError && orders.length === 0 && (
        <div className="text-center py-16 text-gray-400">
          <p className="text-lg">No orders today yet.</p>
          <p className="text-sm mt-1">Walk-in orders will appear here.</p>
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

      {data?.hasNextPage && (
        <p className="text-center text-xs text-gray-400">
          Showing first 50 orders. Scroll back later for older entries.
        </p>
      )}
    </div>
  )
}
