/**
 * Authenticated app layout — guards against unauthenticated access.
 * Wraps (tabs) and any stack screens pushed from tabs (e.g. assignment detail).
 */
import React, { useEffect } from 'react';
import { Stack, useRouter } from 'expo-router';
import { useAuthStore } from '@/store/authStore';

export default function AppLayout() {
  const { accessToken, isHydrated } = useAuthStore();
  const router = useRouter();

  useEffect(() => {
    if (isHydrated && !accessToken) {
      router.replace('/(auth)/login');
    }
  }, [accessToken, isHydrated, router]);

  return (
    <Stack screenOptions={{ headerShown: false }}>
      <Stack.Screen name="(tabs)" />
      <Stack.Screen
        name="assignments/[id]"
        options={{
          headerShown:    true,
          headerTitle:    'Assignment',
          headerBackTitle: 'Back',
          presentation:   'card',
          headerStyle:    { backgroundColor: '#15803D' },
          headerTintColor: '#FFFFFF',
          headerTitleStyle: { color: '#FFFFFF', fontWeight: 'bold' },
        }}
      />
    </Stack>
  );
}
