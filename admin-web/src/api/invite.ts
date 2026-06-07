import { identityClient, unwrap } from './client'
import type { ApiResponse, InvitePreview } from '@/types/api'

/** Public: validate an invitation token and learn who it's for. */
export async function getInvitePreview(token: string): Promise<InvitePreview> {
  const { data } = await identityClient.get<ApiResponse<InvitePreview>>(
    `/api/v1/auth/invite/${encodeURIComponent(token)}`,
  )
  return unwrap(data)
}

/** Public: set a password against an invitation token, activating the account. */
export async function acceptInvite(token: string, newPassword: string): Promise<void> {
  await identityClient.post('/api/v1/auth/accept-invite', { token, newPassword })
}
