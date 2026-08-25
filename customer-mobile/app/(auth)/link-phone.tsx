/**
 * Optional "add your mobile number" step, shown once after a Google sign-up.
 *
 * This screen is reached with a valid session already in hand — the customer IS signed
 * in. Nothing here is required: Skip goes straight to the dashboard, and the number can
 * be added later from Account settings. That is the whole point of the friction-free
 * sign-up rule; the only thing we insist on is that a number, once given, is verified.
 *
 * Two steps in one screen (entry → code) so skipping stays one tap away throughout.
 */
import React, { useEffect, useState } from 'react';
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
import { StatusBar } from 'expo-status-bar';
import { Ionicons } from '@expo/vector-icons';
import { Button } from '@/components/ui/Button';
import { OtpInput } from '@/components/ui/OtpInput';
import { Keypad } from '@/components/ui/Keypad';
import { sendPhoneLinkOtp, verifyPhoneLinkOtp } from '@/api/auth';
import { OTP_LENGTH, FEATURES } from '@/constants/config';
import { useAuthStore } from '@/store/authStore';
import { maskPhone } from '@/lib/format';
import { useTranslation } from 'react-i18next';

const RESEND_SECONDS = 30;

function normalizePhone(raw: string): string {
  const digits = raw.replace(/\D/g, '');
  if (digits.length === 10) return `+91${digits}`;
  if (digits.length === 12 && digits.startsWith('91')) return `+${digits}`;
  return raw;
}

function isValid(raw: string): boolean {
  const digits = raw.replace(/\D/g, '');
  return digits.length === 10 || (digits.length === 12 && digits.startsWith('91'));
}

export default function LinkPhoneScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const { isNew } = useLocalSearchParams<{ isNew?: string }>();
  const { rememberIdentifier } = useAuthStore();

  const [step, setStep] = useState<'entry' | 'code'>('entry');
  const [phone, setPhone] = useState('');
  const [normalized, setNormalized] = useState('');
  const [code, setCode] = useState('');
  const [sending, setSending] = useState(false);
  const [verifying, setVerifying] = useState(false);
  const [error, setError] = useState(false);
  const [seconds, setSeconds] = useState(RESEND_SECONDS);

  useEffect(() => {
    if (step !== 'code' || seconds <= 0) return;
    const timer = setTimeout(() => setSeconds((s) => s - 1), 1000);
    return () => clearTimeout(timer);
  }, [seconds, step]);

  /**
   * Skip and Done land in the same place. A brand-new customer is offered the PIN
   * setup first (also skippable); a returning one goes straight to the dashboard.
   */
  function finish() {
    if (isNew === '1' && FEATURES.pinUnlock) {
      router.replace('/(auth)/secure');
    } else {
      router.replace('/(app)/(tabs)/home');
    }
  }

  async function handleSend() {
    if (!isValid(phone)) {
      Alert.alert(t('auth.invalidNumber'), t('auth.invalidNumberMessage'));
      return;
    }
    setSending(true);
    try {
      const e164 = normalizePhone(phone);
      await sendPhoneLinkOtp(e164);
      setNormalized(e164);
      setStep('code');
      setSeconds(RESEND_SECONDS);
    } catch (err: unknown) {
      Alert.alert('Error', err instanceof Error ? err.message : t('auth.errorSendingOtp'));
    } finally {
      setSending(false);
    }
  }

  async function handleVerify(full: string) {
    setVerifying(true);
    setError(false);
    try {
      await verifyPhoneLinkOtp(normalized, full);
      await rememberIdentifier(normalized);
      finish();
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

  async function handleResend() {
    if (seconds > 0) return;
    try {
      await sendPhoneLinkOtp(normalized);
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
      <KeyboardAvoidingView
        className="flex-1"
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      >
        <ScrollView
          contentContainerStyle={{ flexGrow: 1 }}
          keyboardShouldPersistTaps="handled"
          showsVerticalScrollIndicator={false}
        >
          <View className="flex-1 px-7 pt-4">
            {/* Skip is always reachable — this whole screen is optional. */}
            <View className="flex-row items-center justify-end">
              <Pressable
                onPress={finish}
                hitSlop={10}
                accessibilityRole="button"
                accessibilityLabel={t('linkPhone.skipA11y')}
              >
                <Text className="text-base font-bold text-ink-muted">{t('common.skip')}</Text>
              </Pressable>
            </View>

            <View className="mt-4 h-14 w-14 items-center justify-center rounded-3xl bg-olive-100">
              <Ionicons name="phone-portrait-outline" size={28} color="#4A552A" />
            </View>

            {step === 'entry' ? (
              <>
                <Text className="mt-6 text-3xl font-extrabold text-ink">
                  {t('linkPhone.title')}
                </Text>
                <Text className="mt-2 text-base text-ink-muted">
                  {t('linkPhone.subtitle')}
                </Text>

                <Text className="mb-1.5 mt-8 text-xs font-bold uppercase tracking-wider text-ink-muted">
                  {t('auth.phoneLabel')}
                </Text>
                <View className="flex-row items-center rounded-2xl border border-cream-300 bg-white px-4">
                  <Text className="text-base">🇮🇳</Text>
                  <Text className="ml-2 mr-3 text-base font-bold text-ink">+91</Text>
                  <View className="h-6 w-px bg-cream-300" />
                  <RNTextInput
                    value={phone}
                    onChangeText={setPhone}
                    placeholder={t('auth.phonePlaceholder')}
                    placeholderTextColor="#A8A493"
                    keyboardType="phone-pad"
                    maxLength={11}
                    autoFocus
                    returnKeyType="done"
                    onSubmitEditing={() => void handleSend()}
                    className="ml-3 flex-1 py-4 text-base font-semibold text-ink"
                    accessibilityLabel="Phone number"
                  />
                </View>

                <View className="mt-7">
                  <Button
                    title={t('auth.sendOtp')}
                    size="lg"
                    fullWidth
                    loading={sending}
                    iconRight="arrow-forward"
                    onPress={() => void handleSend()}
                  />
                </View>

                <Text className="mt-4 text-center text-[13px] leading-5 text-ink-faint">
                  {t('linkPhone.whyWeAsk')}
                </Text>
              </>
            ) : (
              <>
                <Text className="mt-6 text-3xl font-extrabold text-ink">
                  {t('auth.enterCode')}
                </Text>
                <Text className="mt-2 text-base text-ink-muted">
                  {t('auth.codeSentTo')}{' '}
                  <Text className="font-bold text-ink-soft">
                    {maskPhone(normalized.replace('+91', ''))}
                  </Text>
                </Text>

                <View className="mt-8">
                  <OtpInput value={code} length={OTP_LENGTH} hasError={error} />
                </View>

                <View className="mt-6 flex-row items-center">
                  {seconds > 0 ? (
                    <Text className="text-sm text-ink-faint">
                      {t('auth.didntGetIt')}{' '}
                      <Text className="font-bold text-ink-soft">
                        0:{String(seconds).padStart(2, '0')}
                      </Text>
                    </Text>
                  ) : (
                    <Pressable
                      onPress={() => void handleResend()}
                      hitSlop={8}
                      accessibilityRole="button"
                      accessibilityLabel={t('a11y.resendCode')}
                    >
                      <Text className="text-sm font-bold text-olive-700">
                        {t('auth.resendCode')}
                      </Text>
                    </Pressable>
                  )}
                  {verifying ? (
                    <Text className="ml-auto text-sm text-ink-faint">{t('auth.verifying')}</Text>
                  ) : null}
                </View>

                <View className="flex-1" />

                <View className="pb-2">
                  <Keypad
                    value={code}
                    maxLength={OTP_LENGTH}
                    onChange={(next) => {
                      setError(false);
                      setCode(next);
                      if (next.length === OTP_LENGTH) void handleVerify(next);
                    }}
                  />
                </View>
              </>
            )}
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
