/**
 * OTP verification screen.
 * Receives ?phone= param from phone screen.
 * Calls POST /api/v1/customer/auth/otp/verify → stores tokens → enters app.
 */
import React, { useEffect, useRef, useState } from 'react';
import {
  Alert,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  Text,
  TextInput as RNTextInput,
  View,
} from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Button } from '@/components/ui/Button';
import { verifyOtp, sendOtp } from '@/api/auth';
import { useAuthStore } from '@/store/authStore';
import type { CustomerTokenResponse } from '@/types/api';

const OTP_LENGTH = 6;

export default function OtpScreen() {
  const router = useRouter();
  const { phone } = useLocalSearchParams<{ phone: string }>();
  const { setTokens } = useAuthStore();

  const [otp, setOtp]             = useState('');
  const [loading, setLoading]     = useState(false);
  const [resending, setResending] = useState(false);
  const [countdown, setCountdown] = useState(30);
  const inputRef = useRef<RNTextInput>(null);

  // Countdown timer for resend
  useEffect(() => {
    if (countdown <= 0) return;
    const timer = setInterval(() => setCountdown((c) => c - 1), 1_000);
    return () => clearInterval(timer);
  }, [countdown]);

  const handleVerify = async () => {
    if (otp.length !== OTP_LENGTH) return;
    setLoading(true);
    try {
      const tokens: CustomerTokenResponse = await verifyOtp(phone ?? '', otp);
      await setTokens(tokens);
      // Replace the entire auth stack so back-button cannot return here
      router.replace('/(app)/(tabs)/home');
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'Invalid OTP. Please try again.';
      Alert.alert('Verification Failed', message);
      setOtp('');
    } finally {
      setLoading(false);
    }
  };

  const handleResend = async () => {
    if (!phone || countdown > 0 || resending) return;
    setResending(true);
    try {
      await sendOtp(phone);
      setCountdown(30);
      setOtp('');
      Alert.alert('OTP Sent', `A new OTP has been sent to ${phone}`);
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to resend OTP';
      Alert.alert('Error', message);
    } finally {
      setResending(false);
    }
  };

  // Render 6 digit boxes atop a hidden input
  const digits = otp.split('').concat(Array(OTP_LENGTH - otp.length).fill(''));

  return (
    <SafeAreaView className="flex-1 bg-white">
      <KeyboardAvoidingView
        className="flex-1"
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
      >
        <ScrollView
          contentContainerStyle={{ flexGrow: 1 }}
          keyboardShouldPersistTaps="handled"
        >
          <View className="flex-1 px-6 pt-12">
            {/* Back */}
            <Pressable
              onPress={() => router.back()}
              accessibilityRole="button"
              accessibilityLabel="Go back"
              className="mb-8 self-start"
            >
              <Text className="text-base font-medium text-brand-700">← Back</Text>
            </Pressable>

            {/* Header */}
            <View className="mb-8">
              <Text className="mb-2 text-3xl font-bold text-gray-900">
                Enter OTP
              </Text>
              <Text className="text-base text-gray-500">
                We sent a 6-digit code to{' '}
                <Text className="font-semibold text-gray-700">{phone}</Text>
              </Text>
            </View>

            {/* OTP boxes */}
            <Pressable
              onPress={() => inputRef.current?.focus()}
              accessibilityLabel="OTP input"
              className="flex-row justify-between mb-8"
            >
              {digits.map((d, i) => (
                <View
                  key={i}
                  className={[
                    'h-14 w-12 items-center justify-center rounded-xl border-2',
                    d ? 'border-brand-700 bg-brand-50' : 'border-gray-300 bg-white',
                    i === otp.length ? 'border-brand-400' : '',
                  ].join(' ')}
                >
                  <Text className="text-2xl font-bold text-gray-900">
                    {d ? '•' : ''}
                  </Text>
                </View>
              ))}
            </Pressable>

            {/* Hidden input captures actual text */}
            <RNTextInput
              ref={inputRef}
              value={otp}
              onChangeText={(t) => {
                const digits_only = t.replace(/\D/g, '').slice(0, OTP_LENGTH);
                setOtp(digits_only);
                if (digits_only.length === OTP_LENGTH) {
                  // Auto-submit
                  setTimeout(() => handleVerify(), 100);
                }
              }}
              keyboardType="number-pad"
              maxLength={OTP_LENGTH}
              style={{ position: 'absolute', opacity: 0, height: 0 }}
              autoFocus
              accessibilityElementsHidden
            />

            <Button
              title="Verify OTP"
              onPress={handleVerify}
              loading={loading}
              disabled={otp.length !== OTP_LENGTH}
              fullWidth
              size="lg"
            />

            {/* Resend */}
            <View className="mt-6 flex-row items-center justify-center gap-1">
              <Text className="text-sm text-gray-500">Didn't receive it?</Text>
              {countdown > 0 ? (
                <Text className="text-sm font-medium text-gray-400">
                  Resend in {countdown}s
                </Text>
              ) : (
                <Pressable
                  onPress={handleResend}
                  disabled={resending}
                  accessibilityRole="button"
                  accessibilityLabel="Resend OTP"
                >
                  <Text className="text-sm font-medium text-brand-700">
                    {resending ? 'Sending…' : 'Resend OTP'}
                  </Text>
                </Pressable>
              )}
            </View>
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
