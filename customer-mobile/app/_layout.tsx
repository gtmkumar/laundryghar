/**
 * Root layout — bootstraps:
 *   1. axios auth wiring (configureApiAuth)
 *   2. SecureStore hydration
 *   3. TanStack Query provider
 *   4. App-config fetch (maintenance / force-update gate)
 *   5. Redirects: unauthenticated → /(auth)/onboarding | authenticated → /(app)/(tabs)/home
 */
import '../global.css';
import React, { useEffect } from 'react';
import { Platform, Text, View } from 'react-native';
import { Stack } from 'expo-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { SafeAreaView } from 'react-native-safe-area-context';
import { bootstrapApiAuth, useAuthStore } from '@/store/authStore';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { StatusBar } from 'expo-status-bar';
import { useAppConfig } from '@/hooks/useEngagement';
import type { AppSettingsConfigValue } from '@/types/api';

// Bootstrap once — wires axios interceptors into auth store
bootstrapApiAuth();

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 2,
      staleTime: 30_000,
    },
  },
});

// ---------------------------------------------------------------------------
// App-config gate — rendered inside QueryClientProvider so it can use hooks
// ---------------------------------------------------------------------------

function AppConfigGate({ children }: { children: React.ReactNode }) {
  const { data: configRows } = useAppConfig();

  // Parse the app_settings row defensively — never crash if absent/malformed
  const appSettings = React.useMemo<AppSettingsConfigValue>(() => {
    if (!configRows) return {};
    const row = configRows.find((r) => r.configKey === 'app_settings');
    if (!row) return {};
    try {
      return JSON.parse(row.configValue) as AppSettingsConfigValue;
    } catch {
      return {};
    }
  }, [configRows]);

  // Maintenance mode — show a blocking screen
  if (appSettings.maintenance_mode === true) {
    return (
      <SafeAreaView style={{ flex: 1, backgroundColor: '#1D4ED8' }}>
        <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', padding: 32 }}>
          <Text style={{ fontSize: 28, fontWeight: 'bold', color: '#fff', textAlign: 'center', marginBottom: 12 }}>
            Under Maintenance
          </Text>
          <Text style={{ fontSize: 16, color: '#BFDBFE', textAlign: 'center', lineHeight: 24 }}>
            We are performing scheduled maintenance. Please check back shortly.
          </Text>
        </View>
      </SafeAreaView>
    );
  }

  // Force update — compare current app version against force_update_version
  // Uses the Expo runtime version / app version from Constants if available.
  // Kept minimal: only blocks if the field is present and non-empty.
  const forceVersion = appSettings.force_update_version;
  const currentVersion = Platform.OS === 'ios' ? '1.0.0' : '1.0.0'; // matches app.config.ts version
  if (forceVersion && forceVersion.trim() && semverGt(forceVersion, currentVersion)) {
    return (
      <SafeAreaView style={{ flex: 1, backgroundColor: '#1D4ED8' }}>
        <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', padding: 32 }}>
          <Text style={{ fontSize: 28, fontWeight: 'bold', color: '#fff', textAlign: 'center', marginBottom: 12 }}>
            Update Required
          </Text>
          <Text style={{ fontSize: 16, color: '#BFDBFE', textAlign: 'center', lineHeight: 24 }}>
            A new version of Laundry Ghar is available. Please update the app to continue.
          </Text>
        </View>
      </SafeAreaView>
    );
  }

  return <>{children}</>;
}

/**
 * Minimal semver greater-than check: returns true when `a` > `b`.
 * Compares major.minor.patch numerically. Does NOT handle pre-release tags.
 */
function semverGt(a: string, b: string): boolean {
  const parse = (v: string) => v.split('.').map((n) => parseInt(n, 10) || 0);
  const [aMaj, aMin, aPat] = parse(a);
  const [bMaj, bMin, bPat] = parse(b);
  if (aMaj !== bMaj) return aMaj > bMaj;
  if (aMin !== bMin) return aMin > bMin;
  return aPat > bPat;
}

// ---------------------------------------------------------------------------
// Root layout
// ---------------------------------------------------------------------------

export default function RootLayout() {
  const { hydrate, isHydrated } = useAuthStore();

  useEffect(() => {
    hydrate();
  }, [hydrate]);

  if (!isHydrated) {
    return <ScreenLoader />;
  }

  return (
    <GestureHandlerRootView style={{ flex: 1 }}>
      <SafeAreaProvider>
        <QueryClientProvider client={queryClient}>
          <StatusBar style="auto" />
          <AppConfigGate>
            <Stack screenOptions={{ headerShown: false }}>
              <Stack.Screen name="(auth)" />
              <Stack.Screen name="(app)" />
            </Stack>
          </AppConfigGate>
        </QueryClientProvider>
      </SafeAreaProvider>
    </GestureHandlerRootView>
  );
}
