import { useQuery } from '@tanstack/react-query'
import { getOrders, getOrderById } from '@/api/orders'
import type { OrderListParams } from '@/types/api'

export const orderKeys = {
  list: (params?: object) => ['orders', 'list', params] as const,
  detail: (id: string) => ['orders', 'detail', id] as const,
}

export function useOrders(params: OrderListParams = {}, refetchInterval?: number, enabled = true) {
  return useQuery({
    queryKey: orderKeys.list(params),
    queryFn: () => getOrders(params),
    refetchInterval,
    enabled,
  })
}

export function useOrder(id: string) {
  return useQuery({
    queryKey: orderKeys.detail(id),
    queryFn: () => getOrderById(id),
    enabled: Boolean(id),
  })
}
