/**
 * Auth store — Zustand + expo-secure-store persistence.
 * Holds accessToken, refreshToken, and basic customer identity.
 * Wires itself into the axios interceptors via configureApiAuth().
 */
import { create } from 'zustand';
import * as SecureStore from 'expo-secure-store';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { configureApiAuth } from '@/api/client';
import { refreshAccessToken as apiRefreshAccessToken, logout as apiLogout } from '@/api/auth';
import type { CustomerTokenResponse, CustomerMeResponse } from '@/types/api';
import { deregisterPushNotifications } from '@/lib/pushNotifications';

// ---------------------------------------------------------------------------
// SecureStore keys
// ---------------------------------------------------------------------------
const KEY_ACCESS_TOKEN  = 'lg_access_token';
const KEY_REFRESH_TOKEN = 'lg_refresh_token';

/** AsyncStorage (not SecureStore) — survives logout; it is not a secret. */
const KEY_HAS_ONBOARDED = 'lg_has_onboarded';

// ---------------------------------------------------------------------------
// State shape
// ---------------------------------------------------------------------------
export interface AuthState {
  accessToken:  string | null;
  refreshToken: string | null;
  customer:     CustomerMeResponse | null;
  isHydrated:   boolean;
  /** True once the user has completed/skipped the onboarding carousel. Survives logout. */
  hasOnboarded: boolean;

  // Actions
  setTokens:     (tokens: CustomerTokenResponse) => Promise<void>;
  setCustomer:   (customer: CustomerMeResponse) => void;
  setHasOnboarded: () => Promise<void>;
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
  hasOnboarded: false,

  setTokens: async (tokens) => {
    await SecureStore.setItemAsync(KEY_ACCESS_TOKEN,  tokens.accessToken);
    await SecureStore.setItemAsync(KEY_REFRESH_TOKEN, tokens.refreshToken);
    set({ accessToken: tokens.accessToken, refreshToken: tokens.refreshToken });
  },

  setCustomer: (customer) => set({ customer }),

  setHasOnboarded: async () => {
    set({ hasOnboarded: true });
    try {
      await AsyncStorage.setItem(KEY_HAS_ONBOARDED, 'true');
    } catch {
      // best-effort — worst case the carousel shows once more
    }
  },

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
    const [access, refresh, onboarded] = await Promise.all([
      SecureStore.getItemAsync(KEY_ACCESS_TOKEN),
      SecureStore.getItemAsync(KEY_REFRESH_TOKEN),
      AsyncStorage.getItem(KEY_HAS_ONBOARDED).catch(() => null),
    ]);
    set({
      accessToken: access,
      refreshToken: refresh,
      hasOnboarded: onboarded === 'true',
      isHydrated: true,
    });
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
