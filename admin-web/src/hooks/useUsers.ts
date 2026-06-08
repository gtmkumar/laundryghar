import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { getUser, updateUser } from '@/api/users'
import type { UpdateUserPayload } from '@/types/api'

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
