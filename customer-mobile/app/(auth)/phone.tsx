/**
 * Phone entry screen.
 * Validates an Indian mobile number (10 digits, optionally +91 prefix),
 * calls POST /api/v1/customer/auth/otp/send, then routes to OTP screen.
 */
import React, { useState } from 'react';
import {
  Alert,
  KeyboardAvoidingView,
  Platform,
  ScrollView,
  Text,
  View,
} from 'react-native';
import { useRouter } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Button } from '@/components/ui/Button';
import { TextInput } from '@/components/ui/TextInput';
import { sendOtp } from '@/api/auth';

function normalizePhone(raw: string): string {
  const digits = raw.replace(/\D/g, '');
  // Accept 10-digit or 12-digit (91 prefix)
  if (digits.length === 10) return `+91${digits}`;
  if (digits.length === 12 && digits.startsWith('91')) return `+${digits}`;
  return raw;
}

function validatePhone(raw: string): string | null {
  const digits = raw.replace(/\D/g, '');
  if (digits.length !== 10 && !(digits.length === 12 && digits.startsWith('91'))) {
    return 'Enter a valid 10-digit mobile number';
  }
  return null;
}

export default function PhoneScreen() {
  const router = useRouter();
  const [phone, setPhone]   = useState('');
  const [error, setError]   = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const handleSendOtp = async () => {
    const validationErr = validatePhone(phone);
    if (validationErr) {
      setError(validationErr);
      return;
    }
    setError(null);
    setLoading(true);
    try {
      const normalized = normalizePhone(phone);
      await sendOtp(normalized);
      router.push({ pathname: '/(auth)/otp', params: { phone: normalized } });
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'Failed to send OTP. Please try again.';
      Alert.alert('Error', message);
    } finally {
      setLoading(false);
    }
  };

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
            {/* Header */}
            <View className="mb-10">
              <Text className="mb-2 text-3xl font-bold text-gray-900">
                Welcome to Laundry Ghar
              </Text>
              <Text className="text-base text-gray-500">
                Enter your mobile number to continue
              </Text>
            </View>

            {/* Phone input */}
            <TextInput
              label="Mobile Number"
              value={phone}
              onChangeText={(t) => {
                setPhone(t);
                if (error) setError(null);
              }}
              placeholder="98765 43210"
              keyboardType="phone-pad"
              maxLength={14}
              error={error ?? undefined}
              autoFocus
              hint="We'll send you a one-time password"
              returnKeyType="done"
              onSubmitEditing={handleSendOtp}
            />

            <View className="mt-6">
              <Button
                title="Send OTP"
                onPress={handleSendOtp}
                loading={loading}
                fullWidth
                size="lg"
              />
            </View>

            <Text className="mt-6 text-center text-xs text-gray-400">
              By continuing, you agree to our Terms of Service and Privacy Policy.
            </Text>
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
