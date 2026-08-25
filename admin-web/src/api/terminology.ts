import { identityClient, unwrap } from './client'
import type { ApiResponse, TerminologyPack } from '@/types/api'

/**
 * The per-vertical vocabulary (PLATFORM_STRATEGY.md §3 "terminology is config, not code").
 * Anonymous and shared across every brand in a vertical, so it is safe to fetch before sign-in and
 * cheap to cache hard.
 */
export async function getTerminology(verticalKey?: string): Promise<TerminologyPack> {
  const { data } = await identityClient.get<ApiResponse<TerminologyPack>>('/api/v1/terminology', {
    params: verticalKey ? { verticalKey } : undefined,
  })
  return unwrap(data)
}
