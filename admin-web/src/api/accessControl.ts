import { identityClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  PaginationParams,
  AccessPeoplePage,
  AccessRoles,
  AccessFranchise,
  InviteUserPayload,
  SetPersonStatusResult,
} from '@/types/api'

export type PersonStatusAction = 'activate' | 'suspend' | 'reactivate'

const BASE = '/api/v1/admin/access-control'

export async function getAccessPeople(
  params: PaginationParams & { search?: string; franchiseId?: string; sort?: string } = {},
): Promise<AccessPeoplePage> {
  const { data } = await identityClient.get<ApiResponse<AccessPeoplePage>>(`${BASE}/people`, {
    params: { page: 1, pageSize: 100, ...params },
  })
  return unwrap(data)
}

export async function getAccessRoles(): Promise<AccessRoles> {
  const { data } = await identityClient.get<ApiResponse<AccessRoles>>(`${BASE}/roles`)
  return unwrap(data)
}

export async function getAccessFranchises(
  params: PaginationParams & { search?: string } = {},
): Promise<PaginatedList<AccessFranchise>> {
  const { data } = await identityClient.get<ApiResponse<PaginatedList<AccessFranchise>>>(
    `${BASE}/franchises`,
    { params: { page: 1, pageSize: 100, ...params } },
  )
  return unwrapPaginated(data)
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
