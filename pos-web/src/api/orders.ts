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

// ── Invoice ───────────────────────────────────────────────────────────────────
// POST /api/v1/admin/orders/{id}/invoice   → generate (idempotent); gated to
//   billable statuses (ready | delivered | closed) by the backend.
// GET  /api/v1/admin/orders/{id}/invoice.pdf → PDF bytes (existing invoice only).

export async function generateInvoice(id: string): Promise<void> {
  await ordersClient.post(`${ADMIN}/orders/${id}/invoice`)
}

/**
 * Fetches the rendered invoice PDF as a Blob.
 * The order must already have an invoice (call generateInvoice first when the
 * status allows). The axios client attaches the auth + brand headers so we can
 * authenticate the binary fetch without exposing the token in a URL.
 */
export async function getInvoicePdf(id: string): Promise<Blob> {
  const { data } = await ordersClient.get<Blob>(
    `${ADMIN}/orders/${id}/invoice.pdf`,
    { responseType: 'blob' },
  )
  return data
}
