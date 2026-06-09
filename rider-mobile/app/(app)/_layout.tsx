/**
 * Authenticated stack — guards unauthenticated access and hosts the duty home,
 * task list, task detail, delivered summary and profile screens. No bottom tab
 * bar (matches the v2 design — navigation is action-driven).
 */
import React, { useEffect } from 'react';
import { Stack, useRouter } from 'expo-router';
import { useAuthStore } from '@/store/authStore';
import { useLocationTracking } from '@/hooks/useLocationTracking';

export default function AppLayout() {
  const { accessToken, isHydrated } = useAuthStore();
  const router = useRouter();

  // Stream GPS pings while on duty (drives the server geofence + live map).
  useLocationTracking();

  useEffect(() => {
    if (isHydrated && !accessToken) {
      router.replace('/(auth)/login');
    }
  }, [accessToken, isHydrated, router]);

  return (
    <Stack
      screenOptions={{
        headerShown: false,
        contentStyle: { backgroundColor: '#F3EEE3' },
        animation: 'slide_from_right',
      }}
    >
      <Stack.Screen name="home" />
      <Stack.Screen name="tasks" />
      <Stack.Screen name="tasks/[id]" />
      <Stack.Screen name="delivered" options={{ animation: 'fade', gestureEnabled: false }} />
      <Stack.Screen name="profile" options={{ presentation: 'card' }} />
    </Stack>
  );
}
