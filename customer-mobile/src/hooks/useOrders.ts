import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  cancelOrder,
  getDeliverySlots,
  getMyOrders,
  getOrderById,
  getOrderTracking,
  schedulePickup,
} from '@/api/orders';
import type { CreatePickupRequestRequest } from '@/types/api';

export const orderKeys = {
  list:     (page: number) => ['orders', 'list', page] as const,
  detail:   (id: string) => ['orders', 'detail', id] as const,
  tracking: (id: string) => ['orders', 'tracking', id] as const,
  slots:    (storeId?: string, date?: string) =>
    ['delivery-slots', storeId ?? '', date ?? ''] as const,
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
      qc.invalidateQueries({ queryKey: ['orders', 'list'] });
      qc.setQueryData(orderKeys.detail(updated.id), updated);
    },
  });
}

export function useSchedulePickup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreatePickupRequestRequest) => schedulePickup(req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['orders'] });
    },
  });
}
