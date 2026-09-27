import { identityClient, unwrap, unwrapPaginated } from './client'
import type {
  ApiResponse,
  PaginatedList,
  PaginationParams,
  AccessPeoplePage,
  AccessRoles,
  AccessRoleSummary,
  AccessFranchise,
  InviteUserPayload,
  SetPersonStatusResult,
  CreateRolePayload,
  UpdateRolePayload,
  CloneRolePayload,
  RoleCellChange,
  PermissionCatalogItem,
  SetUserPermissionOverridePayload,
  GrantMembershipPayload,
  RevokeMembershipPayload,
  MembershipDto,
  PersonMembershipDto,
  PersonPermissionOverrideDto,
} from '@/types/api'

export type PersonStatusAction = 'activate' | 'suspend' | 'reactivate'

const BASE = '/api/v1/admin/access-control'
// Membership grant/revoke + the permission catalog live under a different route group.
const ROLES_BASE = '/api/v1/admin/roles'

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

/** Apply many cell changes to a role in a single atomic request. */
export async function setRoleCells(roleId: string, changes: RoleCellChange[]): Promise<void> {
  await identityClient.post(`${BASE}/roles/${roleId}/cells`, { changes })
}

// ── Role CRUD (UI-managed custom roles) ──────────────────────────────────────
export async function createRole(payload: CreateRolePayload): Promise<AccessRoleSummary> {
  const { data } = await identityClient.post<ApiResponse<AccessRoleSummary>>(`${BASE}/roles`, payload)
  return unwrap(data)
}
export async function updateRole(roleId: string, payload: UpdateRolePayload): Promise<void> {
  await identityClient.put(`${BASE}/roles/${roleId}`, payload)
}
export async function deleteRole(roleId: string): Promise<void> {
  await identityClient.delete(`${BASE}/roles/${roleId}`)
}
export async function cloneRole(roleId: string, payload: CloneRolePayload): Promise<AccessRoleSummary> {
  const { data } = await identityClient.post<ApiResponse<AccessRoleSummary>>(`${BASE}/roles/${roleId}/clone`, payload)
  return unwrap(data)
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

// ── Per-user permission overrides + additive memberships (docs/rbac.md §6/§7) ─
/** Permission catalog for the per-user override picker. Optionally filter by module. */
export async function getPermissionCatalog(module?: string): Promise<PermissionCatalogItem[]> {
  const { data } = await identityClient.get<ApiResponse<PermissionCatalogItem[]>>(
    `${ROLES_BASE}/permissions`,
    { params: module ? { module } : undefined },
  )
  return unwrap(data)
}

/**
 * Every live permission override a person already holds.
 *
 * Added for audit finding A-4 ("blind writes"); the ABAC plan had named the same gap independently
 * as task A8.2 — `user_permission_override` was write-only, with no query to read it back.
 */
export async function getPersonPermissionOverrides(
  personId: string,
): Promise<PersonPermissionOverrideDto[]> {
  const { data } = await identityClient.get<ApiResponse<PersonPermissionOverrideDto[]>>(
    `${BASE}/people/${personId}/permission-overrides`,
  )
  return unwrap(data)
}

/** Set (allow/deny) or clear (effect:null) a per-user permission override for one person. */
export async function setUserPermissionOverride(
  personId: string,
  payload: SetUserPermissionOverridePayload,
): Promise<void> {
  await identityClient.post(`${BASE}/people/${personId}/permission-override`, payload)
}

/**
 * Every live membership a person already holds.
 *
 * Added for audit finding A-4 ("blind writes"): grant and revoke existed, nothing could list, so
 * this panel could only revoke what the current browser session had just granted.
 */
export async function getPersonMemberships(personId: string): Promise<PersonMembershipDto[]> {
  const { data } = await identityClient.get<ApiResponse<PersonMembershipDto[]>>(
    `${BASE}/people/${personId}/memberships`,
  )
  return unwrap(data)
}

/** Grant an additional multi-scope membership; returns the created membership (id needed to revoke it). */
export async function grantMembership(payload: GrantMembershipPayload): Promise<MembershipDto> {
  const { data } = await identityClient.post<ApiResponse<MembershipDto>>(
    `${ROLES_BASE}/memberships/grant`,
    payload,
  )
  return unwrap(data)
}

/** Revoke a single membership by id. */
export async function revokeMembership(payload: RevokeMembershipPayload): Promise<void> {
  await identityClient.post(`${ROLES_BASE}/memberships/revoke`, payload)
}
