import { ExpoConfig, ConfigContext } from 'expo/config';

// TODO: replace with the real EAS project UUID from `eas project:init`
// (interactive — requires your Expo account). The current value is the slug,
// which 404s the OTA update manifest until a real UUID is wired in.
const EAS_PROJECT_ID = 'laundryghar-rider';

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
      NSCameraUsageDescription:
        'Laundry Ghar Rider uses your camera to capture proof-of-delivery and garment-inspection photos.',
      NSPhotoLibraryUsageDescription:
        'Laundry Ghar Rider accesses your photo library to attach proof-of-delivery images.',
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
      // Push notifications — runtime permission requested by expo-notifications
      // on Android 13+ (API 33). The plugin adds POST_NOTIFICATIONS automatically
      // via its config plugin; listing it here is belt-and-suspenders documentation.
      'POST_NOTIFICATIONS',
      // Camera + media for proof-of-delivery and garment-inspection photos.
      'CAMERA',
      'READ_MEDIA_IMAGES',
    ],
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
    // Service base URLs — set ONLY from explicit env overrides (EAS secrets /
    // .env.local / prod). Leave undefined in plain dev so src/constants/config.ts
    // derives the host from how the device reached Metro (localhost on iOS sim,
    // 10.0.2.2 on the Android emulator, LAN IP on a physical device). Hardcoding
    // localhost here would always win over that and break the Android emulator.
    identityApiUrl:   process.env.IDENTITY_API_URL,
    logisticsApiUrl:  process.env.LOGISTICS_API_URL,
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
      },
    ],
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
    // expo-image-picker config plugin — declares camera + photo-library entitlements
    // so the native build passes App Store and Google Play policy requirements.
    // The permission strings mirror what is already declared in ios.infoPlist above;
    // the plugin wires the runtime permission request on Android and the Info.plist
    // entries on iOS automatically when run through `eas build`.
    [
      'expo-image-picker',
      {
        photosPermission:
          'Laundry Ghar Rider accesses your photo library to attach proof-of-delivery images.',
        cameraPermission:
          'Laundry Ghar Rider uses your camera to capture proof-of-delivery and garment-inspection photos.',
        microphonePermission: false,
      },
    ],
  ],
  experiments: {
    typedRoutes: true,
  },
});
