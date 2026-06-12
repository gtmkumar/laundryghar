/**
 * Authenticated stack — guards unauthenticated access and hosts the duty home,
 * task list, task detail, delivered summary and profile screens. No bottom tab
 * bar (matches the v2 design — navigation is action-driven).
 */
import React, { useEffect, useRef } from 'react';
import { View } from 'react-native';
import { Stack, useRouter } from 'expo-router';
import { useAuthStore } from '@/store/authStore';
import { useLocationTracking } from '@/hooks/useLocationTracking';
import { initialisePushNotifications } from '@/lib/pushNotifications';
import { OfflineBanner } from '@/components/ui/OfflineBanner';
import { useOfflineQueueFlush } from '@/hooks/useOfflineQueueFlush';
import { useNetworkStatus } from '@/hooks/useNetworkStatus';

export default function AppLayout() {
  const { accessToken, isHydrated } = useAuthStore();
  const router = useRouter();
  const { flushOfflineQueue } = useOfflineQueueFlush();
  const { isConnected } = useNetworkStatus();
  // Track previous connectivity so we only flush on the offline→online transition.
  const prevConnected = useRef<boolean | null>(null);

  // Stream GPS pings while on duty (drives the server geofence + live map).
  useLocationTracking();

  useEffect(() => {
    if (isHydrated && !accessToken) {
      router.replace('/(auth)/login');
    }
  }, [accessToken, isHydrated, router]);

  // Flush offline queue immediately after auth tokens are hydrated (app cold-start).
  useEffect(() => {
    if (isHydrated && accessToken) {
      void flushOfflineQueue();
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isHydrated, accessToken]);

  // Flush on reconnect (offline → online transition).
  useEffect(() => {
    if (isConnected === true && prevConnected.current === false) {
      void flushOfflineQueue();
    }
    prevConnected.current = isConnected;
  }, [isConnected, flushOfflineQueue]);

  // Register for push notifications when the rider is authenticated.
  // Best-effort — failures never block navigation or render.
  useEffect(() => {
    if (isHydrated && accessToken) {
      void initialisePushNotifications();
    }
  }, [isHydrated, accessToken]);

  return (
    <View style={{ flex: 1 }}>
      {/* Offline indicator — appears on every authenticated screen */}
      <OfflineBanner />
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
        <Stack.Screen name="earnings" options={{ presentation: 'card' }} />
        <Stack.Screen name="cash" options={{ presentation: 'card' }} />
        <Stack.Screen name="inspection/[id]" options={{ presentation: 'card' }} />
        <Stack.Screen name="notifications" options={{ presentation: 'card' }} />
      </Stack>
    </View>
  );
}
