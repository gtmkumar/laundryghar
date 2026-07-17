import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getAccessPeople,
  getAccessRoles,
  getAccessFranchises,
  inviteUser,
  setRoleCells,
  setPersonStatus,
  createRole,
  updateRole,
  deleteRole,
  cloneRole,
  getPermissionCatalog,
  setUserPermissionOverride,
  grantMembership,
  revokeMembership,
  type PersonStatusAction,
} from '@/api/accessControl'
import type {
  InviteUserPayload,
  CreateRolePayload,
  UpdateRolePayload,
  CloneRolePayload,
  RoleCellChange,
  SetUserPermissionOverridePayload,
  GrantMembershipPayload,
  RevokeMembershipPayload,
  AccessPeoplePage,
} from '@/types/api'
import { rollbackWithToast, snapshotAndSet } from '@/lib/optimistic'
import { useEffectiveBrandId } from './useBrandContext'

const PEOPLE_PAGE_SIZE = 100

/** The row status each person-status action lands on, for the optimistic flip. */
const PERSON_STATUS_BY_ACTION: Record<PersonStatusAction, string> = {
  activate: 'active',
  reactivate: 'active',
  suspend: 'suspended',
}

export function useAccessPeople(search?: string, sort?: string) {
  const brandId = useEffectiveBrandId()
  return useInfiniteQuery({
    queryKey: ['access', 'people', search ?? '', sort ?? '', brandId],
    queryFn: ({ pageParam }) => getAccessPeople({ search, sort, page: pageParam, pageSize: PEOPLE_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) => (lastPage.people.hasNextPage ? allPages.length + 1 : undefined),
    enabled: !!brandId,
  })
}

export function useAccessRoles() {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['access', 'roles', brandId],
    queryFn: getAccessRoles,
    enabled: !!brandId,
  })
}

const FRANCHISE_PAGE_SIZE = 100

export function useAccessFranchises(search?: string) {
  const brandId = useEffectiveBrandId()
  return useInfiniteQuery({
    queryKey: ['access', 'franchises', search ?? '', brandId],
    queryFn: ({ pageParam }) =>
      getAccessFranchises({ search, page: pageParam, pageSize: FRANCHISE_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) => (lastPage.hasNextPage ? allPages.length + 1 : undefined),
    enabled: !!brandId,
  })
}

export function useInviteUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: InviteUserPayload) => inviteUser(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['access', 'people'] }),
  })
}

export function useSetRoleCells() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { roleId: string; changes: RoleCellChange[] }) => setRoleCells(v.roleId, v.changes),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['access', 'roles'] }),
  })
}

export function useSetPersonStatus() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { userId: string; action: PersonStatusAction; password?: string }) =>
      setPersonStatus(v.userId, v.action, v.password),
    // Optimistic: flip the person's status badge in the cached infinite people
    // list. The pages are shaped { counts, people: { list } }, not the standard
    // { list } the list helpers understand, so patch through snapshotAndSet.
    onMutate: (v) => {
      const nextStatus = PERSON_STATUS_BY_ACTION[v.action]
      return snapshotAndSet(qc, [['access', 'people']], (data) => {
        const page = data as { pages?: AccessPeoplePage[] }
        if (!Array.isArray(page?.pages)) return data
        return {
          ...page,
          pages: page.pages.map((pg) => ({
            ...pg,
            people: {
              ...pg.people,
              list: pg.people.list.map((person) =>
                person.id === v.userId ? { ...person, status: nextStatus } : person,
              ),
            },
          })),
        }
      })
    },
    onError: (error, _v, ctx) => rollbackWithToast(ctx, error),
    onSettled: () => qc.invalidateQueries({ queryKey: ['access', 'people'] }),
  })
}

// ── Role CRUD ────────────────────────────────────────────────────────────────
export function useCreateRole() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateRolePayload) => createRole(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['access', 'roles'] }),
  })
}
export function useUpdateRole() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { roleId: string; payload: UpdateRolePayload }) => updateRole(v.roleId, v.payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['access', 'roles'] }),
  })
}
export function useDeleteRole() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (roleId: string) => deleteRole(roleId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['access', 'roles'] }),
  })
}
export function useCloneRole() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { roleId: string; payload: CloneRolePayload }) => cloneRole(v.roleId, v.payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['access', 'roles'] }),
  })
}

// ── Per-user overrides + additive memberships ────────────────────────────────
/** The permission catalog (module/action/name/riskLevel) for the override picker.
 *  Static-ish, so cache it a while; pass `enabled` to defer until the panel is shown. */
export function usePermissionCatalog(enabled = true) {
  return useQuery({
    queryKey: ['access', 'permission-catalog'],
    queryFn: () => getPermissionCatalog(),
    enabled,
    staleTime: 5 * 60_000,
  })
}

/** Set/clear a per-user permission override; refreshes the person's detail. */
export function useSetUserPermissionOverride() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { personId: string; payload: SetUserPermissionOverridePayload }) =>
      setUserPermissionOverride(v.personId, v.payload),
    onSuccess: (_data, v) => {
      qc.invalidateQueries({ queryKey: ['admin-user', v.personId] })
    },
  })
}

/** Grant an additional membership; refreshes the person + the People/Franchises lists. */
export function useGrantMembership() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: GrantMembershipPayload) => grantMembership(payload),
    onSuccess: (_data, v) => {
      qc.invalidateQueries({ queryKey: ['admin-user', v.userId] })
      qc.invalidateQueries({ queryKey: ['access', 'people'] })
      qc.invalidateQueries({ queryKey: ['access', 'franchises'] })
    },
  })
}

/** Revoke a membership; `userId` is passed through only to scope the cache invalidation. */
export function useRevokeMembership() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { userId: string; payload: RevokeMembershipPayload }) => revokeMembership(v.payload),
    onSuccess: (_data, v) => {
      qc.invalidateQueries({ queryKey: ['admin-user', v.userId] })
      qc.invalidateQueries({ queryKey: ['access', 'people'] })
    },
  })
}
