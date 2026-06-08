/**
 * Enter the code — 6-digit OTP verification (mockup OTP screen).
 *
 * NOTE: the backend issues **6-digit** codes (OtpSendHandler.CodeLength = 6),
 * so we render 6 cells even though the static mockup shows 4.
 *
 *   POST /api/v1/auth/otp/verify → { accessToken, refreshToken }
 * On success we persist tokens and land on the duty home screen.
 */
import React, { useEffect, useState } from 'react';
import { Alert, Pressable, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import { sendLoginOtp, verifyLoginOtp } from '@/api/auth';
import { useAuthStore } from '@/store/authStore';
import { OtpInput } from '@/components/ui/OtpInput';
import { Keypad } from '@/components/ui/Keypad';

const CODE_LENGTH = 6;
const RESEND_SECONDS = 30;

export default function OtpScreen() {
  const router = useRouter();
  const { setTokens } = useAuthStore();
  const { phone, raw } = useLocalSearchParams<{ phone: string; raw: string }>();

  const [code, setCode]       = useState('');
  const [verifying, setVerifying] = useState(false);
  const [error, setError]     = useState(false);
  const [seconds, setSeconds] = useState(RESEND_SECONDS);

  useEffect(() => {
    if (seconds <= 0) return;
    const t = setTimeout(() => setSeconds((s) => s - 1), 1000);
    return () => clearTimeout(t);
  }, [seconds]);

  const masked = raw
    ? `+91 ${raw.slice(0, 2)} ●●●● ${raw.slice(6)}`
    : phone ?? '';

  async function verify(full: string) {
    if (!phone) return;
    setVerifying(true);
    setError(false);
    try {
      const tokens = await verifyLoginOtp(phone, full);
      await setTokens(tokens);
      router.replace('/(app)/home');
    } catch (err: unknown) {
      setError(true);
      setCode('');
      Alert.alert(
        'Verification failed',
        err instanceof Error ? err.message : 'That code did not match. Try again.',
      );
    } finally {
      setVerifying(false);
    }
  }

  async function resend() {
    if (seconds > 0 || !phone) return;
    try {
      await sendLoginOtp(phone);
      setSeconds(RESEND_SECONDS);
      setCode('');
      setError(false);
    } catch (err: unknown) {
      Alert.alert('Could not resend', err instanceof Error ? err.message : 'Try again shortly.');
    }
  }

  return (
    <SafeAreaView className="flex-1 bg-cream">
      <StatusBar style="dark" />
      <View className="flex-1 px-6 pt-6">
        {/* Icon tile */}
        <View className="h-12 w-12 items-center justify-center rounded-2xl bg-olive-100">
          <MaterialCommunityIcons name="truck-fast-outline" size={24} color="#4A552A" />
        </View>

        <Text className="mt-8 text-4xl font-extrabold text-ink">Enter the code</Text>
        <Text className="mt-2 text-base text-ink-muted">
          Sent to <Text className="font-bold text-ink-soft">{masked}</Text>
        </Text>

        {/* Code cells (display only — driven by the keypad below) */}
        <View className="mt-8">
          <OtpInput
            value={code}
            onChangeText={setCode}
            length={CODE_LENGTH}
            editable={false}
            hasError={error}
            onComplete={(full) => void verify(full)}
          />
        </View>

        {/* Resend */}
        <View className="mt-6 flex-row items-center">
          {seconds > 0 ? (
            <Text className="text-sm text-ink-faint">
              Resend code in {seconds}s
            </Text>
          ) : (
            <Pressable onPress={() => void resend()} hitSlop={8}>
              <Text className="text-sm font-bold text-olive-700">Resend code</Text>
            </Pressable>
          )}
          {verifying ? (
            <Text className="ml-auto text-sm text-ink-faint">Verifying…</Text>
          ) : null}
        </View>

        <View className="flex-1" />

        {/* Custom numeric keypad */}
        <View className="pb-2">
          <Keypad value={code} onChange={(next) => { setError(false); setCode(next); if (next.length === CODE_LENGTH) void verify(next); }} maxLength={CODE_LENGTH} />
        </View>
      </View>
    </SafeAreaView>
  );
}
