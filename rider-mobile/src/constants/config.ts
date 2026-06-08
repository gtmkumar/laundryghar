import Constants from 'expo-constants';

// expo-constants surfaces app.config.ts `extra` at runtime
const extra = (Constants.expoConfig?.extra ?? {}) as Record<string, string>;

export const CONFIG = {
  identityApiUrl:   extra['identityApiUrl']   ?? 'http://localhost:5050',
  logisticsApiUrl:  extra['logisticsApiUrl']  ?? 'http://localhost:5004',
  engagementApiUrl: extra['engagementApiUrl'] ?? 'http://localhost:5007',
  defaultBrandCode: extra['defaultBrandCode'] ?? 'LG-MAIN',
} as const;

/**
 * Feature flags. Flip `riderTasksApi` to true once the backend ships the
 * rider-facing GET /api/v1/rider/tasks/today route group; until then the app
 * serves a clearly-labelled demo task set (src/data/demoTasks.ts).
 */
export const FEATURES = {
  riderTasksApi: false,
} as const;

export type ServiceName = keyof typeof CONFIG;
