/**
 * Entry redirect — duty home when authenticated, otherwise sign-in.
 */
import { Redirect } from 'expo-router';
import { useAuthStore } from '@/store/authStore';

export default function Index() {
  const { accessToken, isHydrated } = useAuthStore();

  if (!isHydrated) return null;

  return <Redirect href={accessToken ? '/(app)/home' : '/(auth)/login'} />;
}
