import Constants from 'expo-constants';

// expo-constants surfaces app.config.ts `extra` at runtime
const extra = (Constants.expoConfig?.extra ?? {}) as Record<string, string>;

export const CONFIG = {
  identityApiUrl:   extra['identityApiUrl']   ?? 'http://localhost:5000',
  logisticsApiUrl:  extra['logisticsApiUrl']  ?? 'http://localhost:5004',
  engagementApiUrl: extra['engagementApiUrl'] ?? 'http://localhost:5007',
  defaultBrandCode: extra['defaultBrandCode'] ?? 'LG-MAIN',
} as const;

export type ServiceName = keyof typeof CONFIG;
