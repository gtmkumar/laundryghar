/**
 * Auth store — Zustand over platform-aware token persistence (see lib/tokenStorage:
 * keychain/keystore on native, localStorage on web, because expo-secure-store is
 * native-only and throws on web).
 * Holds accessToken, refreshToken, and basic customer identity.
 * Wires itself into the axios interceptors via configureApiAuth().
 */
import { create } from 'zustand';
import { setToken, getToken, deleteToken } from '@/lib/tokenStorage';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { configureApiAuth } from '@/api/client';
import { refreshAccessToken as apiRefreshAccessToken, logout as apiLogout } from '@/api/auth';
import type { CustomerTokenResponse, CustomerMeResponse } from '@/types/api';
import { deregisterPushNotifications } from '@/lib/pushNotifications';

// ---------------------------------------------------------------------------
// Token keys — stored via tokenStorage (keychain/keystore on native, localStorage on web)
// ---------------------------------------------------------------------------
const KEY_ACCESS_TOKEN  = 'lg_access_token';
const KEY_REFRESH_TOKEN = 'lg_refresh_token';

/** AsyncStorage (not the token store) — survives logout; it is not a secret. */
const KEY_HAS_ONBOARDED = 'lg_has_onboarded';

/**
 * The phone or email the customer last signed in with, so the PIN-unlock screen can
 * pre-fill it instead of asking a returning user to type it again. Survives logout on
 * purpose — that is the whole point of "welcome back". Not a secret: it is the
 * customer's own identifier on their own device, and the PIN is what actually gates entry.
 */
const KEY_LAST_IDENTIFIER = 'lg_last_identifier';

/** Whether the last known session had an unlock PIN set. Drives which unlock UI to show. */
const KEY_HAS_PIN = 'lg_has_pin';

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
  /** Phone or email of the last signed-in account, for the PIN-unlock screen. Survives logout. */
  lastIdentifier: string | null;
  /** Whether that account has an unlock PIN. Survives logout so the unlock screen can offer it. */
  hasPin: boolean;

  // Actions
  setTokens:     (tokens: CustomerTokenResponse) => Promise<void>;
  setCustomer:   (customer: CustomerMeResponse) => void;
  setHasOnboarded: () => Promise<void>;
  /** Records who signed in, so a returning user can unlock with a PIN instead of an OTP. */
  rememberIdentifier: (identifier: string | null | undefined) => Promise<void>;
  /** Updates the cached "this account has a PIN" flag after setting or clearing one. */
  setHasPin:     (hasPin: boolean) => Promise<void>;
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
  lastIdentifier: null,
  hasPin:       false,

  setTokens: async (tokens) => {
    await setToken(KEY_ACCESS_TOKEN,  tokens.accessToken);
    await setToken(KEY_REFRESH_TOKEN, tokens.refreshToken);
    set({ accessToken: tokens.accessToken, refreshToken: tokens.refreshToken });
    // The server tells us whether this account can be unlocked with a PIN; cache it so
    // the next cold start can render the right unlock screen before any network call.
    if (typeof tokens.hasPin === 'boolean') {
      await get().setHasPin(tokens.hasPin);
    }
  },

  setCustomer: (customer) => set({ customer }),

  rememberIdentifier: async (identifier) => {
    if (!identifier) return;
    set({ lastIdentifier: identifier });
    try {
      await AsyncStorage.setItem(KEY_LAST_IDENTIFIER, identifier);
    } catch {
      // best-effort — the unlock screen just asks for the identifier instead
    }
  },

  setHasPin: async (hasPin) => {
    set({ hasPin });
    try {
      await AsyncStorage.setItem(KEY_HAS_PIN, hasPin ? 'true' : 'false');
    } catch {
      // best-effort — worst case the unlock screen offers OTP instead of PIN
    }
  },

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
    await setToken(KEY_ACCESS_TOKEN, newAccessToken);
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
    await deleteToken(KEY_ACCESS_TOKEN);
    await deleteToken(KEY_REFRESH_TOKEN);
    set({ accessToken: null, refreshToken: null, customer: null });
  },

  hydrate: async () => {
    const [access, refresh, onboarded, lastIdentifier, hasPin] = await Promise.all([
      getToken(KEY_ACCESS_TOKEN),
      getToken(KEY_REFRESH_TOKEN),
      AsyncStorage.getItem(KEY_HAS_ONBOARDED).catch(() => null),
      AsyncStorage.getItem(KEY_LAST_IDENTIFIER).catch(() => null),
      AsyncStorage.getItem(KEY_HAS_PIN).catch(() => null),
    ]);
    set({
      accessToken: access,
      refreshToken: refresh,
      hasOnboarded: onboarded === 'true',
      lastIdentifier,
      hasPin: hasPin === 'true',
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
