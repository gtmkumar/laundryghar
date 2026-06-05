import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { getOrders, getOrderById, createOrder, updateOrderStatus } from '@/api/orders'
import type { OrderListParams, CreateOrderRequest, UpdateOrderStatusRequest } from '@/types/api'

export const orderKeys = {
  list: (params?: object) => ['orders', 'list', params] as const,
  detail: (id: string) => ['orders', 'detail', id] as const,
}

export function useOrders(params: OrderListParams = {}) {
  return useQuery({
    queryKey: orderKeys.list(params),
    queryFn: () => getOrders(params),
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
    onSuccess: () => {
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
