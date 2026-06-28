import { identityClient, unwrap } from './client'
import type { ApiResponse, BrandEntitlements, ModuleBundle, BrandPlatformSubscription, PlatformBillingSummary } from '@/types/api'

const BASE = '/api/v1/admin/entitlements'

/** Platform-wide SaaS revenue summary (operator MRR view). */
export async function getPlatformBillingSummary(): Promise<PlatformBillingSummary> {
  const { data } = await identityClient.get<ApiResponse<PlatformBillingSummary>>(`${BASE}/platform-billing`)
  return unwrap(data)
}

export async function getBrandEntitlements(brandId: string): Promise<BrandEntitlements> {
  const { data } = await identityClient.get<ApiResponse<BrandEntitlements>>(`${BASE}/brands/${brandId}/modules`)
  return unwrap(data)
}

/** The brand's current platform tier subscription + invoices, or null if not on a priced tier. */
export async function getBrandPlatformSubscription(brandId: string): Promise<BrandPlatformSubscription | null> {
  const { data } = await identityClient.get<ApiResponse<BrandPlatformSubscription | null>>(
    `${BASE}/brands/${brandId}/platform-subscription`)
  return unwrap(data)
}

export async function getModuleBundles(): Promise<ModuleBundle[]> {
  const { data } = await identityClient.get<ApiResponse<ModuleBundle[]>>(`${BASE}/bundles`)
  return unwrap(data)
}

export async function setBrandModule(
  brandId: string,
  moduleKey: string,
  enabled: boolean,
  validUntil?: string | null,
): Promise<void> {
  await identityClient.post(`${BASE}/brands/${brandId}/modules`, { moduleKey, enabled, validUntil: validUntil ?? null })
}

export async function applyBundleToBrand(brandId: string, bundleCode: string): Promise<void> {
  await identityClient.post(`${BASE}/brands/${brandId}/apply-bundle`, { bundleCode })
}

/** Mark a brand-platform invoice 'paid' or 'void' (records a manual/offline payment, or cancels it). */
export async function setBrandPlatformInvoiceStatus(invoiceId: string, status: 'paid' | 'void'): Promise<void> {
  await identityClient.post(`${BASE}/brand-platform-invoices/${invoiceId}/status`, { status })
}

/** Create (or fetch the existing) Razorpay payment link for an invoice; returns the payable short URL. */
export async function createBrandPlatformInvoicePaymentLink(invoiceId: string): Promise<string> {
  const { data } = await identityClient.post<ApiResponse<string>>(`${BASE}/brand-platform-invoices/${invoiceId}/payment-link`)
  return unwrap(data)
}

/** Reconcile an invoice against its Razorpay link status (marks paid if the link was paid); returns the status. */
export async function syncBrandPlatformInvoicePayment(invoiceId: string): Promise<string> {
  const { data } = await identityClient.post<ApiResponse<string>>(`${BASE}/brand-platform-invoices/${invoiceId}/sync-payment`)
  return unwrap(data)
}
