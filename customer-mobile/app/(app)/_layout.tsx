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
  const { accessToken, isHydrated, hasOnboarded } = useAuthStore();
  const router = useRouter();

  useEffect(() => {
    if (isHydrated && !accessToken) {
      // Returning users have already seen the carousel — go straight to login.
      router.replace(hasOnboarded ? '/(auth)/phone' : '/(auth)/onboarding');
    }
  }, [accessToken, isHydrated, hasOnboarded, router]);

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
      <Stack.Screen name="parcel/pickup" />
      <Stack.Screen name="parcel/drop" />
      <Stack.Screen name="parcel/vehicle" />
      <Stack.Screen name="parcel/quote" />
      <Stack.Screen name="orders/[id]" />
      <Stack.Screen name="orders/tracking/[id]" />
      <Stack.Screen name="support/index" />
      <Stack.Screen name="support/[id]" />
      <Stack.Screen name="notifications" />
      <Stack.Screen name="offers" />
    </Stack>
  );
}
