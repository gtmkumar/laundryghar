import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { getOrders, getOrderById } from '@/api/orders'
import type { OrderListParams } from '@/types/api'

export const orderKeys = {
  list: (params?: object) => ['orders', 'list', params] as const,
  infinite: (params?: object) => ['orders', 'infinite', params] as const,
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

export function useOrder(id: string) {
  return useQuery({
    queryKey: orderKeys.detail(id),
    queryFn: () => getOrderById(id),
    enabled: Boolean(id),
  })
}
