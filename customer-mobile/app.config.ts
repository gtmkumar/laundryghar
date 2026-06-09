import { ExpoConfig, ConfigContext } from 'expo/config';

const OLIVE_700 = '#4A552A';

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
    eas: {
      projectId: 'laundryghar-customer',
    },
  },
  plugins: [
    'expo-router',
    'expo-secure-store',
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
