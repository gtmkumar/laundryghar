import Constants from 'expo-constants';

// expo-constants surfaces app.config.ts `extra` at runtime
const extra = (Constants.expoConfig?.extra ?? {}) as Record<string, string>;

// Derive the dev host from however *this device* reached the Metro dev server.
// That host is correct for the API too: iOS sim → "localhost", Android emulator
// → "10.0.2.2", a physical device → the dev machine's LAN IP. No react-native
// import (importing `Platform` into this early-loaded module pulls RN core init
// forward and crashes Android's New Architecture with a "runtime not ready"
// PlatformConstants error). Falls back to localhost (and *_API_URL overrides win).
const hostUri =
  Constants.expoConfig?.hostUri ??
  Constants.expoGoConfig?.debuggerHost ??
  '';
const DEV_HOST = hostUri.split(':')[0] || 'localhost';

export const CONFIG = {
  identityApiUrl:   extra['identityApiUrl']   ?? `http://${DEV_HOST}:5050`,
  logisticsApiUrl:  extra['logisticsApiUrl']  ?? `http://${DEV_HOST}:5004`,
  engagementApiUrl: extra['engagementApiUrl'] ?? `http://${DEV_HOST}:5007`,
  defaultBrandCode: extra['defaultBrandCode'] ?? 'LG-MAIN',
} as const;

/**
 * Feature flags. Flip `riderTasksApi` to true once the backend ships the
 * rider-facing GET /api/v1/rider/tasks/today route group; until then the app
 * serves a clearly-labelled demo task set (src/data/demoTasks.ts).
 */
export const FEATURES = {
  riderTasksApi: true,
} as const;

export type ServiceName = keyof typeof CONFIG;
