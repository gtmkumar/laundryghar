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

export const CONFIG = {
  identityApiUrl:   extra['identityApiUrl']   ?? devUrl(5050),
  catalogApiUrl:    extra['catalogApiUrl']    ?? devUrl(5001),
  ordersApiUrl:     extra['ordersApiUrl']     ?? devUrl(5002),
  commerceApiUrl:   extra['commerceApiUrl']   ?? devUrl(5005),
  engagementApiUrl: extra['engagementApiUrl'] ?? devUrl(5007),
  defaultBrandCode: extra['defaultBrandCode'] ?? 'LG-MAIN',
} as const;

export type ServiceName = keyof typeof CONFIG;

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
  bookingApi: false,
  socialLogin: false, // Google / Apple sign-in buttons are presentational for now
} as const;
