import { ExpoConfig, ConfigContext } from 'expo/config';

const OLIVE_700 = '#4A552A';
const EAS_PROJECT_ID = 'laundryghar-customer';

export default ({ config }: ConfigContext): ExpoConfig => ({
  ...config,
  name: 'Laundry Ghar',
  slug: 'laundryghar-customer',
  version: '2.0.0',
  orientation: 'portrait',
  icon: './assets/icon.png',
  userInterfaceStyle: 'light',
  splash: {
    image: './assets/splash.png',
    resizeMode: 'contain',
    backgroundColor: OLIVE_700,
  },
  scheme: 'laundryghar',
  ios: {
    supportsTablet: false,
    bundleIdentifier: 'com.laundryghar.customer',
    buildNumber: '1',
  },
  android: {
    adaptiveIcon: {
      foregroundImage: './assets/adaptive-icon.png',
      backgroundColor: OLIVE_700,
    },
    package: 'com.laundryghar.customer',
    versionCode: 1,
    permissions: [],
  },
  // ---------------------------------------------------------------------------
  // OTA updates — expo-updates
  // checkAutomatically: 'ON_LOAD' means the runtime checks on every cold start.
  // fallbackToCacheTimeout: 0 → never block startup waiting for a manifest.
  // runtimeVersion policy 'appVersion' ties OTA compatibility to the app version
  // declared above — a new native binary gets a new channel automatically.
  // ---------------------------------------------------------------------------
  updates: {
    url: `https://u.expo.dev/${EAS_PROJECT_ID}`,
    checkAutomatically: 'ON_LOAD',
    fallbackToCacheTimeout: 0,
  },
  runtimeVersion: {
    policy: 'appVersion',
  },
  extra: {
    // Service base URLs — set ONLY from env (undefined in plain dev so the app
    // falls back to constants/config.ts's DEV_HOST resolution, which is correct
    // on iOS sim / Android emulator / physical device). Do NOT hardcode
    // localhost here: an `extra` value always wins over the config.ts fallback,
    // which breaks the Android emulator (10.0.2.2). Lesson from rider-mobile.
    identityApiUrl: process.env.IDENTITY_API_URL,
    catalogApiUrl: process.env.CATALOG_API_URL,
    ordersApiUrl: process.env.ORDERS_API_URL,
    commerceApiUrl: process.env.COMMERCE_API_URL,
    engagementApiUrl: process.env.ENGAGEMENT_API_URL,
    defaultBrandCode: process.env.DEFAULT_BRAND_CODE ?? 'LG-MAIN',
    // Sentry DSN — set via EAS secret (SENTRY_DSN) or .env.local
    // (EXPO_PUBLIC_SENTRY_DSN). When absent Sentry is fully disabled.
    sentryDsn: process.env.SENTRY_DSN,
    eas: {
      projectId: EAS_PROJECT_ID,
    },
  },
  plugins: [
    'expo-router',
    'expo-secure-store',
    'expo-localization',
    // Sentry config plugin — injects native crash-reporter init.
    // organization/project enable build-time source-map upload (also needs
    // SENTRY_AUTH_TOKEN — EAS secret only); when unset the upload is skipped
    // with a warning and runtime crash reporting is unaffected.
    [
      '@sentry/react-native',
      {
        organization: process.env.SENTRY_ORG,
        project: process.env.SENTRY_PROJECT,
      },
    ],
    [
      // expo-notifications: configures native notification permissions and
      // Android notification channels. Required for production/dev builds.
      // In Expo Go only Android can obtain push tokens; iOS requires a native build.
      'expo-notifications',
      {
        icon: './assets/icon.png',
        color: '#4A552A',
        androidMode: 'default',
        // Android 13+ explicit notification permission is requested at runtime
        // via requestPermissionsAsync() — no manifest entry needed here.
      },
    ],
    [
      'expo-splash-screen',
      {
        image: './assets/splash.png',
        imageWidth: 200,
        resizeMode: 'contain',
        backgroundColor: OLIVE_700,
      },
    ],
  ],
  experiments: {
    typedRoutes: true,
  },
});
