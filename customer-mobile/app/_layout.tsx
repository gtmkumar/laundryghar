/**
 * Root layout — bootstraps:
 *   1. axios auth wiring (configureApiAuth)
 *   2. SecureStore hydration
 *   3. TanStack Query provider
 *   4. App-config fetch (maintenance / force-update gate)
 */
import '../global.css';
import React, { useEffect } from 'react';
import { Text, View } from 'react-native';
import { Stack } from 'expo-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaProvider, SafeAreaView } from 'react-native-safe-area-context';
import { StatusBar } from 'expo-status-bar';
import { bootstrapApiAuth, useAuthStore } from '@/store/authStore';
import { BrandSplash } from '@/components/BrandSplash';
import { useAppConfig } from '@/hooks/useEngagement';
import type { AppSettingsConfigValue } from '@/types/api';

// Bootstrap once — wires axios interceptors into auth store
bootstrapApiAuth();

const CREAM = '#F3EEE3';

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

function BlockingScreen({ title, body }: { title: string; body: string }) {
  return (
    <SafeAreaView style={{ flex: 1, backgroundColor: '#4A552A' }}>
      <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', padding: 32 }}>
        <Text style={{ fontSize: 28, fontWeight: 'bold', color: '#fff', textAlign: 'center', marginBottom: 12 }}>
          {title}
        </Text>
        <Text style={{ fontSize: 16, color: '#E3E7D0', textAlign: 'center', lineHeight: 24 }}>
          {body}
        </Text>
      </View>
    </SafeAreaView>
  );
}

function AppConfigGate({ children }: { children: React.ReactNode }) {
  const { data: configRows } = useAppConfig();

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

  if (appSettings.maintenance_mode === true) {
    return (
      <BlockingScreen
        title="Under Maintenance"
        body="We are performing scheduled maintenance. Please check back shortly."
      />
    );
  }

  const forceVersion = appSettings.force_update_version;
  const currentVersion = '2.0.0'; // matches app.config.ts version
  if (forceVersion && forceVersion.trim() && semverGt(forceVersion, currentVersion)) {
    return (
      <BlockingScreen
        title="Update Required"
        body="A new version of Laundry Ghar is available. Please update the app to continue."
      />
    );
  }

  return <>{children}</>;
}

/** Minimal semver greater-than check: returns true when `a` > `b`. */
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
    return <BrandSplash />;
  }

  return (
    <GestureHandlerRootView style={{ flex: 1 }}>
      <SafeAreaProvider>
        <QueryClientProvider client={queryClient}>
          <StatusBar style="dark" />
          <AppConfigGate>
            <Stack
              screenOptions={{
                headerShown: false,
                contentStyle: { backgroundColor: CREAM },
              }}
            >
              <Stack.Screen name="(auth)" />
              <Stack.Screen name="(app)" />
            </Stack>
          </AppConfigGate>
        </QueryClientProvider>
      </SafeAreaProvider>
    </GestureHandlerRootView>
  );
}
