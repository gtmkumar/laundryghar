import { identityClient, unwrap } from './client'
import type { ApiResponse, BrandEntitlements, ModuleBundle } from '@/types/api'

const BASE = '/api/v1/admin/entitlements'

export async function getBrandEntitlements(brandId: string): Promise<BrandEntitlements> {
  const { data } = await identityClient.get<ApiResponse<BrandEntitlements>>(`${BASE}/brands/${brandId}/modules`)
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
