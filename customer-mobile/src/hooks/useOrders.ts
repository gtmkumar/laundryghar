import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  cancelOrder,
  getDeliverySlots,
  getMyOrders,
  getMyPickupRequestById,
  getMyPickupRequests,
  getOrderById,
  getOrderTracking,
  rateOrder,
  schedulePickup,
} from '@/api/orders';
import type { CreatePickupRequestRequest, RateOrderRequest } from '@/types/api';

export const orderKeys = {
  list:        (page: number) => ['orders', 'list', page] as const,
  detail:      (id: string)   => ['orders', 'detail', id] as const,
  tracking:    (id: string)   => ['orders', 'tracking', id] as const,
  slots:       (storeId?: string, date?: string) =>
    ['delivery-slots', storeId ?? '', date ?? ''] as const,
  pickupList:  (page: number, status?: string) =>
    ['pickups', 'list', page, status ?? ''] as const,
  pickupDetail: (id: string) => ['pickups', 'detail', id] as const,
};

export function useMyOrders(page = 1) {
  return useQuery({
    queryKey: orderKeys.list(page),
    queryFn:  () => getMyOrders(page),
    staleTime: 30_000,
  });
}

export function useOrderDetail(id: string) {
  return useQuery({
    queryKey: orderKeys.detail(id),
    queryFn:  () => getOrderById(id),
    enabled:  !!id,
    // MOB-6: poll every 30s so the tracking screen updates live
    refetchInterval: 30_000,
  });
}

export function useOrderTracking(id: string) {
  return useQuery({
    queryKey: orderKeys.tracking(id),
    queryFn:  () => getOrderTracking(id),
    enabled:  !!id,
    staleTime: 30_000,
  });
}

export function useDeliverySlots(storeId?: string, date?: string) {
  return useQuery({
    queryKey: orderKeys.slots(storeId, date),
    queryFn:  () => getDeliverySlots(storeId, date),
    enabled:  !!storeId && !!date,
    staleTime: 30_000,
  });
}

export function useCancelOrder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => cancelOrder(id),
    onSuccess: (updated) => {
      void qc.invalidateQueries({ queryKey: ['orders', 'list'] });
      qc.setQueryData(orderKeys.detail(updated.id), updated);
    },
  });
}

export function useRateOrder(orderId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: RateOrderRequest) => rateOrder(orderId, body),
    onSuccess: (updated) => {
      qc.setQueryData(orderKeys.detail(orderId), updated);
      void qc.invalidateQueries({ queryKey: ['orders', 'list'] });
    },
  });
}

export function useSchedulePickup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreatePickupRequestRequest) => schedulePickup(req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['pickups', 'list'] });
    },
  });
}

/** Paginated list of the customer's own pickup requests. */
export function useMyPickupRequests(page = 1, status?: string) {
  return useQuery({
    queryKey: orderKeys.pickupList(page, status),
    queryFn:  () => getMyPickupRequests(page, 20, status),
    staleTime: 30_000,
  });
}

/** Single pickup request detail — used by the tracking screen for pickup ids. */
export function usePickupRequestDetail(id: string) {
  return useQuery({
    queryKey: orderKeys.pickupDetail(id),
    queryFn:  () => getMyPickupRequestById(id),
    enabled:  !!id,
    staleTime: 30_000,
    // MOB-6: poll every 30s so the tracking screen updates live
    refetchInterval: 30_000,
  });
}
