import { ordersClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  OrderDto,
  OrderListParams,
} from '@/types/api'

const ADMIN = '/api/v1/admin'

export async function getOrders(
  params: OrderListParams = {},
): Promise<PaginatedList<OrderDto>> {
  const { data } = await ordersClient.get<ApiResponse<PaginatedList<OrderDto>>>(
    `${ADMIN}/orders`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

export async function getOrderById(id: string): Promise<OrderDto> {
  const { data } = await ordersClient.get<ApiResponse<OrderDto>>(
    `${ADMIN}/orders/${id}`,
  )
  return unwrap(data)
}

export async function updateOrderStatus(
  id: string,
  payload: { toStatus: string; reason?: string; notes?: string; customerNotified: boolean },
): Promise<OrderDto> {
  const { data } = await ordersClient.patch<ApiResponse<OrderDto>>(
    `${ADMIN}/orders/${id}/status`,
    payload,
  )
  return unwrap(data)
}
