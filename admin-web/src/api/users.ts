import { identityClient, unwrap } from './client'
import type { ApiResponse, AdminUserDetail, UpdateUserPayload, ChangeRolePayload, MembershipDto } from '@/types/api'

const BASE = '/api/v1/admin/users'

export async function getUser(id: string): Promise<AdminUserDetail> {
  const { data } = await identityClient.get<ApiResponse<AdminUserDetail>>(`${BASE}/${id}`)
  return unwrap(data)
}

export async function updateUser(id: string, payload: UpdateUserPayload): Promise<AdminUserDetail> {
  const { data } = await identityClient.put<ApiResponse<AdminUserDetail>>(`${BASE}/${id}`, payload)
  return unwrap(data)
}

export async function changeUserRole(id: string, payload: ChangeRolePayload): Promise<MembershipDto> {
  const { data } = await identityClient.post<ApiResponse<MembershipDto>>(`${BASE}/${id}/change-role`, payload)
  return unwrap(data)
}

/**
 * Change a user's coarse account type (privileged; requires `users.set_type`). Used to migrate a
 * mislabeled account — e.g. a laundry `warehouse_staff` to the neutral `ops_staff` on a salon brand.
 */
export async function setUserType(id: string, newUserType: string): Promise<void> {
  await identityClient.post(`${BASE}/${id}/set-type`, { newUserType })
}

/** Re-send the invitation email to a still-pending (invited) user; rotates their invite token. */
export async function resendInvite(id: string): Promise<void> {
  await identityClient.post(`${BASE}/${id}/resend-invite`)
}
