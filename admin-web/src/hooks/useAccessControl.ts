import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getAccessPeople,
  getAccessRoles,
  getAccessFranchises,
  inviteUser,
  setRoleCell,
  setRoleCells,
  setPersonStatus,
  createRole,
  updateRole,
  deleteRole,
  cloneRole,
  type PersonStatusAction,
} from '@/api/accessControl'
import type { InviteUserPayload, CreateRolePayload, UpdateRolePayload, CloneRolePayload, RoleCellChange } from '@/types/api'
import { useEffectiveBrandId } from './useBrandContext'

const PEOPLE_PAGE_SIZE = 100

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

export function useSetRoleCell() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { roleId: string; cellKey: string; enabled: boolean }) =>
      setRoleCell(v.roleId, v.cellKey, v.enabled),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['access', 'roles'] }),
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
    onSuccess: () => qc.invalidateQueries({ queryKey: ['access', 'people'] }),
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
