/**
 * Commerce API — maps to CustomerCommerceEndpoints.cs (Commerce service)
 * Endpoint prefix: {Commerce}/api/v1/customer/
 */
import { commerceClient, unwrapList, unwrapPaginated, unwrapSingle } from '@/api/client';
import type {
  CouponDto,
  CouponRedemptionDto,
  CustomerPackageDto,
  InitiatePaymentRequest,
  ListResponse,
  LoyaltyBalanceDto,
  PackageDto,
  PackageUsageLedgerDto,
  PaginatedListResponse,
  PaymentDto,
  PurchasePackageRequest,
  SingleResponse,
  ValidateCouponRequest,
  VerifyPaymentRequest,
  WalletAccountDto,
  WalletTopUpRequest,
  WalletTransactionDto,
} from '@/types/api';

// ── Packages ──────────────────────────────────────────────────────────────────

/** GET /api/v1/customer/packages */
export async function getAvailablePackages(): Promise<PackageDto[]> {
  const res = await commerceClient.get<ListResponse<PackageDto>>(
    '/customer/packages/',
  );
  return unwrapList(res.data);
}

/** GET /api/v1/customer/packages/my */
export async function getMyPackages(): Promise<CustomerPackageDto[]> {
  const res = await commerceClient.get<ListResponse<CustomerPackageDto>>(
    '/customer/packages/my',
  );
  return unwrapList(res.data);
}

/** GET /api/v1/customer/packages/my/{id}/usage */
export async function getPackageUsage(
  packageId: string,
  page = 1,
  pageSize = 20,
): Promise<{ list: PackageUsageLedgerDto[]; hasPreviousPage: boolean; hasNextPage: boolean }> {
  const res = await commerceClient.get<PaginatedListResponse<PackageUsageLedgerDto>>(
    `/customer/packages/my/${packageId}/usage`,
    { params: { page, pageSize } },
  );
  return unwrapPaginated(res.data);
}

/** POST /api/v1/customer/packages/purchase/initiate */
export async function initiatePackagePurchase(
  req: PurchasePackageRequest,
  idempotencyKey?: string,
): Promise<PaymentDto> {
  const res = await commerceClient.post<SingleResponse<PaymentDto>>(
    '/customer/packages/purchase/initiate',
    req,
    idempotencyKey ? { headers: { 'Idempotency-Key': idempotencyKey } } : undefined,
  );
  return unwrapSingle(res.data);
}

/** POST /api/v1/customer/packages/purchase/verify */
export async function verifyPackagePurchase(
  req: VerifyPaymentRequest,
): Promise<CustomerPackageDto> {
  const res = await commerceClient.post<SingleResponse<CustomerPackageDto>>(
    '/customer/packages/purchase/verify',
    req,
  );
  return unwrapSingle(res.data);
}

// ── Loyalty ───────────────────────────────────────────────────────────────────

/** GET /api/v1/customer/loyalty/balance */
export async function getLoyaltyBalance(): Promise<LoyaltyBalanceDto> {
  const res = await commerceClient.get<SingleResponse<LoyaltyBalanceDto>>(
    '/customer/loyalty/balance',
  );
  return unwrapSingle(res.data);
}

// ── Coupons ───────────────────────────────────────────────────────────────────

/** GET /api/v1/customer/coupons */
export async function getCoupons(): Promise<CouponDto[]> {
  const res = await commerceClient.get<ListResponse<CouponDto>>(
    '/customer/coupons/',
  );
  return unwrapList(res.data);
}

/** POST /api/v1/customer/coupons/validate-apply */
export async function validateApplyCoupon(
  req: ValidateCouponRequest,
): Promise<CouponRedemptionDto> {
  const res = await commerceClient.post<SingleResponse<CouponRedemptionDto>>(
    '/customer/coupons/validate-apply',
    req,
  );
  return unwrapSingle(res.data);
}

// ── Wallet ────────────────────────────────────────────────────────────────────

/** GET /api/v1/customer/wallet */
export async function getWallet(): Promise<WalletAccountDto> {
  const res = await commerceClient.get<SingleResponse<WalletAccountDto>>(
    '/customer/wallet/',
  );
  return unwrapSingle(res.data);
}

/** GET /api/v1/customer/wallet/transactions */
export async function getWalletTransactions(
  page = 1,
  pageSize = 20,
): Promise<{ list: WalletTransactionDto[]; hasPreviousPage: boolean; hasNextPage: boolean }> {
  const res = await commerceClient.get<PaginatedListResponse<WalletTransactionDto>>(
    '/customer/wallet/transactions',
    { params: { page, pageSize } },
  );
  return unwrapPaginated(res.data);
}

/** POST /api/v1/customer/wallet/topup/initiate */
export async function initiateWalletTopUp(
  req: WalletTopUpRequest,
  idempotencyKey?: string,
): Promise<PaymentDto> {
  const res = await commerceClient.post<SingleResponse<PaymentDto>>(
    '/customer/wallet/topup/initiate',
    req,
    idempotencyKey ? { headers: { 'Idempotency-Key': idempotencyKey } } : undefined,
  );
  return unwrapSingle(res.data);
}

// ── Payments (generic) ────────────────────────────────────────────────────────

/** POST /api/v1/customer/payments/initiate */
export async function initiatePayment(
  req: InitiatePaymentRequest,
  idempotencyKey?: string,
): Promise<PaymentDto> {
  const res = await commerceClient.post<SingleResponse<PaymentDto>>(
    '/customer/payments/initiate',
    req,
    idempotencyKey ? { headers: { 'Idempotency-Key': idempotencyKey } } : undefined,
  );
  return unwrapSingle(res.data);
}

/** POST /api/v1/customer/payments/verify */
export async function verifyPayment(
  req: VerifyPaymentRequest,
): Promise<PaymentDto> {
  const res = await commerceClient.post<SingleResponse<PaymentDto>>(
    '/customer/payments/verify',
    req,
  );
  return unwrapSingle(res.data);
}
