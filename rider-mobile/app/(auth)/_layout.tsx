import { Stack } from 'expo-router';
import React from 'react';

export default function AuthLayout() {
  return (
    <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: '#F3EEE3' } }}>
      <Stack.Screen name="login" />
      <Stack.Screen name="otp" />
    </Stack>
  );
}
