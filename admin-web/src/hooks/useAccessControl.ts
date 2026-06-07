import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getAccessPeople,
  getAccessRoles,
  getAccessFranchises,
  inviteUser,
  setRoleCell,
  setPersonStatus,
  type PersonStatusAction,
} from '@/api/accessControl'
import type { InviteUserPayload } from '@/types/api'
import { useEffectiveBrandId } from './useBrandContext'

const PEOPLE_PAGE_SIZE = 100

export function useAccessPeople(search?: string) {
  const brandId = useEffectiveBrandId()
  return useInfiniteQuery({
    queryKey: ['access', 'people', search ?? '', brandId],
    queryFn: ({ pageParam }) => getAccessPeople({ search, page: pageParam, pageSize: PEOPLE_PAGE_SIZE }),
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

export function useAccessFranchises() {
  const brandId = useEffectiveBrandId()
  return useInfiniteQuery({
    queryKey: ['access', 'franchises', brandId],
    queryFn: ({ pageParam }) => getAccessFranchises({ page: pageParam, pageSize: FRANCHISE_PAGE_SIZE }),
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

export function useSetPersonStatus() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { userId: string; action: PersonStatusAction; password?: string }) =>
      setPersonStatus(v.userId, v.action, v.password),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['access', 'people'] }),
  })
}
