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

/** Build a default service URL on the gateway, with this service's path prefix. */
const gw = (prefix: string) => `http://${DEV_HOST}:8080/${prefix}`;

// All traffic via gateway :8080/<prefix>; extra overrides win (prod gateway URL).
// The API Gateway (YARP) strips the prefix and forwards to the right host:
//   /identity,/engagement -> core (5050);  /logistics -> operations (5002).
export const CONFIG = {
  identityApiUrl:   extra['identityApiUrl']   ?? gw('identity'),
  logisticsApiUrl:  extra['logisticsApiUrl']  ?? gw('logistics'),
  engagementApiUrl: extra['engagementApiUrl'] ?? gw('engagement'),
  defaultBrandCode: extra['defaultBrandCode'] ?? 'LG-MAIN',
} as const;

/**
 * Feature flags.
 */
export const FEATURES = {
  riderTasksApi: true,
  /**
   * Push notifications — Expo push token registration + foreground handler.
   * Requires a dev/production build for full iOS support (Expo Go iOS cannot
   * obtain push tokens). Set to false to skip all push initialisation.
   */
  pushNotifications: true,
  /**
   * Sentry crash reporting — gates initialiseSentry() in lib/sentry.ts.
   * Sentry is ALSO disabled when no DSN is configured or when __DEV__ === true,
   * regardless of this flag. Set to false to fully opt-out of crash reporting.
   */
  crashReporting: true,
  /**
   * OTA updates — gates the expo-updates check on boot.
   * Always a no-op in Expo Go (Updates.isEnabled === false) and in dev builds.
   * Set to false to disable the update check without removing the package.
   */
  otaUpdates: true,
  /**
   * Version gate — evaluates min/force-update versions from app_settings config.
   * When false the version-gate UI is never shown, even if the backend signals an update.
   */
  versionGate: true,
} as const;

export type ServiceName = keyof typeof CONFIG;
