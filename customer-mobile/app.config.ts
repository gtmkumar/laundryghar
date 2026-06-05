import { ExpoConfig, ConfigContext } from 'expo/config';

export default ({ config }: ConfigContext): ExpoConfig => ({
  ...config,
  name: 'Laundry Ghar',
  slug: 'laundryghar-customer',
  version: '1.0.0',
  orientation: 'portrait',
  icon: './assets/icon.png',
  userInterfaceStyle: 'light',
  splash: {
    image: './assets/splash.png',
    resizeMode: 'contain',
    backgroundColor: '#1D4ED8',
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
      backgroundColor: '#1D4ED8',
    },
    package: 'com.laundryghar.customer',
    versionCode: 1,
    permissions: [],
  },
  extra: {
    // Service base URLs — override via EAS secrets / .env.local in dev
    identityApiUrl: process.env.IDENTITY_API_URL ?? 'http://localhost:5050',
    catalogApiUrl: process.env.CATALOG_API_URL ?? 'http://localhost:5001',
    ordersApiUrl: process.env.ORDERS_API_URL ?? 'http://localhost:5002',
    commerceApiUrl: process.env.COMMERCE_API_URL ?? 'http://localhost:5005',
    engagementApiUrl: process.env.ENGAGEMENT_API_URL ?? 'http://localhost:5007',
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
        backgroundColor: '#1D4ED8',
      },
    ],
  ],
  experiments: {
    typedRoutes: true,
  },
});
