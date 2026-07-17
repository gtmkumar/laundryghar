import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useIsFocused } from '@react-navigation/native';
import {
  cancelOrder,
  createParcelOrder,
  getDeliverySlots,
  getMyOrders,
  getMyPickupRequestById,
  getMyPickupRequests,
  getOrderById,
  getOrderTracking,
  quoteParcelFare,
  rateOrder,
  rateRider,
  reschedulePickup,
  schedulePickup,
  validateCouponForPickup,
} from '@/api/orders';
import type {
  CreateParcelOrderRequest,
  CreatePickupRequestRequest,
  CouponPreviewResult,
  FareQuoteDto,
  FareQuoteRequest,
  OrderDto,
  PaginatedData,
  RateOrderRequest,
  RateRiderRequest,
  RateRiderResult,
  ReschedulePickupRequestBody,
  ValidateCouponForPickupRequest,
} from '@/types/api';

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

/** Statuses that mean the order is still moving — used to gate live polling. */
const ACTIVE_ORDER_STATUSES = new Set([
  'placed', 'pickup_scheduled', 'pickup_assigned', 'picked_up',
  'received', 'sorting', 'in_process', 'qc', 'ready',
  'delivery_scheduled', 'out_for_delivery',
]);

const ACTIVE_PICKUP_STATUSES = new Set([
  'pending', 'assigned', 'rider_dispatched', 'arrived', 'rescheduled', 'no_response',
]);

export function useMyOrders(page = 1) {
  return useQuery({
    queryKey: orderKeys.list(page),
    queryFn:  () => getMyOrders(page),
    staleTime: 30_000,
  });
}

/**
 * R3-CM-1: poll every 25s while the screen is focused AND the order is active.
 * Once the order reaches a terminal status the interval stops automatically.
 */
export function useOrderDetail(id: string) {
  const isFocused = useIsFocused();
  const query = useQuery({
    queryKey: orderKeys.detail(id),
    queryFn:  () => getOrderById(id),
    enabled:  !!id,
    refetchInterval: (query) => {
      if (!isFocused) return false;
      const status = query.state.data?.status;
      return status && ACTIVE_ORDER_STATUSES.has(status) ? 25_000 : false;
    },
  });
  return query;
}

/**
 * R3-CM-1: poll tracking history every 25s while focused + order active.
 * Callers pass the order status so we can stop polling on terminal states.
 */
export function useOrderTracking(id: string, orderStatus?: string) {
  const isFocused = useIsFocused();
  return useQuery({
    queryKey: orderKeys.tracking(id),
    queryFn:  () => getOrderTracking(id),
    enabled:  !!id,
    staleTime: 20_000,
    refetchInterval: () => {
      if (!isFocused) return false;
      if (!orderStatus) return 25_000; // unknown — poll conservatively
      return ACTIVE_ORDER_STATUSES.has(orderStatus) ? 25_000 : false;
    },
  });
}

export function useDeliverySlots(storeId?: string, date?: string) {
  // CUST-BUG-01: storeId is optional on the backend (GET /delivery-slots?date=…).
  // Enabling only when storeId is present means the query never fires pre-order
  // (store is assigned server-side). Guard on date alone so the slot grid loads
  // as soon as a day is selected. The error state must only render on isError=true,
  // not when the query is idle/disabled.
  return useQuery({
    queryKey: orderKeys.slots(storeId, date),
    queryFn:  () => getDeliverySlots(storeId, date),
    enabled:  !!date,
    staleTime: 30_000,
  });
}

export function useCancelOrder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => cancelOrder(id),
    onMutate: async (id) => {
      await qc.cancelQueries({ queryKey: orderKeys.detail(id) });
      await qc.cancelQueries({ queryKey: ['orders', 'list'] });
      const previousDetail = qc.getQueryData<OrderDto>(orderKeys.detail(id));
      const previousLists = qc.getQueriesData<PaginatedData<OrderDto>>({
        queryKey: ['orders', 'list'],
      });
      qc.setQueryData<OrderDto>(orderKeys.detail(id), (prev) =>
        prev ? { ...prev, status: 'cancelled' } : prev,
      );
      qc.setQueriesData<PaginatedData<OrderDto>>(
        { queryKey: ['orders', 'list'] },
        (prev) =>
          prev
            ? {
                ...prev,
                list: prev.list.map((o) =>
                  o.id === id ? { ...o, status: 'cancelled' } : o,
                ),
              }
            : prev,
      );
      return { previousDetail, previousLists };
    },
    // Consumer (order detail) surfaces the rollback Alert; here we only restore.
    onError: (_err, id, ctx) => {
      if (ctx?.previousDetail) {
        qc.setQueryData(orderKeys.detail(id), ctx.previousDetail);
      }
      ctx?.previousLists?.forEach(([key, data]) => qc.setQueryData(key, data));
    },
    onSettled: (_data, _err, id) => {
      void qc.invalidateQueries({ queryKey: orderKeys.detail(id) });
      void qc.invalidateQueries({ queryKey: ['orders', 'list'] });
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

/**
 * Rate the rider who delivered an order. The mutation resolves to the rider's
 * new running average + count. The order detail is invalidated so any cached
 * derived state refreshes. A 422 (no rider / not delivered) surfaces as an
 * error for the caller to handle.
 */
export function useRateRider(orderId: string) {
  const qc = useQueryClient();
  return useMutation<RateRiderResult, Error, RateRiderRequest>({
    mutationFn: (body) => rateRider(orderId, body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: orderKeys.detail(orderId) });
    },
  });
}

// ── Parcel (point-to-point) ───────────────────────────────────────────────────

/**
 * Fetch a fare quote for a parcel job. Exposed as a mutation (not a query) so
 * the caller controls exactly when to (re-)price — the returned token is
 * short-lived and must be re-fetched if it expires before order creation.
 */
export function useFareQuote() {
  return useMutation<FareQuoteDto, Error, FareQuoteRequest>({
    mutationFn: quoteParcelFare,
  });
}

/**
 * Create a parcel order from a held fare-quote token. Invalidates the orders
 * list and the pickups list (a parcel order can surface in both feeds).
 */
export function useCreateParcelOrder() {
  const qc = useQueryClient();
  return useMutation<OrderDto, Error, CreateParcelOrderRequest>({
    mutationFn: createParcelOrder,
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['orders'] });
      void qc.invalidateQueries({ queryKey: ['pickups', 'list'] });
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

/**
 * R3-CM-1: poll pickup detail every 25s while focused + pickup is active.
 */
export function usePickupRequestDetail(id: string) {
  const isFocused = useIsFocused();
  return useQuery({
    queryKey: orderKeys.pickupDetail(id),
    queryFn:  () => getMyPickupRequestById(id),
    enabled:  !!id,
    staleTime: 20_000,
    refetchInterval: (query) => {
      if (!isFocused) return false;
      const status = query.state.data?.status;
      return status && ACTIVE_PICKUP_STATUSES.has(status) ? 25_000 : false;
    },
  });
}

/** R3-BE-3: reschedule a pending/no_response/rescheduled pickup request. */
export function useReschedulePickup(pickupId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: ReschedulePickupRequestBody) => reschedulePickup(pickupId, req),
    onSuccess: (updated) => {
      qc.setQueryData(orderKeys.pickupDetail(pickupId), updated);
      void qc.invalidateQueries({ queryKey: ['pickups', 'list'] });
    },
  });
}

/**
 * R3-BE-2: validate a coupon code against the cart subtotal before submitting.
 * Returns a useMutation — callers call mutateAsync and get CouponPreviewResult.
 */
export function useValidateCoupon() {
  return useMutation<CouponPreviewResult, Error, ValidateCouponForPickupRequest>({
    mutationFn: validateCouponForPickup,
  });
}
