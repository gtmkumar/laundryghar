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

/**
 * Host port of the LOCAL AppHost gateway. Not 8080: on a developer machine 8080 is the single
 * most contested port there is — here another project's container answers it with a uvicorn 404,
 * which is what "the gateway returns 502s" (see scripts/run-device.sh) actually was. The AppHost
 * now binds the reserved 5300–5303 block; this must track the gateway endpoint in
 * backend/laundryghar/laundryghar.AppHost/AppHost.cs.
 *
 * This is the DEV default only. In production `extra.*ApiUrl` overrides win, and the gateway
 * container still listens on 8080 inside its own network (deploy/docker-compose.yml) where
 * nothing competes for it.
 */
const GATEWAY_PORT = 5300;

/** Build a default service URL on the gateway, with this service's path prefix. */
const gw = (prefix: string) => `http://${DEV_HOST}:${GATEWAY_PORT}/${prefix}`;

// All traffic via the gateway /<prefix>; extra overrides win (prod gateway URL).
// The API Gateway (YARP) strips the prefix and forwards to the right host:
//   /identity,/engagement -> core (5301);  /catalog,/orders -> operations (5302);
//   /commerce -> commerce (5303).
export const CONFIG = {
  identityApiUrl:   extra['identityApiUrl']   ?? gw('identity'),
  catalogApiUrl:    extra['catalogApiUrl']    ?? gw('catalog'),
  ordersApiUrl:     extra['ordersApiUrl']     ?? gw('orders'),
  commerceApiUrl:   extra['commerceApiUrl']   ?? gw('commerce'),
  engagementApiUrl: extra['engagementApiUrl'] ?? gw('engagement'),
  defaultBrandCode: extra['defaultBrandCode'] ?? 'LG-MAIN',
} as const;

export type ServiceName = keyof typeof CONFIG;

/**
 * Digits in a customer login OTP. Must match the backend's Otp:CustomerCodeLength
 * (4 by default) — the verify endpoint rejects a code of any other length.
 */
export const OTP_LENGTH = 4;

/** PIN length used by the set/unlock screens. The backend accepts 4–6 digits. */
export const PIN_LENGTH = 4;

/**
 * Google OAuth client IDs, one per platform, from Google Cloud Console
 * (APIs & Services → Credentials). These are public identifiers, not secrets —
 * they ship inside the app bundle by design.
 *
 * `web` doubles as the client for Expo web AND for Expo Go / the auth proxy;
 * `android` is bound to the package name + signing SHA-1; `ios` to the bundle id.
 * Google sign-in is hidden in the UI when the relevant id is missing, rather than
 * showing a button that can only fail.
 */
export const GOOGLE_AUTH = {
  webClientId:     extra['googleWebClientId'],
  androidClientId: extra['googleAndroidClientId'],
  iosClientId:     extra['googleIosClientId'],
} as const;

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
  /**
   * Google sign-in via expo-auth-session → POST /customer/auth/google.
   * The button additionally hides itself when no client ID is configured for the
   * running platform (see GOOGLE_AUTH), so enabling this without credentials is safe.
   * Apple sign-in is still presentational.
   */
  socialLogin: true,
  /**
   * PIN + biometric unlock for returning customers. When false the app always
   * falls back to the OTP flow, which remains available regardless.
   */
  pinUnlock: true,
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
