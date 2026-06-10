/**
 * Authenticated app layout — guards against unauthenticated access and hosts
 * the tab group plus all pushed (stack) screens: the booking flow, order
 * detail/tracking, price list and offers.
 */
import React, { useEffect } from 'react';
import { Stack, useRouter } from 'expo-router';
import { useAuthStore } from '@/store/authStore';
import { initialisePushNotifications } from '@/lib/pushNotifications';

const CREAM = '#F3EEE3';

export default function AppLayout() {
  const { accessToken, isHydrated } = useAuthStore();
  const router = useRouter();

  useEffect(() => {
    if (isHydrated && !accessToken) {
      router.replace('/(auth)/onboarding');
    }
  }, [accessToken, isHydrated, router]);

  // Register for push notifications when the customer is authenticated.
  // Best-effort — failures never block navigation or render.
  useEffect(() => {
    if (isHydrated && accessToken) {
      void initialisePushNotifications();
    }
  }, [isHydrated, accessToken]);

  return (
    <Stack
      screenOptions={{
        headerShown: false,
        contentStyle: { backgroundColor: CREAM },
        animation: 'slide_from_right',
      }}
    >
      <Stack.Screen name="(tabs)" />
      <Stack.Screen name="price-list" />
      <Stack.Screen name="booking/items" />
      <Stack.Screen name="booking/pickup" />
      <Stack.Screen name="booking/pay" />
      <Stack.Screen
        name="booking/confirm"
        options={{ animation: 'fade', gestureEnabled: false }}
      />
      <Stack.Screen name="orders/[id]" />
      <Stack.Screen name="orders/tracking/[id]" />
      <Stack.Screen name="offers" />
    </Stack>
  );
}
