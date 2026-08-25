/**
 * Platform-aware storage for the auth tokens.
 *
 * `expo-secure-store` is a native-only module — it wraps the iOS keychain and Android
 * keystore, and simply does not exist on web (calling it there throws
 * "getValueWithKeyAsync is not a function" and takes the whole app down at boot).
 * Since the customer app also ships as a web build, the store has to branch.
 *
 * Security note, stated plainly: on web there is no keychain equivalent, so tokens live
 * in localStorage and are readable by any script running on the origin. That is the same
 * exposure every SPA that holds a bearer token has, and it is why the access token is
 * short-lived (15 min) and the refresh token is rotated on every use — a stolen pair has
 * a bounded, detectable lifetime. Native builds keep full keychain/keystore protection.
 */
import { Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';

/** True when the native secure-storage module is actually available. */
const useNativeSecureStore = Platform.OS !== 'web';

function webStorage(): Storage | null {
  // Guarded: localStorage throws in some embedded/private-mode browsers rather than
  // being absent, and a storage failure must never break sign-in.
  try {
    return typeof globalThis !== 'undefined' && 'localStorage' in globalThis
      ? (globalThis as unknown as { localStorage: Storage }).localStorage
      : null;
  } catch {
    return null;
  }
}

export async function setToken(key: string, value: string): Promise<void> {
  if (useNativeSecureStore) {
    await SecureStore.setItemAsync(key, value);
    return;
  }
  webStorage()?.setItem(key, value);
}

export async function getToken(key: string): Promise<string | null> {
  if (useNativeSecureStore) {
    return SecureStore.getItemAsync(key);
  }
  return webStorage()?.getItem(key) ?? null;
}

export async function deleteToken(key: string): Promise<void> {
  if (useNativeSecureStore) {
    await SecureStore.deleteItemAsync(key);
    return;
  }
  webStorage()?.removeItem(key);
}
