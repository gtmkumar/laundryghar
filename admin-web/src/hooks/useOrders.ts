import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getOrders,
  getOrderById,
  updateOrderStatus,
  cancelOrder,
  getOrderNotes,
  createOrderNote,
  deleteOrderNote,
  getInvoice,
  generateInvoice,
  getOpsQueues,
} from '@/api/orders'
import type { CreateOrderNoteRequest, OrderListParams, OpsQueuesParams } from '@/types/api'

export const orderKeys = {
  list: (params?: object) => ['orders', 'list', params] as const,
  infinite: (params?: object) => ['orders', 'infinite', params] as const,
  detail: (id: string) => ['orders', 'detail', id] as const,
  notes: (id: string) => ['orders', 'notes', id] as const,
  invoice: (id: string) => ['orders', 'invoice', id] as const,
  opsQueues: (params?: object) => ['orders', 'ops-queues', params] as const,
}

export function useOrders(params: OrderListParams = {}, refetchInterval?: number, enabled = true) {
  return useQuery({
    queryKey: orderKeys.list(params),
    queryFn: () => getOrders(params),
    refetchInterval,
    enabled,
    // Keep prior rows visible across polls so the card grid never flashes empty.
    placeholderData: (prev) => prev,
  })
}

const ORDER_PAGE_SIZE = 100

export function useOrdersInfinite(params: Omit<OrderListParams, 'page' | 'pageSize'> = {}) {
  return useInfiniteQuery({
    queryKey: orderKeys.infinite(params),
    queryFn: ({ pageParam }) =>
      getOrders({ ...params, page: pageParam, pageSize: ORDER_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
  })
}

export function useOrder(id: string | null) {
  return useQuery({
    queryKey: orderKeys.detail(id ?? ''),
    queryFn: () => getOrderById(id as string),
    enabled: Boolean(id),
  })
}

// ── Mutations ───────────────────────────────────────────────────────────────

/**
 * Invalidates every list/infinite orders query plus the affected order's detail.
 * Status/cancel mutations move an order between status buckets, so any open list
 * (filtered or not) may be stale.
 */
function invalidateOrder(qc: ReturnType<typeof useQueryClient>, id: string) {
  void qc.invalidateQueries({ queryKey: ['orders', 'list'] })
  void qc.invalidateQueries({ queryKey: ['orders', 'infinite'] })
  void qc.invalidateQueries({ queryKey: orderKeys.detail(id) })
}

export function useUpdateOrderStatus(id: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: {
      toStatus: string
      reason?: string
      notes?: string
      customerNotified: boolean
    }) => updateOrderStatus(id, payload),
    onSuccess: () => invalidateOrder(qc, id),
  })
}

export function useCancelOrder(id: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (reason: string) => cancelOrder(id, reason),
    onSuccess: () => invalidateOrder(qc, id),
  })
}

// ── Notes ─────────────────────────────────────────────────────────────────────

export function useOrderNotes(id: string | null) {
  return useQuery({
    queryKey: orderKeys.notes(id ?? ''),
    queryFn: () => getOrderNotes(id as string),
    enabled: Boolean(id),
  })
}

export function useCreateOrderNote(id: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateOrderNoteRequest) => createOrderNote(id, payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: orderKeys.notes(id) }),
  })
}

export function useDeleteOrderNote(id: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (noteId: string) => deleteOrderNote(id, noteId),
    onSuccess: () => void qc.invalidateQueries({ queryKey: orderKeys.notes(id) }),
  })
}

// ── Invoice ───────────────────────────────────────────────────────────────────

export function useInvoice(id: string | null, enabled = true) {
  return useQuery({
    queryKey: orderKeys.invoice(id ?? ''),
    queryFn: () => getInvoice(id as string),
    enabled: Boolean(id) && enabled,
  })
}

export function useGenerateInvoice(id: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => generateInvoice(id),
    onSuccess: (inv) => qc.setQueryData(orderKeys.invoice(id), inv),
  })
}

// ── Ops queues ────────────────────────────────────────────────────────────────

export function useOpsQueues(params: OpsQueuesParams = {}, refetchInterval?: number, enabled = true) {
  return useQuery({
    queryKey: orderKeys.opsQueues(params),
    queryFn: () => getOpsQueues(params),
    refetchInterval,
    enabled,
  })
}
