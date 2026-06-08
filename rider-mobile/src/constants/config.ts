import { Platform } from 'react-native';
import Constants from 'expo-constants';

// expo-constants surfaces app.config.ts `extra` at runtime
const extra = (Constants.expoConfig?.extra ?? {}) as Record<string, string>;

// Dev host differs per platform: the Android emulator can't see the host's
// `localhost` — it reaches the host machine via the 10.0.2.2 alias. iOS sim
// shares the host loopback, so `localhost` works there. (A physical device
// would need the LAN IP via the *_API_URL env overrides.)
const DEV_HOST = Platform.OS === 'android' ? '10.0.2.2' : 'localhost';

export const CONFIG = {
  identityApiUrl:   extra['identityApiUrl']   ?? `http://${DEV_HOST}:5050`,
  logisticsApiUrl:  extra['logisticsApiUrl']  ?? `http://${DEV_HOST}:5004`,
  engagementApiUrl: extra['engagementApiUrl'] ?? `http://${DEV_HOST}:5007`,
  defaultBrandCode: extra['defaultBrandCode'] ?? 'LG-MAIN',
} as const;

/**
 * Feature flags. Flip `riderTasksApi` to true once the backend ships the
 * rider-facing GET /api/v1/rider/tasks/today route group; until then the app
 * serves a clearly-labelled demo task set (src/data/demoTasks.ts).
 */
export const FEATURES = {
  riderTasksApi: true,
} as const;

export type ServiceName = keyof typeof CONFIG;
