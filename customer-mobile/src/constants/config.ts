import Constants from 'expo-constants';

// ---------------------------------------------------------------------------
// Dev host resolution
//
// Derive the host from HOW the device reached Metro. This is correct on every
// target: iOS sim -> `localhost`, Android emulator -> `10.0.2.2`, physical
// device -> the LAN IP. Do NOT `import { Platform } from 'react-native'` here:
// this module loads very early and pulling RN core forward crashes Android
// New-Arch with a "[runtime not ready] PlatformConstants" redbox.
// (Same lesson learned in rider-mobile.)
// ---------------------------------------------------------------------------
const DEV_HOST =
  (
    Constants.expoConfig?.hostUri ??
    (Constants as { expoGoConfig?: { debuggerHost?: string } }).expoGoConfig?.debuggerHost ??
    ''
  ).split(':')[0] || 'localhost';

// expo-constants surfaces app.config.ts `extra` at runtime
const extra = (Constants.expoConfig?.extra ?? {}) as Record<string, string | undefined>;

/** Build a default service URL on the resolved dev host. */
const devUrl = (port: number) => `http://${DEV_HOST}:${port}`;

// Post-consolidation the 11 services collapsed to 3 hosts:
//   core (5050) = identity + engagement;  operations (5002) = catalog + orders;
//   commerce (5005) = commerce.
export const CONFIG = {
  identityApiUrl:   extra['identityApiUrl']   ?? devUrl(5050),
  catalogApiUrl:    extra['catalogApiUrl']    ?? devUrl(5002),
  ordersApiUrl:     extra['ordersApiUrl']     ?? devUrl(5002),
  commerceApiUrl:   extra['commerceApiUrl']   ?? devUrl(5005),
  engagementApiUrl: extra['engagementApiUrl'] ?? devUrl(5050),
  defaultBrandCode: extra['defaultBrandCode'] ?? 'LG-MAIN',
} as const;

export type ServiceName = keyof typeof CONFIG;

/** Flat express surcharge (₹) applied at payment; mirrored on the tracking summary. */
export const EXPRESS_SURCHARGE = 50;

// ---------------------------------------------------------------------------
// Feature flags
//
// `bookingApi` gates whether the place-an-order flow talks to a live backend
// order-creation endpoint. There is currently no single "create order with
// items" customer endpoint (orders are created server-side after pickup +
// weighing), so the booking flow runs on local cart state and finalises by
// scheduling a real pickup request. Flip this on once the endpoint ships.
// ---------------------------------------------------------------------------
export const FEATURES = {
  bookingApi: true,   // POST /api/v1/customer/pickup-requests is live with cart items
  socialLogin: false, // Google / Apple sign-in buttons are presentational for now
  /**
   * Push notifications — Expo push token registration + foreground handler.
   * Requires a dev/production build for full iOS support (Expo Go iOS cannot
   * obtain push tokens). Set to false to skip all push initialisation.
   */
  pushNotifications: true,
  /**
   * Wallet top-up — gates the Razorpay payment sheet.
   * Set to false until the native Razorpay SDK is integrated (requires custom dev build).
   * When false, 'Add money' shows a 'coming soon' bottom sheet explaining amounts.
   */
  walletTopUp: false,
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
