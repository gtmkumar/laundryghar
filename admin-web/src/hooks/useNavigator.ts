import { useQuery } from '@tanstack/react-query'
import { getNavigator } from '@/api/navigator'
import { useAuthStore } from '@/stores/authStore'

/** Loads the data-driven sidebar menu for the signed-in user. */
export function useNavigator() {
  const accessToken = useAuthStore((s) => s.accessToken)
  return useQuery({
    queryKey: ['navigator', accessToken],
    queryFn: getNavigator,
    enabled: !!accessToken,
    staleTime: 5 * 60_000,
  })
}
