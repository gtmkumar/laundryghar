/**
 * OTA update check — customer-mobile.
 *
 * Checks for an available expo-updates bundle on boot.
 *
 * Behaviour matrix:
 *   Expo Go            → Updates.isEnabled === false → silent no-op
 *   Dev build (__DEV__)→ silent no-op (checkForUpdateAsync throws in dev)
 *   Preview / prod     → checks, fetches, signals banner via callback
 *
 * NEVER blocks startup. All errors are swallowed.
 * Guarded by FEATURES.otaUpdates.
 *
 * Usage:
 *   const restart = await checkAndFetchOtaUpdate();
 *   // restart is a callable if an update was downloaded, otherwise null
 */
import * as Updates from 'expo-updates';
import { FEATURES } from '@/constants/config';

/**
 * Check for an OTA update and fetch it if available.
 *
 * @returns A `restart` function if a new bundle was downloaded and is ready
 *          to apply, or `null` if no update is available / applicable.
 *          The caller shows a non-blocking banner and calls restart() on user tap.
 */
export async function checkAndFetchOtaUpdate(): Promise<(() => Promise<void>) | null> {
  if (!FEATURES.otaUpdates) return null;

  // Updates.isEnabled is false in Expo Go and in bare dev builds
  if (!Updates.isEnabled) return null;

  // Extra guard: in dev mode the Updates API throws — skip silently
  if (__DEV__) return null;

  try {
    const check = await Updates.checkForUpdateAsync();
    if (!check.isAvailable) return null;

    await Updates.fetchUpdateAsync();

    return async () => {
      try {
        await Updates.reloadAsync();
      } catch {
        // If reload fails for some reason, swallow — app is still usable
      }
    };
  } catch {
    // Network error, server down, invalid manifest, etc. — never crash the app
    return null;
  }
}
