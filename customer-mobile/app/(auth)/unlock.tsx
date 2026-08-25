/**
 * "Welcome back" unlock for a returning customer whose account already exists on this
 * device — the sign-IN path, as opposed to sign-up.
 *
 * Two ways in, both avoiding another OTP round-trip:
 *   - Biometrics: authenticate locally, then resume the refresh token this device already
 *     holds. Attempted automatically on mount when enabled, since that is the fastest path.
 *   - PIN: POST /customer/auth/pin/verify, which works even when the stored refresh token
 *     has expired — this is why the PIN is server-side and the biometric is not.
 *
 * "Use a different method" always leads back to OTP, so a forgotten PIN or a failing
 * sensor can never lock someone out of their own account.
 */
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Pressable, Text, View } from 'react-native';
import { useRouter } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import { StatusBar } from 'expo-status-bar';
import { Ionicons, MaterialCommunityIcons } from '@expo/vector-icons';
import { OtpInput } from '@/components/ui/OtpInput';
import { Keypad } from '@/components/ui/Keypad';
import { verifyPin } from '@/api/auth';
import { PIN_LENGTH } from '@/constants/config';
import { useAuthStore } from '@/store/authStore';
import {
  authenticateWithBiometrics,
  biometricLabel,
  getBiometricKind,
  isBiometricAvailable,
  isBiometricUnlockEnabled,
  type BiometricKind,
} from '@/lib/biometrics';
import { maskPhone } from '@/lib/format';
import { useTranslation } from 'react-i18next';

export default function UnlockScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const { lastIdentifier, refreshToken, setTokens, logout } = useAuthStore();

  const [pin, setPin] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [verifying, setVerifying] = useState(false);
  const [biometricKind, setBiometricKind] = useState<BiometricKind>('none');
  const [biometricEnabled, setBiometricEnabled] = useState(false);

  // Guards the auto-prompt so a re-render cannot fire a second OS dialog on top of the first.
  const promptedRef = useRef(false);

  const goHome = useCallback(() => router.replace('/(app)/(tabs)/home'), [router]);

  const tryBiometric = useCallback(async () => {
    const ok = await authenticateWithBiometrics(t('unlock.biometricPrompt'));
    if (!ok) return;

    // The device proved the user locally; the session it unlocks is the refresh token
    // this device already holds (see lib/tokenStorage). Without one there is nothing to
    // resume, so fall through to the PIN, which can mint a fresh session.
    if (refreshToken) {
      goHome();
    } else {
      setError(t('unlock.sessionExpired'));
    }
  }, [goHome, refreshToken, t]);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      if (!(await isBiometricAvailable())) return;
      const [kind, enabled] = await Promise.all([
        getBiometricKind(),
        isBiometricUnlockEnabled(),
      ]);
      if (cancelled) return;

      setBiometricKind(kind);
      setBiometricEnabled(enabled);

      if (enabled && !promptedRef.current) {
        promptedRef.current = true;
        void tryBiometric();
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [tryBiometric]);

  async function submit(full: string) {
    if (!lastIdentifier) {
      // Nothing to verify against — send them through the normal sign-in.
      router.replace('/(auth)/phone');
      return;
    }

    setVerifying(true);
    setError(null);
    try {
      const tokens = await verifyPin(lastIdentifier, full);
      await setTokens(tokens);
      goHome();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('auth.tryAgain'));
      setPin('');
    } finally {
      setVerifying(false);
    }
  }

  /** Abandon the cached session and start a clean OTP sign-in. */
  async function switchToOtpSignIn() {
    await logout();
    router.replace('/(auth)/phone');
  }

  const displayIdentifier = lastIdentifier?.startsWith('+')
    ? maskPhone(lastIdentifier.replace('+91', ''))
    : (lastIdentifier ?? '');

  return (
    <SafeAreaView className="flex-1 bg-cream">
      <StatusBar style="dark" />
      <View className="flex-1 px-7 pt-8">
        <View className="h-14 w-14 items-center justify-center rounded-3xl bg-olive-100">
          <MaterialCommunityIcons name="hanger" size={28} color="#4A552A" />
        </View>

        <Text className="mt-6 text-3xl font-extrabold text-ink">{t('unlock.title')}</Text>
        {displayIdentifier ? (
          <Text className="mt-2 text-base text-ink-muted">
            {t('unlock.subtitle')}{' '}
            <Text className="font-bold text-ink-soft">{displayIdentifier}</Text>
          </Text>
        ) : null}

        <View className="mt-8">
          <OtpInput value={pin} length={PIN_LENGTH} hasError={Boolean(error)} />
        </View>

        {error ? (
          <Text className="mt-3 text-sm font-semibold text-danger" accessibilityLiveRegion="polite">
            {error}
          </Text>
        ) : null}

        {verifying ? (
          <Text className="mt-3 text-sm text-ink-faint">{t('auth.verifying')}</Text>
        ) : null}

        {biometricKind !== 'none' && biometricEnabled ? (
          <Pressable
            onPress={() => void tryBiometric()}
            className="mt-6 flex-row items-center gap-2"
            hitSlop={8}
            accessibilityRole="button"
            accessibilityLabel={t('unlock.useBiometric', { method: biometricLabel(biometricKind) })}
          >
            <Ionicons
              name={biometricKind === 'face' ? 'scan-outline' : 'finger-print-outline'}
              size={20}
              color="#4A552A"
            />
            <Text className="text-sm font-bold text-olive-700">
              {t('unlock.useBiometric', { method: biometricLabel(biometricKind) })}
            </Text>
          </Pressable>
        ) : null}

        <View className="flex-1" />

        <Pressable
          onPress={() => void switchToOtpSignIn()}
          className="items-center py-4"
          hitSlop={8}
          accessibilityRole="button"
          accessibilityLabel={t('unlock.useAnotherMethod')}
        >
          <Text className="text-sm font-bold text-olive-700">{t('unlock.useAnotherMethod')}</Text>
        </Pressable>

        <View className="pb-2">
          <Keypad
            value={pin}
            maxLength={PIN_LENGTH}
            onChange={(next) => {
              setError(null);
              setPin(next);
              if (next.length === PIN_LENGTH) void submit(next);
            }}
          />
        </View>
      </View>
    </SafeAreaView>
  );
}
