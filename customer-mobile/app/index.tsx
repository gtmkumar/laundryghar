/**
 * Entry redirect — send users to the correct route group.
 *
 * Order matters:
 *   1. A live session goes straight to the dashboard.
 *   2. A known-but-signed-out account with a PIN gets the unlock screen, so returning
 *      users are not made to request a fresh OTP.
 *   3. Everyone else lands on sign-in (or the carousel, first time only).
 */
import { Redirect } from 'expo-router';
import { useAuthStore } from '@/store/authStore';
import { FEATURES } from '@/constants/config';

export default function Index() {
  const { accessToken, isHydrated, hasOnboarded, hasPin, lastIdentifier } = useAuthStore();

  if (!isHydrated) return null;

  if (accessToken) {
    return <Redirect href="/(app)/(tabs)/home" />;
  }

  // Both flags are needed: /pin/verify checks (identifier, pin), so a remembered PIN
  // with no remembered identifier has nothing to verify against.
  if (FEATURES.pinUnlock && hasPin && lastIdentifier) {
    return <Redirect href="/(auth)/unlock" />;
  }

  return <Redirect href={hasOnboarded ? '/(auth)/phone' : '/(auth)/onboarding'} />;
}
