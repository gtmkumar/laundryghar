import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getOrders,
  getOrderById,
  createOrder,
  updateOrderStatus,
  generateInvoice,
  getInvoicePdf,
} from '@/api/orders'
import { showToast } from '@/stores/toastStore'
import type {
  OrderListParams,
  CreateOrderRequest,
  UpdateOrderStatusRequest,
  OrderDto,
  PaginatedList,
} from '@/types/api'

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
    // Optimistic advance: flip the status the instant the button is pressed so
    // the detail screen (and any cached list rows) reflect the new stage without
    // waiting on the PATCH round-trip. We roll back on error.
    onMutate: async ({ id, payload }) => {
      const detailKey = orderKeys.detail(id)
      const listPrefix = ['orders', 'list'] as const

      // Stop any in-flight refetches from clobbering the optimistic write.
      await Promise.all([
        qc.cancelQueries({ queryKey: detailKey }),
        qc.cancelQueries({ queryKey: listPrefix }),
      ])

      // Snapshot for rollback.
      const prevDetail = qc.getQueryData<OrderDto>(detailKey)
      const prevLists = qc.getQueriesData<PaginatedList<OrderDto>>({ queryKey: listPrefix })

      // Detail cache: set the target status and clear allowedTransitions so the
      // advance buttons don't offer stale next-steps until onSettled reconciles
      // with the backend's authoritative transition list.
      if (prevDetail) {
        qc.setQueryData<OrderDto>(detailKey, {
          ...prevDetail,
          status: payload.toStatus,
          allowedTransitions: [],
        })
      }

      // List caches: flip the matching row's status in every cached page.
      for (const [key, page] of prevLists) {
        if (!page) continue
        qc.setQueryData<PaginatedList<OrderDto>>(key, {
          ...page,
          list: page.list.map((o) =>
            o.id === id ? { ...o, status: payload.toStatus } : o,
          ),
        })
      }

      return { detailKey, prevDetail, prevLists }
    },
    onSuccess: (updatedOrder) => {
      // Replace the optimistic write with the server's authoritative order
      // (real allowedTransitions + statusHistory row).
      qc.setQueryData(orderKeys.detail(updatedOrder.id), updatedOrder)
    },
    onError: (err, _vars, ctx) => {
      // Roll back both caches to their pre-mutation snapshots.
      if (ctx?.prevDetail) qc.setQueryData(ctx.detailKey, ctx.prevDetail)
      if (ctx?.prevLists) {
        for (const [key, page] of ctx.prevLists) {
          qc.setQueryData(key, page)
        }
      }
      showToast('error', err instanceof Error ? err.message : 'Status update failed.')
    },
    onSettled: (_data, _err, { id }) => {
      // Reconcile: refetch the detail and lists so status, allowedTransitions,
      // and statusHistory match the backend.
      void qc.invalidateQueries({ queryKey: orderKeys.detail(id) })
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
