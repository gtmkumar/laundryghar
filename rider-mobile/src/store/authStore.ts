/**
 * Rider auth store — Zustand + expo-secure-store persistence.
 * Holds accessToken, refreshToken, and the rider's profile.
 * Wires itself into the axios interceptors via configureApiAuth().
 *
 * Mirrors customer-mobile/src/store/authStore.ts pattern exactly.
 */
import { create } from 'zustand';
import * as SecureStore from 'expo-secure-store';
import { configureApiAuth } from '@/api/client';
import { logout as apiLogout } from '@/api/auth';
import type { RiderDto, TokenResponse } from '@/types/api';
import { deregisterPushNotifications } from '@/lib/pushNotifications';

// ---------------------------------------------------------------------------
// SecureStore keys
// ---------------------------------------------------------------------------
const KEY_ACCESS_TOKEN  = 'lg_rider_access_token';
const KEY_REFRESH_TOKEN = 'lg_rider_refresh_token';

// ---------------------------------------------------------------------------
// State shape
// ---------------------------------------------------------------------------
export interface AuthState {
  accessToken:  string | null;
  refreshToken: string | null;
  rider:        RiderDto | null;
  isHydrated:   boolean;

  // Actions
  setTokens:  (tokens: TokenResponse) => Promise<void>;
  setRider:   (rider: RiderDto) => void;
  logout:     () => Promise<void>;
  hydrate:    () => Promise<void>;
}

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------
export const useAuthStore = create<AuthState>()((set, get) => ({
  accessToken:  null,
  refreshToken: null,
  rider:        null,
  isHydrated:   false,

  setTokens: async (tokens) => {
    await SecureStore.setItemAsync(KEY_ACCESS_TOKEN,  tokens.accessToken);
    await SecureStore.setItemAsync(KEY_REFRESH_TOKEN, tokens.refreshToken);
    set({ accessToken: tokens.accessToken, refreshToken: tokens.refreshToken });
  },

  setRider: (rider) => set({ rider }),

  logout: async () => {
    const { refreshToken } = get();
    // Deactivate push token BEFORE clearing auth state — the API call needs
    // the Bearer token still valid. Best-effort — never blocks logout.
    await deregisterPushNotifications();
    // Clear the local session FIRST so the (app) auth guard redirects to login
    // immediately — even if the network revoke below hangs or 401s (which is
    // exactly the case when the token has already expired/been invalidated).
    set({ accessToken: null, refreshToken: null, rider: null });
    await SecureStore.deleteItemAsync(KEY_ACCESS_TOKEN).catch(() => undefined);
    await SecureStore.deleteItemAsync(KEY_REFRESH_TOKEN).catch(() => undefined);
    // Best-effort server-side revoke (tokens already cleared locally, so a
    // failure here is harmless and can't loop back through the interceptor).
    if (refreshToken) {
      try { await apiLogout(refreshToken); } catch { /* ignore */ }
    }
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
    onAuthFailure:   () => void useAuthStore.getState().logout(),
  });
}
