import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getOrders,
  getOrderById,
  createOrder,
  updateOrderStatus,
  generateInvoice,
  getInvoicePdf,
} from '@/api/orders'
import type { OrderListParams, CreateOrderRequest, UpdateOrderStatusRequest } from '@/types/api'

export const orderKeys = {
  list: (params?: object) => ['orders', 'list', params] as const,
  detail: (id: string) => ['orders', 'detail', id] as const,
}

export function useOrders(params: OrderListParams = {}) {
  return useQuery({
    queryKey: orderKeys.list(params),
    queryFn: () => getOrders(params),
    // Wait until the active store is resolved — an unscoped request is
    // rejected by the backend (401) before brand/store selection completes.
    enabled: Boolean(params.storeId),
    staleTime: 30_000,
  })
}

export function useOrder(id: string) {
  return useQuery({
    queryKey: orderKeys.detail(id),
    queryFn: () => getOrderById(id),
    enabled: Boolean(id),
    staleTime: 30_000,
  })
}

export function useCreateOrder() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: CreateOrderRequest) => createOrder(req),
    onSuccess: (order) => {
      // POS-6: seed the detail cache so a "View order" navigation renders
      // instantly (and a follow-up payment invalidation has a key to bust)
      // without a round-trip flash of the loading state.
      qc.setQueryData(orderKeys.detail(order.id), order)
      // Invalidate the orders list so it refreshes
      void qc.invalidateQueries({ queryKey: ['orders', 'list'] })
    },
  })
}

export function useUpdateOrderStatus() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateOrderStatusRequest }) =>
      updateOrderStatus(id, payload),
    onSuccess: (updatedOrder) => {
      // Update the cached detail and invalidate list
      qc.setQueryData(orderKeys.detail(updatedOrder.id), updatedOrder)
      void qc.invalidateQueries({ queryKey: ['orders', 'list'] })
    },
  })
}

/**
 * Generates the invoice (if the status allows) then opens the rendered PDF in a
 * new browser tab. Generation is idempotent server-side, so re-printing an
 * already-invoiced order just re-fetches. The PDF is fetched as an authenticated
 * Blob and shown via an object URL (the token never lands in a URL).
 */
export function useOpenInvoicePdf() {
  return useMutation({
    mutationFn: async (orderId: string) => {
      // Best-effort generate; ignore "already exists" / status-gate errors and
      // let the subsequent fetch surface a real "no invoice" failure.
      try {
        await generateInvoice(orderId)
      } catch {
        // Status not billable yet, or invoice already exists — fall through.
      }
      const blob = await getInvoicePdf(orderId)
      const url = URL.createObjectURL(blob)
      window.open(url, '_blank', 'noopener')
      // Revoke after the tab has had time to load the resource.
      setTimeout(() => URL.revokeObjectURL(url), 60_000)
    },
  })
}
