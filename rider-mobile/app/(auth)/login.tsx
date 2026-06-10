/**
 * Rider sign-in — phone number → OTP.
 *
 * Riders authenticate via the shared OTP flow:
 *   POST /api/v1/auth/otp/send   { identifier:"+91…", identifierType:"phone", purpose:"login" }
 * On success we push the OTP screen, where the rider enters the 6-digit code
 *   POST /api/v1/auth/otp/verify → { accessToken, refreshToken }
 *
 * The phone must already be registered (rider onboarded by an admin/franchise).
 */
import React, { useState } from 'react';
import {
  KeyboardAvoidingView,
  Platform,
  Text,
  TextInput,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import { sendLoginOtp } from '@/api/auth';
import { Button } from '@/components/ui/Button';
import { useTranslation } from 'react-i18next';

/** Tiny India flag — drawn (not emoji) so it renders identically on iOS+Android. */
function IndiaFlag() {
  return (
    <View
      accessibilityLabel="India"
      style={{ width: 22, height: 15, borderRadius: 3, overflow: 'hidden', borderWidth: 0.5, borderColor: '#D2C8B2' }}
    >
      <View style={{ flex: 1, backgroundColor: '#FF9933' }} />
      <View style={{ flex: 1, backgroundColor: '#FFFFFF', alignItems: 'center', justifyContent: 'center' }}>
        <View style={{ width: 4, height: 4, borderRadius: 2, borderWidth: 1, borderColor: '#0A3A8C' }} />
      </View>
      <View style={{ flex: 1, backgroundColor: '#138808' }} />
    </View>
  );
}

export default function LoginScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const [digits, setDigits]   = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError]     = useState('');

  const valid = digits.length === 10;

  async function handleSend() {
    if (!valid) {
      setError(t('auth.errors.invalidPhone'));
      return;
    }
    setError('');
    setLoading(true);
    const e164 = `+91${digits}`;
    try {
      await sendLoginOtp(e164);
      router.push({ pathname: '/(auth)/otp', params: { phone: e164, raw: digits } });
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('auth.errors.tryAgain'));
    } finally {
      setLoading(false);
    }
  }

  // group as "98 7700 1234" for display
  const display = digits
    .replace(/^(\d{2})(\d{0,4})(\d{0,4}).*/, (_, a, b, c) =>
      [a, b, c].filter(Boolean).join(' '),
    );

  return (
    <SafeAreaView className="flex-1 bg-cream">
      <StatusBar style="dark" />
      <KeyboardAvoidingView
        className="flex-1"
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      >
        <View className="flex-1 px-6 pt-6">
          {/* Icon tile */}
          <View className="h-12 w-12 items-center justify-center rounded-2xl bg-olive-100">
            <MaterialCommunityIcons name="truck-fast-outline" size={24} color="#4A552A" />
          </View>

          <Text className="mt-8 text-4xl font-extrabold text-ink">{t('auth.title')}</Text>
          <Text className="mt-2 text-base text-ink-muted">
            {t('auth.subtitle')}
          </Text>

          {/* Phone field */}
          <Text className="mt-9 text-xs font-semibold uppercase tracking-widest text-ink-faint">
            {t('auth.phoneLabel')}
          </Text>
          <View
            className={[
              'mt-2 flex-row items-center rounded-2xl border bg-white px-4',
              error ? 'border-danger' : 'border-cream-300',
            ].join(' ')}
            style={{
              shadowColor: '#000',
              shadowOpacity: 0.04,
              shadowRadius: 6,
              shadowOffset: { width: 0, height: 2 },
              elevation: 1,
            }}
          >
            <IndiaFlag />
            <Text className="ml-2 mr-3 text-lg font-semibold text-ink">+91</Text>
            <View className="h-7 w-px bg-cream-300" />
            <TextInput
              value={display}
              onChangeText={(t) => setDigits(t.replace(/[^0-9]/g, '').slice(0, 10))}
              keyboardType="number-pad"
              placeholder="98 7700 1234"
              placeholderTextColor="#A8A493"
              autoFocus
              maxLength={14}
              className="ml-3 flex-1 py-4 text-lg font-semibold text-ink"
              accessibilityLabel="Mobile number"
              returnKeyType="done"
              onSubmitEditing={() => void handleSend()}
            />
          </View>
          {error ? (
            <Text className="mt-2 text-sm text-danger" accessibilityRole="alert">
              {error}
            </Text>
          ) : null}
        </View>

        {/* Pinned CTA */}
        <View className="px-6 pb-4">
          <Button
            title={t('auth.sendOtp')}
            iconRight="arrow-forward"
            size="lg"
            fullWidth
            loading={loading}
            disabled={!valid}
            onPress={() => void handleSend()}
          />
          <Text className="mt-4 text-center text-xs text-ink-faint">
            {t('auth.troubleSignIn')}
          </Text>
        </View>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
