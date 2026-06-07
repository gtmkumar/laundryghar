import { identityClient, unwrap } from './client'
import type {
  ApiResponse,
  AccessPeople,
  AccessRoles,
  AccessFranchises,
  InviteUserPayload,
  SetPersonStatusResult,
} from '@/types/api'

export type PersonStatusAction = 'activate' | 'suspend' | 'reactivate'

const BASE = '/api/v1/admin/access-control'

export async function getAccessPeople(search?: string): Promise<AccessPeople> {
  const { data } = await identityClient.get<ApiResponse<AccessPeople>>(`${BASE}/people`, {
    params: search ? { search } : undefined,
  })
  return unwrap(data)
}

export async function getAccessRoles(): Promise<AccessRoles> {
  const { data } = await identityClient.get<ApiResponse<AccessRoles>>(`${BASE}/roles`)
  return unwrap(data)
}

export async function getAccessFranchises(): Promise<AccessFranchises> {
  const { data } = await identityClient.get<ApiResponse<AccessFranchises>>(`${BASE}/franchises`)
  return unwrap(data)
}

export async function inviteUser(payload: InviteUserPayload): Promise<void> {
  await identityClient.post(`${BASE}/invite`, payload)
}

export async function setRoleCell(roleId: string, cellKey: string, enabled: boolean): Promise<void> {
  await identityClient.post(`${BASE}/role-cell`, { roleId, cellKey, enabled })
}

export async function setPersonStatus(
  userId: string,
  action: PersonStatusAction,
  password?: string,
): Promise<SetPersonStatusResult> {
  const { data } = await identityClient.post<ApiResponse<SetPersonStatusResult>>(
    `${BASE}/people/${userId}/status`,
    { action, password },
  )
  return unwrap(data)
}
