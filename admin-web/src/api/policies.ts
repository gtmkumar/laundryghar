import { identityClient, unwrap } from './client'
import type { ApiResponse, AbacPolicy, AbacDecision, PolicyEditPayload } from '@/types/api'

const BASE = '/api/v1/admin/policies'

export async function getPolicies(params: { resourceType?: string; search?: string } = {}): Promise<AbacPolicy[]> {
  const { data } = await identityClient.get<ApiResponse<AbacPolicy[]>>(BASE, { params })
  return unwrap(data)
}

/**
 * The explain feed (A8.2). Denies only by default: a permit tells you nothing you did not already
 * know from the request succeeding, whereas a deny is the thing somebody is trying to understand.
 */
export async function getDecisions(
  params: { userId?: string; deniesOnly?: boolean; limit?: number } = {},
): Promise<AbacDecision[]> {
  const { data } = await identityClient.get<ApiResponse<AbacDecision[]>>(`${BASE}/decisions`, {
    params: { deniesOnly: true, limit: 100, ...params },
  })
  return unwrap(data)
}

export async function updatePolicy(id: string, payload: PolicyEditPayload): Promise<void> {
  await identityClient.patch(`${BASE}/${id}`, payload)
}
