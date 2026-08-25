import { identityClient, unwrap } from './client'
import type { ApiResponse, BrandDomain, VerifyBrandDomainResult } from '@/types/api'

/**
 * Custom domains for white-label tier T2 (PLATFORM_STRATEGY.md §4.2): a provider serves their
 * business on their own hostname. Gated on brands.read / brands.update — a domain is the brand's
 * own branding, which §6 Law 1 puts with the Owner.
 */
const BASE = (brandId: string) => `/api/v1/admin/brands/${brandId}/domains`

export async function getBrandDomains(brandId: string): Promise<BrandDomain[]> {
  const { data } = await identityClient.get<ApiResponse<BrandDomain[]>>(BASE(brandId))
  return unwrap(data)
}

/** Registers the domain UNVERIFIED and returns the DNS records the provider must publish. */
export async function addBrandDomain(
  brandId: string,
  domain: string,
  isPrimary = false,
): Promise<BrandDomain> {
  const { data } = await identityClient.post<ApiResponse<BrandDomain>>(BASE(brandId), { domain, isPrimary })
  return unwrap(data)
}

/**
 * Runs the DNS check. Resolves (not rejects) when the domain is simply not verified yet — "the
 * record isn't there" is an ordinary outcome of this flow, so the caller reads `verified`/`status`
 * rather than catching.
 */
export async function verifyBrandDomain(
  brandId: string,
  domainId: string,
): Promise<VerifyBrandDomainResult> {
  const { data } = await identityClient.post<ApiResponse<VerifyBrandDomainResult>>(
    `${BASE(brandId)}/${domainId}/verify`,
  )
  return unwrap(data)
}

export async function deleteBrandDomain(brandId: string, domainId: string): Promise<void> {
  await identityClient.delete(`${BASE(brandId)}/${domainId}`)
}
