/**
 * useMe — the single source of truth for the customer's identity
 * (GET {Identity}/customer/auth/me) shared by Home (greeting) and Profile.
 *
 * Mirrors the result into the auth store so legacy readers stay in sync, and
 * is invalidated by the profile edit mutation (queryKey ['customer', 'me']).
 */
import { useQuery } from '@tanstack/react-query';
import { getMe } from '@/api/auth';
import { useAuthStore } from '@/store/authStore';

export const meKey = ['customer', 'me'] as const;

export function useMe() {
  const accessToken = useAuthStore((s) => s.accessToken);
  return useQuery({
    queryKey: meKey,
    queryFn: async () => {
      const data = await getMe();
      useAuthStore.getState().setCustomer(data);
      return data;
    },
    enabled: !!accessToken,
    staleTime: 5 * 60_000,
  });
}
