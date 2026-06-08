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
