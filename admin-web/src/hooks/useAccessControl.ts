import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getAccessPeople,
  getAccessRoles,
  getAccessFranchises,
  inviteUser,
  setRoleCell,
} from '@/api/accessControl'
import type { InviteUserPayload } from '@/types/api'
import { useEffectiveBrandId } from './useBrandContext'

export function useAccessPeople(search?: string) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['access', 'people', search ?? '', brandId],
    queryFn: () => getAccessPeople(search),
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

export function useAccessFranchises() {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['access', 'franchises', brandId],
    queryFn: getAccessFranchises,
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
