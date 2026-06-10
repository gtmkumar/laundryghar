/**
 * Root layout — bootstraps (in order):
 *   1. Sentry crash reporting (initialiseSentry — before any render)
 *   2. axios auth wiring (bootstrapApiAuth)
 *   3. SecureStore hydration
 *   4. TanStack Query provider
 *   5. OTA update check (non-blocking, async, post-hydration)
 *   6. App-config version gate (maintenance / force-update / soft-update)
 *   7. ErrorBoundary wrapping the app group layouts
 */
import '../global.css';
import React, { useEffect, useState } from 'react';
import { Text, View, TouchableOpacity, Linking, StyleSheet } from 'react-native';
import { Stack } from 'expo-router';
import { initI18n } from '@/i18n';
import { useTranslation } from 'react-i18next';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaProvider, SafeAreaView } from 'react-native-safe-area-context';
import { StatusBar } from 'expo-status-bar';
import { bootstrapApiAuth, useAuthStore } from '@/store/authStore';
import { BrandSplash } from '@/components/BrandSplash';
import { ErrorBoundary } from '@/components/ErrorBoundary';
import { useAppConfig } from '@/hooks/useEngagement';
import { initialiseSentry, withSentry } from '@/lib/sentry';
import { checkAndFetchOtaUpdate } from '@/lib/otaUpdates';
import { evaluateVersionGate } from '@/lib/versionGate';
import { FEATURES } from '@/constants/config';
import type { MobileAppConfigDto } from '@/types/api';

// ---------------------------------------------------------------------------
// Sentry — initialise ONCE at module-load time, before any component renders.
// This is the earliest safe point; it covers crashes during the first render.
// ---------------------------------------------------------------------------
initialiseSentry();

// Bootstrap once — wires axios interceptors into auth store
bootstrapApiAuth();

const CREAM  = '#F3EEE3';
const OLIVE  = '#4A552A';
const OLIVE_LIGHT = '#E3E7D0';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 2,
      staleTime: 30_000,
    },
  },
});

// ---------------------------------------------------------------------------
// Blocking screens (maintenance / force-update)
// ---------------------------------------------------------------------------

function BlockingScreen({
  title,
  body,
  storeUrl,
}: {
  title: string;
  body: string;
  storeUrl?: string;
}) {
  const { t } = useTranslation();
  return (
    <SafeAreaView style={blockStyles.root}>
      <View style={blockStyles.container}>
        <Text style={blockStyles.title}>{title}</Text>
        <Text style={blockStyles.body}>{body}</Text>
        {storeUrl ? (
          <TouchableOpacity
            style={blockStyles.button}
            accessibilityRole="button"
            accessibilityLabel={t('update.updateNow')}
            onPress={() => { void Linking.openURL(storeUrl); }}
          >
            <Text style={blockStyles.buttonText}>{t('update.updateNow')}</Text>
          </TouchableOpacity>
        ) : null}
      </View>
    </SafeAreaView>
  );
}

const blockStyles = StyleSheet.create({
  root: { flex: 1, backgroundColor: OLIVE },
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 32,
    gap: 16,
  },
  title: {
    fontSize: 28,
    fontWeight: 'bold',
    color: '#fff',
    textAlign: 'center',
  },
  body: {
    fontSize: 16,
    color: OLIVE_LIGHT,
    textAlign: 'center',
    lineHeight: 24,
  },
  button: {
    marginTop: 8,
    backgroundColor: OLIVE_LIGHT,
    paddingVertical: 14,
    paddingHorizontal: 40,
    borderRadius: 12,
    alignItems: 'center',
  },
  buttonText: {
    color: OLIVE,
    fontSize: 16,
    fontWeight: '700',
  },
});

// ---------------------------------------------------------------------------
// OTA update banner (non-blocking, dismissible once per session)
// ---------------------------------------------------------------------------

function OtaBanner({ onRestart }: { onRestart: () => void }) {
  const { t } = useTranslation();
  return (
    <View style={bannerStyles.root}>
      <Text style={bannerStyles.text}>{t('update.ready')}</Text>
      <TouchableOpacity
        onPress={onRestart}
        style={bannerStyles.button}
        accessibilityRole="button"
        accessibilityLabel={t('update.restart')}
      >
        <Text style={bannerStyles.buttonText}>{t('update.restart')}</Text>
      </TouchableOpacity>
    </View>
  );
}

const bannerStyles = StyleSheet.create({
  root: {
    backgroundColor: OLIVE,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingVertical: 10,
  },
  text: { color: OLIVE_LIGHT, fontSize: 14 },
  button: {
    backgroundColor: OLIVE_LIGHT,
    paddingHorizontal: 14,
    paddingVertical: 6,
    borderRadius: 8,
  },
  buttonText: { color: OLIVE, fontSize: 13, fontWeight: '600' },
});

// ---------------------------------------------------------------------------
// Soft-version banner (dismissible, once per session)
// ---------------------------------------------------------------------------

function SoftUpdateBanner({ onDismiss }: { onDismiss: () => void }) {
  const { t } = useTranslation();
  return (
    <View style={softStyles.root}>
      <Text style={softStyles.text}>{t('update.newVersionAvailable')}</Text>
      <TouchableOpacity
        onPress={onDismiss}
        style={softStyles.dismiss}
        accessibilityRole="button"
        accessibilityLabel={t('update.dismiss')}
      >
        <Text style={softStyles.dismissText}>{t('update.dismiss')}</Text>
      </TouchableOpacity>
    </View>
  );
}

const softStyles = StyleSheet.create({
  root: {
    backgroundColor: '#D4B483',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingVertical: 10,
  },
  text: { color: OLIVE, fontSize: 13, flex: 1 },
  dismiss: { paddingHorizontal: 8, paddingVertical: 4 },
  dismissText: { color: OLIVE, fontWeight: '600', fontSize: 13 },
});

// ---------------------------------------------------------------------------
// AppConfigGate — rendered inside QueryClientProvider
// Handles maintenance mode, force-update and soft-update.
// ---------------------------------------------------------------------------

function AppConfigGate({ children }: { children: React.ReactNode }) {
  const { data: configRows } = useAppConfig();
  const [softDismissed, setSoftDismissed] = useState(false);
  const { t } = useTranslation();

  const rows = configRows as MobileAppConfigDto[] | undefined;

  // Maintenance mode — parse from app_settings row
  const appSettings = React.useMemo(() => {
    if (!rows) return {};
    const row = rows.find((r) => r.configKey === 'app_settings');
    if (!row) return {};
    try {
      return JSON.parse(row.configValue) as { maintenance_mode?: boolean };
    } catch {
      return {};
    }
  }, [rows]);

  if (appSettings.maintenance_mode === true) {
    return (
      <BlockingScreen
        title={t('update.maintenance')}
        body={t('update.maintenanceMessage')}
      />
    );
  }

  // Version gate — only when flag is on
  if (FEATURES.versionGate) {
    const gate = evaluateVersionGate(rows ?? null);

    if (gate.kind === 'force') {
      return (
        <BlockingScreen
          title={t('update.updateRequired')}
          body={t('update.updateRequiredMessage')}
          storeUrl={gate.storeUrl}
        />
      );
    }

    if (gate.kind === 'soft' && !softDismissed) {
      return (
        <>
          <SoftUpdateBanner onDismiss={() => setSoftDismissed(true)} />
          {children}
        </>
      );
    }
  }

  return <>{children}</>;
}

// ---------------------------------------------------------------------------
// Root layout
// ---------------------------------------------------------------------------

function RootLayoutInner() {
  const { hydrate, isHydrated } = useAuthStore();
  const [otaRestart, setOtaRestart] = useState<(() => Promise<void>) | null>(null);
  const [i18nReady, setI18nReady] = useState(false);

  useEffect(() => {
    void hydrate();
  }, [hydrate]);

  useEffect(() => {
    void initI18n().then(() => setI18nReady(true));
  }, []);

  // OTA check — runs once after hydration, non-blocking
  useEffect(() => {
    if (!isHydrated) return;
    void (async () => {
      try {
        const restart = await checkAndFetchOtaUpdate();
        if (restart) setOtaRestart(() => restart);
      } catch {
        // Never propagate — app continues normally
      }
    })();
  }, [isHydrated]);

  if (!isHydrated || !i18nReady) {
    return <BrandSplash />;
  }

  return (
    <GestureHandlerRootView style={{ flex: 1 }}>
      <SafeAreaProvider>
        <QueryClientProvider client={queryClient}>
          <StatusBar style="dark" />
          {otaRestart ? (
            <OtaBanner
              onRestart={() => { void otaRestart(); }}
            />
          ) : null}
          <AppConfigGate>
            <ErrorBoundary>
              <Stack
                screenOptions={{
                  headerShown: false,
                  contentStyle: { backgroundColor: CREAM },
                }}
              >
                <Stack.Screen name="(auth)" />
                <Stack.Screen name="(app)" />
              </Stack>
            </ErrorBoundary>
          </AppConfigGate>
        </QueryClientProvider>
      </SafeAreaProvider>
    </GestureHandlerRootView>
  );
}

export default withSentry(RootLayoutInner);
