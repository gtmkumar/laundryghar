import { ordersClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  OrderDto,
  OrderListParams,
  OrderNoteDto,
  CreateOrderNoteRequest,
  InvoiceDto,
  OpsQueuesResponse,
  OpsQueuesParams,
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

export async function cancelOrder(id: string, reason: string): Promise<OrderDto> {
  const { data } = await ordersClient.post<ApiResponse<OrderDto>>(
    `${ADMIN}/orders/${id}/cancel`,
    { reason },
  )
  return unwrap(data)
}

// ── Notes ─────────────────────────────────────────────────────────────────────

export async function getOrderNotes(id: string): Promise<OrderNoteDto[]> {
  const { data } = await ordersClient.get<ApiResponse<OrderNoteDto[]>>(
    `${ADMIN}/orders/${id}/notes`,
  )
  return data.data ?? []
}

export async function createOrderNote(
  id: string,
  payload: CreateOrderNoteRequest,
): Promise<OrderNoteDto> {
  const { data } = await ordersClient.post<ApiResponse<OrderNoteDto>>(
    `${ADMIN}/orders/${id}/notes`,
    payload,
  )
  return unwrap(data)
}

export async function deleteOrderNote(id: string, noteId: string): Promise<void> {
  await ordersClient.delete(`${ADMIN}/orders/${id}/notes/${noteId}`)
}

// ── Invoice ───────────────────────────────────────────────────────────────────

/** Returns the invoice JSON, or null when none has been generated yet (404). */
export async function getInvoice(id: string): Promise<InvoiceDto | null> {
  try {
    const { data } = await ordersClient.get<ApiResponse<InvoiceDto>>(
      `${ADMIN}/orders/${id}/invoice`,
    )
    return data.status && data.data ? data.data : null
  } catch (e) {
    // 404 = no invoice yet; surface as null rather than throwing.
    const status = (e as { response?: { status?: number } })?.response?.status
    if (status === 404) return null
    throw e
  }
}

export async function generateInvoice(id: string): Promise<InvoiceDto> {
  const { data } = await ordersClient.post<ApiResponse<InvoiceDto>>(
    `${ADMIN}/orders/${id}/invoice`,
  )
  return unwrap(data)
}

// ── Ops queues ────────────────────────────────────────────────────────────────

export async function getOpsQueues(params: OpsQueuesParams = {}): Promise<OpsQueuesResponse> {
  const { data } = await ordersClient.get<ApiResponse<OpsQueuesResponse>>(
    `${ADMIN}/orders/ops-queues`,
    { params: { page: 1, pageSize: 20, ...params } },
  )
  return unwrap(data)
}

/**
 * Fetches the invoice PDF as a blob (auth header attached by the axios
 * interceptor) and triggers a browser download via an object URL.
 */
export async function downloadInvoicePdf(id: string, fileName: string): Promise<void> {
  const { data } = await ordersClient.get<Blob>(`${ADMIN}/orders/${id}/invoice.pdf`, {
    responseType: 'blob',
  })
  const url = URL.createObjectURL(data)
  try {
    const a = document.createElement('a')
    a.href = url
    a.download = fileName.endsWith('.pdf') ? fileName : `${fileName}.pdf`
    document.body.appendChild(a)
    a.click()
    a.remove()
  } finally {
    URL.revokeObjectURL(url)
  }
}
