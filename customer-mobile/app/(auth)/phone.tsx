/**
 * Login (phone entry) — "Welcome back".
 * Validates an Indian mobile number, requires T&C consent, sends an OTP
 * (POST /customer/auth/otp/send), then routes to the code screen.
 * Google / Apple buttons are presentational until social login ships (FEATURES.socialLogin).
 */
import React, { useState } from 'react';
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
import { useRouter } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import { StatusBar } from 'expo-status-bar';
import { Ionicons, MaterialCommunityIcons, FontAwesome } from '@expo/vector-icons';
import { Button } from '@/components/ui/Button';
import { sendOtp } from '@/api/auth';
import { FEATURES } from '@/constants/config';
import { useTranslation } from 'react-i18next';

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

function SocialButton({
  icon,
  label,
  onPress,
}: {
  icon: React.ReactNode;
  label: string;
  onPress: () => void;
}) {
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={`Continue with ${label}`}
      className="flex-1 flex-row items-center justify-center gap-2 rounded-2xl border border-cream-300 bg-white py-3.5 active:opacity-80"
    >
      {icon}
      <Text className="text-base font-bold text-ink-soft">{label}</Text>
    </Pressable>
  );
}

export default function LoginScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const [phone, setPhone] = useState('');
  const [agreed, setAgreed] = useState(false);
  const [loading, setLoading] = useState(false);

  const valid = isValid(phone);

  const handleSendOtp = async () => {
    if (!valid) {
      Alert.alert(t('auth.invalidNumber'), t('auth.invalidNumberMessage'));
      return;
    }
    if (!agreed) {
      Alert.alert(t('auth.pleaseAgree'), t('auth.pleaseAgreeMessage'));
      return;
    }
    setLoading(true);
    try {
      const normalized = normalizePhone(phone);
      await sendOtp(normalized);
      const raw = normalized.replace('+91', '');
      router.push({ pathname: '/(auth)/otp', params: { phone: normalized, raw } });
    } catch (err: unknown) {
      Alert.alert('Error', err instanceof Error ? err.message : t('auth.errorSendingOtp'));
    } finally {
      setLoading(false);
    }
  };

  const socialSoon = () =>
    Alert.alert(t('auth.social.comingSoon'), t('auth.social.socialSoonMessage'));

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
          <View className="flex-1 px-7 pt-6">
            {/* Logo tile */}
            <View className="h-12 w-12 items-center justify-center rounded-2xl bg-olive-500">
              <MaterialCommunityIcons name="hanger" size={26} color="#FFFFFF" />
            </View>

            <View className="mt-8 flex-row items-center gap-2">
              <Text className="text-4xl font-extrabold text-ink">{t('auth.welcomeBack')}</Text>
              <Text style={{ fontSize: 30 }}>{t('auth.welcomeBackEmoji')}</Text>
            </View>
            <Text className="mt-2 text-base text-ink-muted">
              {t('auth.enterPhone')}
            </Text>

            {/* Phone input */}
            <Text className="mb-1.5 mt-9 text-xs font-bold uppercase tracking-wider text-ink-muted">
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
                onSubmitEditing={handleSendOtp}
                className="ml-3 flex-1 py-4 text-base font-semibold text-ink"
                accessibilityLabel="Phone number"
              />
            </View>

            {/* Consent */}
            <Pressable
              onPress={() => setAgreed((a) => !a)}
              accessibilityRole="checkbox"
              accessibilityState={{ checked: agreed }}
              accessibilityLabel={t('auth.agreeToTerms')}
              className="mt-4 flex-row items-center"
              hitSlop={6}
            >
              <View
                className={`h-5 w-5 items-center justify-center rounded-md border ${
                  agreed ? 'border-olive-600 bg-olive-600' : 'border-cream-400 bg-white'
                }`}
              >
                {agreed ? <Ionicons name="checkmark" size={14} color="#FFFFFF" /> : null}
              </View>
              <Text className="ml-2 text-sm text-ink-muted">
                {t('auth.agreeToTerms')} <Text className="font-bold text-olive-700">{t('auth.termsAndConditions')}</Text>{' '}
                {t('auth.and')} <Text className="font-bold text-olive-700">{t('auth.privacy')}</Text>
              </Text>
            </Pressable>

            {/* Send OTP */}
            <View className="mt-7">
              <Button
                title={t('auth.sendOtp')}
                size="lg"
                fullWidth
                loading={loading}
                iconRight="arrow-forward"
                onPress={handleSendOtp}
              />
            </View>

            {/* Divider */}
            <View className="my-7 flex-row items-center gap-3">
              <View className="h-px flex-1 bg-cream-300" />
              <Text className="text-xs text-ink-faint">{t('common.orContinueWith')}</Text>
              <View className="h-px flex-1 bg-cream-300" />
            </View>

            {/* Social */}
            <View className="flex-row gap-3">
              <SocialButton
                label="Google"
                onPress={socialSoon}
                icon={<FontAwesome name="google" size={18} color="#DB4437" />}
              />
              <SocialButton
                label="Apple"
                onPress={socialSoon}
                icon={<FontAwesome name="apple" size={20} color="#1E2119" />}
              />
            </View>
            {!FEATURES.socialLogin ? (
              <Text className="mt-2 text-center text-[11px] text-ink-faint">
                {t('auth.socialSoon')}
              </Text>
            ) : null}

            <View className="flex-1" />

            {/* New user */}
            <View className="flex-row items-center justify-center py-6">
              <Text className="text-sm text-ink-muted">{t('auth.newHere')} </Text>
              <Pressable onPress={handleSendOtp} hitSlop={6} accessibilityRole="button" accessibilityLabel={t('auth.createAccount')}>
                <Text className="text-sm font-bold text-olive-700">{t('auth.createAccount')}</Text>
              </Pressable>
            </View>
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
