import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { getUser, updateUser, changeUserRole, setUserType, resendInvite } from '@/api/users'
import type { UpdateUserPayload, ChangeRolePayload } from '@/types/api'

export function useUser(id: string | null) {
  return useQuery({
    queryKey: ['admin-user', id],
    queryFn: () => getUser(id as string),
    enabled: !!id,
  })
}

export function useUpdateUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { id: string; payload: UpdateUserPayload }) => updateUser(v.id, v.payload),
    onSuccess: (data) => {
      qc.setQueryData(['admin-user', data.id], data)
      // The People list shows name/email — refresh it.
      qc.invalidateQueries({ queryKey: ['access', 'people'] })
    },
  })
}

export function useChangeUserRole() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { id: string; payload: ChangeRolePayload }) => changeUserRole(v.id, v.payload),
    onSuccess: (_data, v) => {
      // Role badge + scope live on the People list; the detail may also reflect it.
      qc.invalidateQueries({ queryKey: ['access', 'people'] })
      qc.invalidateQueries({ queryKey: ['access', 'franchises'] })
      qc.invalidateQueries({ queryKey: ['admin-user', v.id] })
    },
  })
}

export function useSetUserType() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { id: string; newUserType: string }) => setUserType(v.id, v.newUserType),
    onSuccess: (_data, v) => {
      // The account type shows on the detail drawer and the People directory "Type" column.
      qc.invalidateQueries({ queryKey: ['admin-user', v.id] })
      qc.invalidateQueries({ queryKey: ['access', 'people'] })
    },
  })
}

export function useResendInvite() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => resendInvite(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['access', 'people'] }),
  })
}
