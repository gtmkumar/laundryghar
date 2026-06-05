/**
 * Authenticated app layout — guards against unauthenticated access.
 * Wraps (tabs) and any modal/stack screens pushed from tabs.
 */
import React, { useEffect } from 'react';
import { Stack, useRouter } from 'expo-router';
import { useAuthStore } from '@/store/authStore';

export default function AppLayout() {
  const { accessToken, isHydrated } = useAuthStore();
  const router = useRouter();

  useEffect(() => {
    if (isHydrated && !accessToken) {
      router.replace('/(auth)/onboarding');
    }
  }, [accessToken, isHydrated, router]);

  return (
    <Stack screenOptions={{ headerShown: false }}>
      <Stack.Screen name="(tabs)" />
      <Stack.Screen
        name="orders/[id]"
        options={{
          headerShown: true,
          headerTitle: 'Order Details',
          headerBackTitle: 'Back',
          presentation: 'card',
        }}
      />
      <Stack.Screen
        name="orders/tracking/[id]"
        options={{
          headerShown: true,
          headerTitle: 'Track Order',
          headerBackTitle: 'Back',
          presentation: 'card',
        }}
      />
    </Stack>
  );
}
