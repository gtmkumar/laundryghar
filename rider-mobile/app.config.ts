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
    backgroundColor: '#4A552A',
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
      // Allow continued GPS while the app is backgrounded during a shift.
      UIBackgroundModes: ['location'],
    },
  },
  android: {
    adaptiveIcon: {
      foregroundImage: './assets/adaptive-icon.png',
      backgroundColor: '#4A552A',
    },
    package: 'com.laundryghar.rider',
    versionCode: 1,
    permissions: [
      'ACCESS_FINE_LOCATION',
      'ACCESS_COARSE_LOCATION',
      // Background GPS + the mandatory foreground service that carries it.
      'ACCESS_BACKGROUND_LOCATION',
      'FOREGROUND_SERVICE',
      'FOREGROUND_SERVICE_LOCATION',
    ],
  },
  extra: {
    // Service base URLs — set ONLY from explicit env overrides (EAS secrets /
    // .env.local / prod). Leave undefined in plain dev so src/constants/config.ts
    // derives the host from how the device reached Metro (localhost on iOS sim,
    // 10.0.2.2 on the Android emulator, LAN IP on a physical device). Hardcoding
    // localhost here would always win over that and break the Android emulator.
    identityApiUrl:   process.env.IDENTITY_API_URL,
    logisticsApiUrl:  process.env.LOGISTICS_API_URL,
    engagementApiUrl: process.env.ENGAGEMENT_API_URL,
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
        // Wire the native background-location entitlements/services.
        isAndroidBackgroundLocationEnabled: true,
        isIosBackgroundLocationEnabled: true,
      },
    ],
    [
      'expo-splash-screen',
      {
        image: './assets/splash.png',
        imageWidth: 200,
        resizeMode: 'contain',
        backgroundColor: '#4A552A',
      },
    ],
  ],
  experiments: {
    typedRoutes: true,
  },
});
