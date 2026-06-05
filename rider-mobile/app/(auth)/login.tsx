/**
 * Rider Login screen
 *
 * Riders are SYSTEM users with user_type='rider'.
 * Auth: POST {Identity}/api/v1/auth/password/login
 *       body: { identifier, password }
 *       response: SingleResponse<TokenResponse> → data.accessToken + data.refreshToken
 *
 * identifier may be:
 *   - phone number (E.164)
 *   - email address
 *   - rider code (e.g. "RDR-0001")  — whatever the backend accepts as a unique identifier
 */
import React, { useState } from 'react';
import {
  Alert,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { passwordLogin } from '@/api/auth';
import { useAuthStore } from '@/store/authStore';
import { Button } from '@/components/ui/Button';
import { TextInput } from '@/components/ui/TextInput';

export default function LoginScreen() {
  const router      = useRouter();
  const { setTokens } = useAuthStore();

  const [identifier, setIdentifier] = useState('');
  const [password,   setPassword]   = useState('');
  const [loading,    setLoading]     = useState(false);
  const [idError,    setIdError]     = useState('');
  const [pwError,    setPwError]     = useState('');

  function validate(): boolean {
    let ok = true;
    if (!identifier.trim()) {
      setIdError('Phone, email, or rider code is required');
      ok = false;
    } else {
      setIdError('');
    }
    if (!password) {
      setPwError('Password is required');
      ok = false;
    } else {
      setPwError('');
    }
    return ok;
  }

  async function handleLogin() {
    if (!validate()) return;
    setLoading(true);
    try {
      const tokens = await passwordLogin(identifier.trim(), password);
      await setTokens(tokens);
      router.replace('/(app)/(tabs)/assignments');
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'Login failed. Please try again.';
      Alert.alert('Login Failed', message);
    } finally {
      setLoading(false);
    }
  }

  return (
    <SafeAreaView className="flex-1 bg-surface-muted">
      <KeyboardAvoidingView
        className="flex-1"
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
      >
        <ScrollView
          contentContainerStyle={{ flexGrow: 1 }}
          keyboardShouldPersistTaps="handled"
          showsVerticalScrollIndicator={false}
        >
          {/* Header */}
          <View className="bg-brand-700 px-6 pb-12 pt-16 items-center">
            <Text className="text-4xl font-bold text-white">Laundry Ghar</Text>
            <Text className="mt-2 text-base text-brand-200 font-medium">
              Rider Portal
            </Text>
          </View>

          {/* Form card */}
          <View className="mx-6 -mt-6 rounded-2xl bg-white px-6 py-8 shadow-sm" style={{ elevation: 3 }}>
            <Text className="mb-6 text-xl font-bold text-gray-900">
              Sign in to your account
            </Text>

            <View className="gap-4">
              <TextInput
                label="Phone / Email / Rider Code"
                placeholder="Enter your identifier"
                value={identifier}
                onChangeText={setIdentifier}
                autoCapitalize="none"
                keyboardType="email-address"
                returnKeyType="next"
                error={idError}
                hint="Use the phone number, email, or rider code assigned by your manager"
              />

              <TextInput
                label="Password"
                placeholder="Enter your password"
                value={password}
                onChangeText={setPassword}
                secureTextEntry
                returnKeyType="done"
                onSubmitEditing={() => void handleLogin()}
                error={pwError}
              />
            </View>

            <View className="mt-6">
              <Button
                title="Sign In"
                variant="primary"
                size="lg"
                fullWidth
                loading={loading}
                onPress={() => void handleLogin()}
                accessibilityLabel="Sign in to Rider Portal"
              />
            </View>

            <Text className="mt-6 text-center text-xs text-gray-400">
              Contact your manager if you have lost access.
            </Text>
          </View>

          <View className="flex-1" />
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
