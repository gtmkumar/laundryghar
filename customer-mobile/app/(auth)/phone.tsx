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
  const [phone, setPhone] = useState('');
  const [agreed, setAgreed] = useState(false);
  const [loading, setLoading] = useState(false);

  const valid = isValid(phone);

  const handleSendOtp = async () => {
    if (!valid) {
      Alert.alert('Invalid number', 'Enter a valid 10-digit mobile number.');
      return;
    }
    if (!agreed) {
      Alert.alert('Please agree', 'Accept the Terms & Privacy policy to continue.');
      return;
    }
    setLoading(true);
    try {
      const normalized = normalizePhone(phone);
      await sendOtp(normalized);
      const raw = normalized.replace('+91', '');
      router.push({ pathname: '/(auth)/otp', params: { phone: normalized, raw } });
    } catch (err: unknown) {
      Alert.alert('Error', err instanceof Error ? err.message : 'Failed to send OTP. Try again.');
    } finally {
      setLoading(false);
    }
  };

  const socialSoon = () =>
    Alert.alert('Coming soon', 'Social sign-in will be available shortly.');

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
              <Text className="text-4xl font-extrabold text-ink">Welcome back</Text>
              <Text style={{ fontSize: 30 }}>👋</Text>
            </View>
            <Text className="mt-2 text-base text-ink-muted">
              Enter your phone — we’ll text you a code.
            </Text>

            {/* Phone input */}
            <Text className="mb-1.5 mt-9 text-xs font-bold uppercase tracking-wider text-ink-muted">
              Phone number
            </Text>
            <View className="flex-row items-center rounded-2xl border border-cream-300 bg-white px-4">
              <Text className="text-base">🇮🇳</Text>
              <Text className="ml-2 mr-3 text-base font-bold text-ink">+91</Text>
              <View className="h-6 w-px bg-cream-300" />
              <RNTextInput
                value={phone}
                onChangeText={setPhone}
                placeholder="98123 45678"
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
                I agree to <Text className="font-bold text-olive-700">T&C</Text> and{' '}
                <Text className="font-bold text-olive-700">Privacy</Text>
              </Text>
            </Pressable>

            {/* Send OTP */}
            <View className="mt-7">
              <Button
                title="Send OTP"
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
              <Text className="text-xs text-ink-faint">or continue with</Text>
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
                Social sign-in coming soon
              </Text>
            ) : null}

            <View className="flex-1" />

            {/* New user */}
            <View className="flex-row items-center justify-center py-6">
              <Text className="text-sm text-ink-muted">New here? </Text>
              <Pressable onPress={handleSendOtp} hitSlop={6}>
                <Text className="text-sm font-bold text-olive-700">Create account</Text>
              </Pressable>
            </View>
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
