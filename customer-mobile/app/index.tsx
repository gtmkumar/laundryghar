/**
 * Entry redirect — send users to the correct route group
 * based on whether they have a valid token in the auth store.
 * Returning (already-onboarded) logged-out users skip the carousel.
 */
import { Redirect } from 'expo-router';
import { useAuthStore } from '@/store/authStore';

export default function Index() {
  const { accessToken, isHydrated, hasOnboarded } = useAuthStore();

  if (!isHydrated) return null;

  if (accessToken) {
    return <Redirect href="/(app)/(tabs)/home" />;
  }

  return <Redirect href={hasOnboarded ? '/(auth)/phone' : '/(auth)/onboarding'} />;
}
