import Constants from 'expo-constants';

// expo-constants surfaces app.config.ts `extra` at runtime
const extra = (Constants.expoConfig?.extra ?? {}) as Record<string, string>;

export const CONFIG = {
  identityApiUrl:   extra['identityApiUrl']   ?? 'http://localhost:5000',
  catalogApiUrl:    extra['catalogApiUrl']    ?? 'http://localhost:5001',
  ordersApiUrl:     extra['ordersApiUrl']     ?? 'http://localhost:5002',
  commerceApiUrl:   extra['commerceApiUrl']   ?? 'http://localhost:5005',
  engagementApiUrl: extra['engagementApiUrl'] ?? 'http://localhost:5007',
  defaultBrandCode: extra['defaultBrandCode'] ?? 'LG-MAIN',
} as const;

export type ServiceName = keyof typeof CONFIG;
