import { ordersClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  OrderDto,
  OrderListParams,
  CreateOrderRequest,
  UpdateOrderStatusRequest,
} from '@/types/api'

const ADMIN = '/api/v1/admin'

// ── List orders ───────────────────────────────────────────────────────────────

export async function getOrders(
  params: OrderListParams = {},
): Promise<PaginatedList<OrderDto>> {
  const { data } = await ordersClient.get<ApiResponse<PaginatedList<OrderDto>>>(
    `${ADMIN}/orders`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrapPaginated(data)
}

// ── Get order by id ───────────────────────────────────────────────────────────

export async function getOrderById(id: string): Promise<OrderDto> {
  const { data } = await ordersClient.get<ApiResponse<OrderDto>>(
    `${ADMIN}/orders/${id}`,
  )
  return unwrap(data)
}

// ── Create order (POS walk-in) ────────────────────────────────────────────────
// POST /api/v1/admin/orders
// Server resolves pricing; channel should be 'walkin'

export async function createOrder(req: CreateOrderRequest): Promise<OrderDto> {
  const { data } = await ordersClient.post<ApiResponse<OrderDto>>(
    `${ADMIN}/orders`,
    req,
  )
  return unwrap(data)
}

// ── Update order status ───────────────────────────────────────────────────────
// PATCH /api/v1/admin/orders/{id}/status

export async function updateOrderStatus(
  id: string,
  payload: UpdateOrderStatusRequest,
): Promise<OrderDto> {
  const { data } = await ordersClient.patch<ApiResponse<OrderDto>>(
    `${ADMIN}/orders/${id}/status`,
    payload,
  )
  return unwrap(data)
}
