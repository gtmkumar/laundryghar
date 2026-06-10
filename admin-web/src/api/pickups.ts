import { ordersClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  PickupRequestDto,
  PickupRequestListParams,
  AssignPickupPayload,
  RejectPickupPayload,
  DeliveryAssignmentDto,
  DeliverySlotDto,
  DeliverySlotListParams,
  CreateDeliverySlotPayload,
  UpdateDeliverySlotPayload,
} from '@/types/api'

const ADMIN = '/api/v1/admin'

// ── Pickup requests ─────────────────────────────────────────────────────────────

export async function getPickupRequests(
  params: PickupRequestListParams = {},
): Promise<PaginatedList<PickupRequestDto>> {
  // Drop blank filters so we never send `?status=` noise.
  const query: PickupRequestListParams = { page: params.page ?? 1, pageSize: params.pageSize ?? 20 }
  if (params.status) query.status = params.status

  const { data } = await ordersClient.get<ApiResponse<PaginatedList<PickupRequestDto>>>(
    `${ADMIN}/pickup-requests`,
    { params: query },
  )
  return unwrapPaginated(data)
}

export async function getPickupRequest(id: string): Promise<PickupRequestDto> {
  const { data } = await ordersClient.get<ApiResponse<PickupRequestDto>>(
    `${ADMIN}/pickup-requests/${id}`,
  )
  return unwrap(data)
}

/** Assign a pending pickup to a rider — backend policy: permission:pickup.assign. */
export async function assignPickup(
  id: string,
  payload: AssignPickupPayload,
): Promise<DeliveryAssignmentDto> {
  const { data } = await ordersClient.post<ApiResponse<DeliveryAssignmentDto>>(
    `${ADMIN}/pickup-requests/${id}/assign`,
    payload,
  )
  return unwrap(data)
}

/** Reject (cancel) a pending pickup — backend policy: permission:pickup.assign. */
export async function rejectPickup(
  id: string,
  payload: RejectPickupPayload,
): Promise<PickupRequestDto> {
  const { data } = await ordersClient.post<ApiResponse<PickupRequestDto>>(
    `${ADMIN}/pickup-requests/${id}/reject`,
    payload,
  )
  return unwrap(data)
}

// ── Delivery slots ──────────────────────────────────────────────────────────────

export async function getDeliverySlots(
  params: DeliverySlotListParams = {},
): Promise<PaginatedList<DeliverySlotDto>> {
  const query: DeliverySlotListParams = { page: params.page ?? 1, pageSize: params.pageSize ?? 100 }
  if (params.storeId) query.storeId = params.storeId
  if (params.date) query.date = params.date
  if (params.slotType) query.slotType = params.slotType

  const { data } = await ordersClient.get<ApiResponse<PaginatedList<DeliverySlotDto>>>(
    `${ADMIN}/delivery-slots`,
    { params: query },
  )
  return unwrapPaginated(data)
}

export async function createDeliverySlot(
  payload: CreateDeliverySlotPayload,
): Promise<DeliverySlotDto> {
  const { data } = await ordersClient.post<ApiResponse<DeliverySlotDto>>(
    `${ADMIN}/delivery-slots`,
    payload,
  )
  return unwrap(data)
}

export async function updateDeliverySlot(
  id: string,
  payload: UpdateDeliverySlotPayload,
): Promise<DeliverySlotDto> {
  const { data } = await ordersClient.put<ApiResponse<DeliverySlotDto>>(
    `${ADMIN}/delivery-slots/${id}`,
    payload,
  )
  return unwrap(data)
}
