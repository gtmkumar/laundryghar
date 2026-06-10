/**
 * Auth store — Zustand + expo-secure-store persistence.
 * Holds accessToken, refreshToken, and basic customer identity.
 * Wires itself into the axios interceptors via configureApiAuth().
 */
import { create } from 'zustand';
import * as SecureStore from 'expo-secure-store';
import { configureApiAuth } from '@/api/client';
import { refreshAccessToken as apiRefreshAccessToken, logout as apiLogout } from '@/api/auth';
import type { CustomerTokenResponse, CustomerMeResponse } from '@/types/api';
import { deregisterPushNotifications } from '@/lib/pushNotifications';

// ---------------------------------------------------------------------------
// SecureStore keys
// ---------------------------------------------------------------------------
const KEY_ACCESS_TOKEN  = 'lg_access_token';
const KEY_REFRESH_TOKEN = 'lg_refresh_token';

// ---------------------------------------------------------------------------
// State shape
// ---------------------------------------------------------------------------
export interface AuthState {
  accessToken:  string | null;
  refreshToken: string | null;
  customer:     CustomerMeResponse | null;
  isHydrated:   boolean;

  // Actions
  setTokens:     (tokens: CustomerTokenResponse) => Promise<void>;
  setCustomer:   (customer: CustomerMeResponse) => void;
  refreshTokens: () => Promise<void>;
  logout:        () => Promise<void>;
  hydrate:       () => Promise<void>;
}

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------
export const useAuthStore = create<AuthState>()((set, get) => ({
  accessToken:  null,
  refreshToken: null,
  customer:     null,
  isHydrated:   false,

  setTokens: async (tokens) => {
    await SecureStore.setItemAsync(KEY_ACCESS_TOKEN,  tokens.accessToken);
    await SecureStore.setItemAsync(KEY_REFRESH_TOKEN, tokens.refreshToken);
    set({ accessToken: tokens.accessToken, refreshToken: tokens.refreshToken });
  },

  setCustomer: (customer) => set({ customer }),

  refreshTokens: async () => {
    const { refreshToken } = get();
    if (!refreshToken) throw new Error('No refresh token stored');
    const newAccessToken = await apiRefreshAccessToken(refreshToken);
    await SecureStore.setItemAsync(KEY_ACCESS_TOKEN, newAccessToken);
    set({ accessToken: newAccessToken });
  },

  logout: async () => {
    const { refreshToken } = get();
    // Deactivate push token before clearing auth state so the API call still
    // has a valid Bearer token. Best-effort — never blocks logout.
    await deregisterPushNotifications();
    try {
      if (refreshToken) await apiLogout(refreshToken);
    } catch {
      // best-effort — proceed regardless
    }
    await SecureStore.deleteItemAsync(KEY_ACCESS_TOKEN);
    await SecureStore.deleteItemAsync(KEY_REFRESH_TOKEN);
    set({ accessToken: null, refreshToken: null, customer: null });
  },

  hydrate: async () => {
    const [access, refresh] = await Promise.all([
      SecureStore.getItemAsync(KEY_ACCESS_TOKEN),
      SecureStore.getItemAsync(KEY_REFRESH_TOKEN),
    ]);
    set({ accessToken: access, refreshToken: refresh, isHydrated: true });
  },
}));

// ---------------------------------------------------------------------------
// Wire auth store into the axios interceptors — call once at app root
// ---------------------------------------------------------------------------
export function bootstrapApiAuth(): void {
  configureApiAuth({
    getAccessToken:  () => useAuthStore.getState().accessToken,
    getRefreshToken: () => useAuthStore.getState().refreshToken,
    onAuthFailure:   () => useAuthStore.getState().logout(),
  });
}
