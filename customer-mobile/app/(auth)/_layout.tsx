import { Stack } from 'expo-router';
import React from 'react';

export default function AuthLayout() {
  return (
    <Stack
      screenOptions={{
        headerShown: false,
        contentStyle: { backgroundColor: '#F3EEE3' },
      }}
    >
      <Stack.Screen name="onboarding" />
      <Stack.Screen name="phone" />
      <Stack.Screen name="otp" />
      {/* Post-Google sign-up: optional phone, then optional PIN. Both are skippable,
          and both leave the customer signed in either way — so gestureEnabled is off
          to stop a swipe-back landing them on a login screen they already passed. */}
      <Stack.Screen name="link-phone" options={{ gestureEnabled: false }} />
      <Stack.Screen name="secure" options={{ gestureEnabled: false }} />
      {/* Returning-user unlock (PIN / biometric). */}
      <Stack.Screen name="unlock" options={{ gestureEnabled: false }} />
    </Stack>
  );
}
