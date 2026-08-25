/**
 * Biometric unlock (Face ID / Touch ID / Android fingerprint).
 *
 * Deliberately has no server component. The biometric never leaves the device and
 * proves nothing to the backend, so treating it as a network credential would be
 * security theatre. What it actually does is gate access to the refresh token this
 * device already holds in SecureStore: pass the local check, and the app resumes the
 * existing session; fail it, and the user falls back to a PIN or an OTP.
 *
 * This is why enabling biometrics is stored as a plain preference — the secret it
 * guards is the refresh token, which SecureStore is already protecting at rest.
 */
import * as LocalAuthentication from 'expo-local-authentication';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { Platform } from 'react-native';

/** Not a secret — it only records the user's preference. */
const KEY_BIOMETRIC_ENABLED = 'lg_biometric_enabled';

export type BiometricKind = 'face' | 'fingerprint' | 'iris' | 'none';

/**
 * Whether this device has biometric hardware AND the user has enrolled at least one
 * biometric. Both are required: enrolled-but-no-hardware and hardware-but-not-enrolled
 * both produce an unlock prompt that can never succeed.
 *
 * Always false on web, where expo-local-authentication has no implementation.
 */
export async function isBiometricAvailable(): Promise<boolean> {
  if (Platform.OS === 'web') return false;
  try {
    const [hasHardware, isEnrolled] = await Promise.all([
      LocalAuthentication.hasHardwareAsync(),
      LocalAuthentication.isEnrolledAsync(),
    ]);
    return hasHardware && isEnrolled;
  } catch {
    return false;
  }
}

/** The strongest enrolled biometric, so the UI can name it ("Use Face ID") accurately. */
export async function getBiometricKind(): Promise<BiometricKind> {
  if (Platform.OS === 'web') return 'none';
  try {
    const types = await LocalAuthentication.supportedAuthenticationTypesAsync();
    if (types.includes(LocalAuthentication.AuthenticationType.FACIAL_RECOGNITION)) return 'face';
    if (types.includes(LocalAuthentication.AuthenticationType.FINGERPRINT)) return 'fingerprint';
    if (types.includes(LocalAuthentication.AuthenticationType.IRIS)) return 'iris';
    return 'none';
  } catch {
    return 'none';
  }
}

/** Human label for the enrolled biometric, for button text and prompts. */
export function biometricLabel(kind: BiometricKind): string {
  switch (kind) {
    case 'face':
      return Platform.OS === 'ios' ? 'Face ID' : 'face unlock';
    case 'fingerprint':
      return Platform.OS === 'ios' ? 'Touch ID' : 'fingerprint';
    case 'iris':
      return 'iris unlock';
    default:
      return 'biometrics';
  }
}

/**
 * Runs the OS biometric prompt. Returns true only on a successful match.
 *
 * disableDeviceFallback is false so the OS can offer the device passcode when the
 * biometric fails repeatedly — locking a user out of their own laundry orders because
 * a fingerprint sensor is wet helps nobody, and the device passcode is a stronger
 * factor than the app PIN anyway.
 */
export async function authenticateWithBiometrics(promptMessage: string): Promise<boolean> {
  if (!(await isBiometricAvailable())) return false;
  try {
    const result = await LocalAuthentication.authenticateAsync({
      promptMessage,
      cancelLabel: 'Use PIN instead',
      disableDeviceFallback: false,
    });
    return result.success;
  } catch {
    return false;
  }
}

export async function isBiometricUnlockEnabled(): Promise<boolean> {
  try {
    return (await AsyncStorage.getItem(KEY_BIOMETRIC_ENABLED)) === 'true';
  } catch {
    return false;
  }
}

export async function setBiometricUnlockEnabled(enabled: boolean): Promise<void> {
  try {
    await AsyncStorage.setItem(KEY_BIOMETRIC_ENABLED, enabled ? 'true' : 'false');
  } catch {
    // Best-effort: worst case the user re-enables it in Account settings.
  }
}
