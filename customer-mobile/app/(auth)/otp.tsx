/**
 * OTP verification — "Enter the code".
 * Display-only OtpInput cells driven by a custom numeric Keypad (no system
 * keyboard). Auto-submits on the last digit.
 *
 * NOTE: the mockup shows 4 cells, but the Identity service issues 6-digit
 * codes for the customer login flow, so we render 6 — correctness over pixels.
 */
import React, { useEffect, useState } from 'react';
import { Alert, Pressable, Text, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import { StatusBar } from 'expo-status-bar';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { OtpInput } from '@/components/ui/OtpInput';
import { Keypad } from '@/components/ui/Keypad';
import { verifyOtp, sendOtp } from '@/api/auth';
import { useAuthStore } from '@/store/authStore';
import { useTranslation } from 'react-i18next';

const CODE_LENGTH = 6;
const RESEND_SECONDS = 30;

export default function OtpScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const { phone, raw } = useLocalSearchParams<{ phone: string; raw: string }>();
  const { setTokens } = useAuthStore();

  const [code, setCode] = useState('');
  const [verifying, setVerifying] = useState(false);
  const [error, setError] = useState(false);
  const [seconds, setSeconds] = useState(RESEND_SECONDS);

  useEffect(() => {
    if (seconds <= 0) return;
    const t = setTimeout(() => setSeconds((s) => s - 1), 1000);
    return () => clearTimeout(t);
  }, [seconds]);

  const masked = raw
    ? `+91 98 ●●●● ${raw.slice(-4)}`
    : (phone ?? '');

  async function verify(full: string) {
    if (!phone) return;
    setVerifying(true);
    setError(false);
    try {
      const tokens = await verifyOtp(phone, full);
      await setTokens(tokens);
      router.replace('/(app)/(tabs)/home');
    } catch (err: unknown) {
      setError(true);
      setCode('');
      Alert.alert(
        t('auth.verificationFailed'),
        err instanceof Error ? err.message : t('auth.tryAgain'),
      );
    } finally {
      setVerifying(false);
    }
  }

  async function resend() {
    if (seconds > 0 || !phone) return;
    try {
      await sendOtp(phone);
      setSeconds(RESEND_SECONDS);
      setCode('');
      setError(false);
    } catch (err: unknown) {
      Alert.alert(t('auth.couldNotResend'), err instanceof Error ? err.message : t('auth.tryAgain'));
    }
  }

  return (
    <SafeAreaView className="flex-1 bg-cream">
      <StatusBar style="dark" />
      <View className="flex-1 px-6 pt-4">
        {/* Back */}
        <Pressable
          onPress={() => router.back()}
          accessibilityRole="button"
          accessibilityLabel={t('common.back')}
          hitSlop={8}
          className="h-10 w-10 items-center justify-center rounded-full bg-white"
        >
          <MaterialCommunityIcons name="chevron-left" size={26} color="#3C3F35" />
        </Pressable>

        <Text className="mt-6 text-4xl font-extrabold text-ink">{t('auth.enterCode')}</Text>
        <Text className="mt-2 text-base text-ink-muted">
          {t('auth.codeSentTo')} <Text className="font-bold text-ink-soft">{masked}</Text>
        </Text>

        {/* Code cells */}
        <View className="mt-8">
          <OtpInput value={code} length={CODE_LENGTH} hasError={error} />
        </View>

        {/* Resend */}
        <View className="mt-6 flex-row items-center">
          {seconds > 0 ? (
            <Text className="text-sm text-ink-faint">
              {t('auth.didntGetIt')} <Text className="font-bold text-ink-soft">0:{String(seconds).padStart(2, '0')}</Text>
            </Text>
          ) : (
            <Pressable
              onPress={() => void resend()}
              hitSlop={8}
              accessibilityRole="button"
              accessibilityLabel={t('a11y.resendCode')}
            >
              <Text className="text-sm font-bold text-olive-700">{t('auth.resendCode')}</Text>
            </Pressable>
          )}
          {verifying ? <Text className="ml-auto text-sm text-ink-faint">{t('auth.verifying')}</Text> : null}
        </View>

        <View className="flex-1" />

        {/* Keypad */}
        <View className="pb-2">
          <Keypad
            value={code}
            maxLength={CODE_LENGTH}
            onChange={(next) => {
              setError(false);
              setCode(next);
              if (next.length === CODE_LENGTH) void verify(next);
            }}
          />
        </View>
      </View>
    </SafeAreaView>
  );
}
