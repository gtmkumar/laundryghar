/**
 * Orders API — maps to CustomerOrderEndpoints.cs (Orders service)
 * Endpoint prefix: {Orders}/api/v1/customer/
 */
import { ordersClient, unwrapList, unwrapPaginated, unwrapSingle } from '@/api/client';
import type {
  CreateParcelOrderRequest,
  CreatePickupRequestRequest,
  CouponPreviewResult,
  DeliverySlotDto,
  FareQuoteDto,
  FareQuoteRequest,
  ListResponse,
  OrderDto,
  OrderStatusHistoryDto,
  PaginatedListResponse,
  PickupRequestDto,
  RateOrderRequest,
  ReschedulePickupRequestBody,
  SingleResponse,
  ValidateCouponForPickupRequest,
} from '@/types/api';

// ── Orders ───────────────────────────────────────────────────────────────────

/** GET /api/v1/customer/orders?page=&pageSize= */
export async function getMyOrders(
  page = 1,
  pageSize = 20,
): Promise<{ list: OrderDto[]; hasPreviousPage: boolean; hasNextPage: boolean }> {
  const res = await ordersClient.get<PaginatedListResponse<OrderDto>>(
    '/customer/orders/',
    { params: { page, pageSize } },
  );
  return unwrapPaginated(res.data);
}

/** GET /api/v1/customer/orders/{id} */
export async function getOrderById(id: string): Promise<OrderDto> {
  const res = await ordersClient.get<SingleResponse<OrderDto>>(
    `/customer/orders/${id}`,
  );
  return unwrapSingle(res.data);
}

/** GET /api/v1/customer/orders/{id}/tracking */
export async function getOrderTracking(
  id: string,
): Promise<OrderStatusHistoryDto[]> {
  const res = await ordersClient.get<ListResponse<OrderStatusHistoryDto>>(
    `/customer/orders/${id}/tracking`,
  );
  return unwrapList(res.data);
}

/** POST /api/v1/customer/orders/{id}/cancel */
export async function cancelOrder(id: string): Promise<OrderDto> {
  const res = await ordersClient.post<SingleResponse<OrderDto>>(
    `/customer/orders/${id}/cancel`,
  );
  return unwrapSingle(res.data);
}

/** POST /api/v1/customer/orders/{id}/rate */
export async function rateOrder(
  id: string,
  body: RateOrderRequest,
): Promise<OrderDto> {
  const res = await ordersClient.post<SingleResponse<OrderDto>>(
    `/customer/orders/${id}/rate`,
    body,
  );
  return unwrapSingle(res.data);
}

// ── Parcel (point-to-point) ───────────────────────────────────────────────────

/**
 * POST /api/v1/customer/fare/quote
 * Returns a short-lived fare quote + token. 422 when an address lacks geo-location.
 */
export async function quoteParcelFare(
  req: FareQuoteRequest,
): Promise<FareQuoteDto> {
  const res = await ordersClient.post<SingleResponse<FareQuoteDto>>(
    '/customer/fare/quote',
    req,
  );
  return unwrapSingle(res.data);
}

/**
 * POST /api/v1/customer/orders/parcel
 * Creates a parcel order from a held fare-quote token (201). 422 on a
 * missing/expired/tampered/mismatched token.
 */
export async function createParcelOrder(
  req: CreateParcelOrderRequest,
): Promise<OrderDto> {
  const res = await ordersClient.post<SingleResponse<OrderDto>>(
    '/customer/orders/parcel',
    req,
  );
  return unwrapSingle(res.data);
}

// ── Pickup scheduling ─────────────────────────────────────────────────────────

/** POST /api/v1/customer/pickup-requests */
export async function schedulePickup(
  req: CreatePickupRequestRequest,
): Promise<PickupRequestDto> {
  const res = await ordersClient.post<SingleResponse<PickupRequestDto>>(
    '/customer/pickup-requests/',
    req,
  );
  return unwrapSingle(res.data);
}

/** GET /api/v1/customer/pickup-requests?page=&pageSize=&status= */
export async function getMyPickupRequests(
  page = 1,
  pageSize = 20,
  status?: string,
): Promise<{ list: PickupRequestDto[]; hasPreviousPage: boolean; hasNextPage: boolean }> {
  const res = await ordersClient.get<PaginatedListResponse<PickupRequestDto>>(
    '/customer/pickup-requests/',
    {
      params: {
        page,
        pageSize,
        ...(status ? { status } : {}),
      },
    },
  );
  return unwrapPaginated(res.data);
}

/** GET /api/v1/customer/pickup-requests/{id} */
export async function getMyPickupRequestById(id: string): Promise<PickupRequestDto> {
  const res = await ordersClient.get<SingleResponse<PickupRequestDto>>(
    `/customer/pickup-requests/${id}`,
  );
  return unwrapSingle(res.data);
}

/** POST /api/v1/customer/pickup-requests/{id}/reschedule */
export async function reschedulePickup(
  id: string,
  req: ReschedulePickupRequestBody,
): Promise<PickupRequestDto> {
  const res = await ordersClient.post<SingleResponse<PickupRequestDto>>(
    `/customer/pickup-requests/${id}/reschedule`,
    req,
  );
  return unwrapSingle(res.data);
}

// ── Coupon preview ────────────────────────────────────────────────────────────

/**
 * POST /api/v1/customer/coupons/validate
 * Preview-only — does not create a redemption record.
 */
export async function validateCouponForPickup(
  req: ValidateCouponForPickupRequest,
): Promise<CouponPreviewResult> {
  const res = await ordersClient.post<SingleResponse<CouponPreviewResult>>(
    '/customer/coupons/validate',
    req,
  );
  return unwrapSingle(res.data);
}

// ── Delivery slots ────────────────────────────────────────────────────────────

/** GET /api/v1/customer/delivery-slots?storeId=&date= */
export async function getDeliverySlots(
  storeId?: string,
  date?: string,
): Promise<DeliverySlotDto[]> {
  const res = await ordersClient.get<ListResponse<DeliverySlotDto>>(
    '/customer/delivery-slots/',
    { params: { ...(storeId && { storeId }), ...(date && { date }) } },
  );
  return unwrapList(res.data);
}
