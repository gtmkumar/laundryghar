import { ExpoConfig, ConfigContext } from 'expo/config';

export default ({ config }: ConfigContext): ExpoConfig => ({
  ...config,
  name: 'Laundry Ghar Rider',
  slug: 'laundryghar-rider',
  version: '1.0.0',
  orientation: 'portrait',
  icon: './assets/icon.png',
  userInterfaceStyle: 'light',
  splash: {
    image: './assets/splash.png',
    resizeMode: 'contain',
    backgroundColor: '#15803D',
  },
  scheme: 'laundryghar-rider',
  ios: {
    supportsTablet: false,
    bundleIdentifier: 'com.laundryghar.rider',
    buildNumber: '1',
    infoPlist: {
      NSLocationWhenInUseUsageDescription:
        'Laundry Ghar Rider uses your location to track pickups and deliveries.',
      NSLocationAlwaysAndWhenInUseUsageDescription:
        'Laundry Ghar Rider uses your location in the background during active shifts.',
    },
  },
  android: {
    adaptiveIcon: {
      foregroundImage: './assets/adaptive-icon.png',
      backgroundColor: '#15803D',
    },
    package: 'com.laundryghar.rider',
    versionCode: 1,
    permissions: [
      'ACCESS_FINE_LOCATION',
      'ACCESS_COARSE_LOCATION',
    ],
  },
  extra: {
    // Service base URLs — override via EAS secrets / .env.local in dev
    identityApiUrl:   process.env.IDENTITY_API_URL   ?? 'http://localhost:5050',
    logisticsApiUrl:  process.env.LOGISTICS_API_URL  ?? 'http://localhost:5004',
    engagementApiUrl: process.env.ENGAGEMENT_API_URL ?? 'http://localhost:5007',
    defaultBrandCode: process.env.DEFAULT_BRAND_CODE ?? 'LG-MAIN',
    eas: {
      projectId: 'laundryghar-rider',
    },
  },
  plugins: [
    'expo-router',
    'expo-secure-store',
    [
      'expo-location',
      {
        locationAlwaysAndWhenInUsePermission:
          'Laundry Ghar Rider uses your location during active shifts.',
      },
    ],
    [
      'expo-splash-screen',
      {
        image: './assets/splash.png',
        imageWidth: 200,
        resizeMode: 'contain',
        backgroundColor: '#15803D',
      },
    ],
  ],
  experiments: {
    typedRoutes: true,
  },
});
