import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getPickupRequests,
  getPickupRequest,
  assignPickup,
  rejectPickup,
  getDeliverySlots,
  createDeliverySlot,
  updateDeliverySlot,
} from '@/api/pickups'
import type {
  PickupRequestListParams,
  AssignPickupPayload,
  RejectPickupPayload,
  DeliverySlotListParams,
  CreateDeliverySlotPayload,
  UpdateDeliverySlotPayload,
} from '@/types/api'
import { useEffectiveBrandId } from './useBrandContext'

export const pickupKeys = {
  list: (params?: object) => ['pickups', 'list', params] as const,
  detail: (id: string) => ['pickups', 'detail', id] as const,
}

export const slotKeys = {
  list: (params?: object) => ['delivery-slots', 'list', params] as const,
}

// ── Pickup requests ─────────────────────────────────────────────────────────────

/**
 * Brand-scoped pickup-request list. Gated on an effective brand id so a
 * platform-admin doesn't fire the query before a brand is selected (the same
 * pattern the riders/tenancy hooks use).
 */
export function usePickupRequests(params: PickupRequestListParams = {}, refetchInterval?: number) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: pickupKeys.list({ brandId, ...params }),
    queryFn: () => getPickupRequests(params),
    enabled: !!brandId,
    refetchInterval,
    placeholderData: (prev) => prev,
  })
}

export function usePickupRequest(id: string | null) {
  return useQuery({
    queryKey: pickupKeys.detail(id ?? ''),
    queryFn: () => getPickupRequest(id as string),
    enabled: !!id,
  })
}

/**
 * Assign a pickup to a rider. Invalidates every pickup list (the request leaves
 * the "pending" bucket) plus the affected request's detail.
 */
export function useAssignPickup() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: AssignPickupPayload }) =>
      assignPickup(id, payload),
    onSuccess: (_res, { id }) => {
      void qc.invalidateQueries({ queryKey: ['pickups', 'list'] })
      void qc.invalidateQueries({ queryKey: pickupKeys.detail(id) })
    },
  })
}

/**
 * Reject (cancel) a pending pickup request.
 * Invalidates the pickup list (the request leaves the "pending" bucket) and the detail.
 */
export function useRejectPickup() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: RejectPickupPayload }) =>
      rejectPickup(id, payload),
    onSuccess: (_res, { id }) => {
      void qc.invalidateQueries({ queryKey: ['pickups', 'list'] })
      void qc.invalidateQueries({ queryKey: pickupKeys.detail(id) })
    },
  })
}

// ── Delivery slots ──────────────────────────────────────────────────────────────

export function useDeliverySlots(params: DeliverySlotListParams = {}, enabled = true) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: slotKeys.list({ brandId, ...params }),
    queryFn: () => getDeliverySlots(params),
    enabled: !!brandId && enabled,
  })
}

export function useCreateDeliverySlot() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateDeliverySlotPayload) => createDeliverySlot(payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['delivery-slots', 'list'] }),
  })
}

export function useUpdateDeliverySlot() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateDeliverySlotPayload }) =>
      updateDeliverySlot(id, payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['delivery-slots', 'list'] }),
  })
}
