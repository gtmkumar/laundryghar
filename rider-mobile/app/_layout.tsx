/**
 * Root layout — bootstraps:
 *   1. axios auth wiring (configureApiAuth)
 *   2. SecureStore (auth) + AsyncStorage (duty) hydration
 *   3. TanStack Query provider
 *   4. Branded splash while hydrating, then the (auth)/(app) stacks
 */
import '../global.css';
import React, { useEffect } from 'react';
import { Stack } from 'expo-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { bootstrapApiAuth, useAuthStore } from '@/store/authStore';
import { useDutyStore } from '@/store/dutyStore';
import { BrandSplash } from '@/components/BrandSplash';
// Side-effect import: registers the background-location TaskManager task at app
// launch (it must be defined before the OS can deliver a cold background event).
import '@/lib/backgroundLocation';

// Bootstrap once — wires axios interceptors into auth store
bootstrapApiAuth();

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry:     2,
      staleTime: 30_000,
    },
  },
});

export default function RootLayout() {
  const { hydrate, isHydrated } = useAuthStore();
  const hydrateDuty = useDutyStore((s) => s.hydrate);

  useEffect(() => {
    hydrate();
    hydrateDuty();
  }, [hydrate, hydrateDuty]);

  if (!isHydrated) {
    return <BrandSplash />;
  }

  return (
    <GestureHandlerRootView style={{ flex: 1 }}>
      <SafeAreaProvider>
        <QueryClientProvider client={queryClient}>
          <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: '#F3EEE3' } }}>
            <Stack.Screen name="(auth)" />
            <Stack.Screen name="(app)" />
          </Stack>
        </QueryClientProvider>
      </SafeAreaProvider>
    </GestureHandlerRootView>
  );
}
