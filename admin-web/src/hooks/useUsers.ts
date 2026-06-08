import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { getUser, updateUser, changeUserRole } from '@/api/users'
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
