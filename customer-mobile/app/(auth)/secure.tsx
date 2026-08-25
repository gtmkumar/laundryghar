/**
 * "Secure your account" — offers a 4-digit unlock PIN, plus biometrics when the device
 * supports them. Shown once after sign-up and reachable later from Account settings.
 *
 * Entirely optional: Skip goes to the dashboard and the customer keeps using OTP to sign
 * in. The PIN exists so a returning user does not need a fresh OTP every time, which is
 * the slowest part of coming back to the app.
 *
 * Enter → confirm, because a mistyped PIN that only surfaces at the next launch is a
 * support ticket.
 */
import React, { useEffect, useState } from 'react';
import { Pressable, Text, View } from 'react-native';
import { useRouter } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import { StatusBar } from 'expo-status-bar';
import { Ionicons } from '@expo/vector-icons';
import { OtpInput } from '@/components/ui/OtpInput';
import { Keypad } from '@/components/ui/Keypad';
import { setPin as apiSetPin } from '@/api/auth';
import { PIN_LENGTH } from '@/constants/config';
import { useAuthStore } from '@/store/authStore';
import {
  isBiometricAvailable,
  getBiometricKind,
  biometricLabel,
  setBiometricUnlockEnabled,
  type BiometricKind,
} from '@/lib/biometrics';
import { useTranslation } from 'react-i18next';

export default function SecureAccountScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const { setHasPin } = useAuthStore();

  const [stage, setStage] = useState<'enter' | 'confirm'>('enter');
  const [first, setFirst] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [biometricKind, setBiometricKind] = useState<BiometricKind>('none');
  const [useBiometrics, setUseBiometrics] = useState(true);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      if (!(await isBiometricAvailable())) return;
      const kind = await getBiometricKind();
      if (!cancelled) setBiometricKind(kind);
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const value = stage === 'enter' ? first : confirm;

  function skip() {
    router.replace('/(app)/(tabs)/home');
  }

  function handleChange(next: string) {
    setError(null);
    if (stage === 'enter') {
      setFirst(next);
      if (next.length === PIN_LENGTH) {
        setStage('confirm');
      }
      return;
    }

    setConfirm(next);
    if (next.length === PIN_LENGTH) void save(next);
  }

  async function save(confirmed: string) {
    if (confirmed !== first) {
      // Restart from the top rather than letting the user retry the confirm step —
      // if the two differ we do not know which one they meant.
      setError(t('secure.mismatch'));
      setFirst('');
      setConfirm('');
      setStage('enter');
      return;
    }

    setSaving(true);
    try {
      await apiSetPin(confirmed);
      await setHasPin(true);
      await setBiometricUnlockEnabled(biometricKind !== 'none' && useBiometrics);
      router.replace('/(app)/(tabs)/home');
    } catch (err: unknown) {
      // The server rejects trivially guessable PINs; surface its message verbatim.
      setError(err instanceof Error ? err.message : t('auth.tryAgain'));
      setFirst('');
      setConfirm('');
      setStage('enter');
    } finally {
      setSaving(false);
    }
  }

  return (
    <SafeAreaView className="flex-1 bg-cream">
      <StatusBar style="dark" />
      <View className="flex-1 px-7 pt-4">
        <View className="flex-row items-center justify-end">
          <Pressable
            onPress={skip}
            hitSlop={10}
            accessibilityRole="button"
            accessibilityLabel={t('secure.skipA11y')}
          >
            <Text className="text-base font-bold text-ink-muted">{t('common.skip')}</Text>
          </Pressable>
        </View>

        <View className="mt-4 h-14 w-14 items-center justify-center rounded-3xl bg-olive-100">
          <Ionicons name="lock-closed-outline" size={28} color="#4A552A" />
        </View>

        <Text className="mt-6 text-3xl font-extrabold text-ink">
          {stage === 'enter' ? t('secure.title') : t('secure.confirmTitle')}
        </Text>
        <Text className="mt-2 text-base text-ink-muted">
          {stage === 'enter' ? t('secure.subtitle') : t('secure.confirmSubtitle')}
        </Text>

        <View className="mt-8">
          <OtpInput value={value} length={PIN_LENGTH} hasError={Boolean(error)} />
        </View>

        {error ? (
          <Text className="mt-3 text-sm font-semibold text-danger" accessibilityLiveRegion="polite">
            {error}
          </Text>
        ) : null}

        {biometricKind !== 'none' ? (
          <Pressable
            onPress={() => setUseBiometrics((v) => !v)}
            accessibilityRole="switch"
            accessibilityState={{ checked: useBiometrics }}
            accessibilityLabel={t('secure.biometricToggle', { method: biometricLabel(biometricKind) })}
            className="mt-6 flex-row items-center gap-3 rounded-2xl bg-white p-4"
          >
            <View
              className={`h-6 w-6 items-center justify-center rounded-lg border-2 ${
                useBiometrics ? 'border-olive-700 bg-olive-700' : 'border-cream-300 bg-white'
              }`}
            >
              {useBiometrics ? <Ionicons name="checkmark" size={14} color="#FFFFFF" /> : null}
            </View>
            <Text className="flex-1 text-sm text-ink">
              {t('secure.biometricToggle', { method: biometricLabel(biometricKind) })}
            </Text>
          </Pressable>
        ) : null}

        <View className="flex-1" />

        <View className="pb-2">
          <Keypad value={value} maxLength={PIN_LENGTH} onChange={handleChange} />
        </View>

        {saving ? (
          <Text className="pb-3 text-center text-sm text-ink-faint">{t('common.loading')}</Text>
        ) : null}
      </View>
    </SafeAreaView>
  );
}
