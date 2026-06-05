/**
 * Entry redirect — send riders to the correct route group
 * based on whether they have a valid token in the auth store.
 */
import { Redirect } from 'expo-router';
import { useAuthStore } from '@/store/authStore';

export default function Index() {
  const { accessToken, isHydrated } = useAuthStore();

  if (!isHydrated) return null;

  if (accessToken) {
    return <Redirect href="/(app)/(tabs)/assignments" />;
  }

  return <Redirect href="/(auth)/login" />;
}
