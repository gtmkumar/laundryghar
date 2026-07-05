/**
 * OTP verification — "Enter the code".
 * Display-only OtpInput cells driven by a custom numeric Keypad (no system
 * keyboard). Auto-submits on the last digit.
 *
 * NOTE: the mockup shows 4 cells, but the Identity service issues 6-digit
 * codes for the customer login flow, so we render 6 — correctness over pixels.
 *
 * R3-BE-6: DPDP consent
 *   - When the backend returns isNewCustomer=true, we show a consent modal
 *     before navigating to the app.
 *   - The consent checkbox is required to proceed.
 *   - Calls POST /customer/consents/grant (Catalog service) if backend returns
 *     isNewCustomer=true. This is a best-effort call — navigation proceeds even
 *     if the grant fails (the consent can be re-recorded later, and the backend
 *     already defaults marketing opt-ins to false per DPDP Act 2023).
 *   - For returning customers no consent UI is shown.
 */
import React, { useEffect, useState } from 'react';
import { ActivityIndicator, Alert, Linking, Modal, Pressable, Text, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import { StatusBar } from 'expo-status-bar';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Ionicons } from '@expo/vector-icons';
import { OtpInput } from '@/components/ui/OtpInput';
import { Keypad } from '@/components/ui/Keypad';
import { verifyOtp, sendOtp } from '@/api/auth';
import { grantConsent } from '@/api/catalog';
import { useAuthStore } from '@/store/authStore';
import { maskPhone } from '@/lib/format';
import { useTranslation } from 'react-i18next';

const CODE_LENGTH = 6;
const RESEND_SECONDS = 30;

/** Privacy policy version string — bump when the policy changes. */
const PRIVACY_POLICY_VERSION = '1.0';
const PRIVACY_POLICY_URL = 'https://laundryghar.in/privacy';

// ── DPDP Consent modal (shown for new customers only) ────────────────────────

interface ConsentModalProps {
  visible: boolean;
  onAccept: () => void;
  /** Whether the consent API call is in flight. */
  saving: boolean;
}

function ConsentModal({ visible, onAccept, saving }: ConsentModalProps) {
  const { t } = useTranslation();
  const [checked, setChecked] = useState(false);

  return (
    <Modal
      visible={visible}
      animationType="slide"
      presentationStyle="pageSheet"
      // Non-dismissible — user must accept to proceed.
      onRequestClose={() => undefined}
    >
      <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
        <View className="flex-1 px-6 pt-6">
          {/* Icon */}
          <View className="mb-5 h-16 w-16 items-center justify-center rounded-3xl bg-olive-100">
            <Ionicons name="shield-checkmark-outline" size={32} color="#4A552A" />
          </View>

          <Text className="text-2xl font-extrabold text-ink">{t('consent.dataProcessing').split('.')[0]}</Text>
          <Text className="mt-3 text-sm leading-6 text-ink-muted">
            We collect and process your name, phone number, address, and order history to deliver
            our laundry services to you. Your data is stored securely and never sold to third parties,
            as required by the{' '}
            <Text className="font-bold text-ink">Digital Personal Data Protection Act, 2023</Text>.
          </Text>

          {/* Checkbox row */}
          <Pressable
            onPress={() => setChecked((v) => !v)}
            className="mt-6 flex-row items-start gap-3 rounded-2xl bg-white p-4"
            accessibilityRole="checkbox"
            accessibilityState={{ checked }}
            accessibilityLabel={t('consent.checkboxLabel')}
          >
            <View
              className={`mt-0.5 h-6 w-6 items-center justify-center rounded-lg border-2 flex-shrink-0 ${
                checked ? 'border-olive-700 bg-olive-700' : 'border-cream-300 bg-white'
              }`}
            >
              {checked ? <Ionicons name="checkmark" size={14} color="#FFFFFF" /> : null}
            </View>
            <Text className="flex-1 text-sm leading-5 text-ink">
              {t('consent.dataProcessing')}{' '}
              <Text
                className="font-bold text-olive-700"
                onPress={() => void Linking.openURL(PRIVACY_POLICY_URL).catch(() => undefined)}
                accessibilityRole="link"
              >
                {t('consent.privacyPolicy')}
              </Text>
            </Text>
          </Pressable>

          <View className="flex-1" />

          {/* Continue button */}
          <Pressable
            onPress={() => {
              if (!checked) {
                Alert.alert('Required', t('consent.required'));
                return;
              }
              onAccept();
            }}
            disabled={saving}
            className={[
              'mb-6 flex-row items-center justify-center gap-2 rounded-2xl py-4',
              checked ? 'bg-olive-700' : 'bg-cream-300',
            ].join(' ')}
            accessibilityRole="button"
            accessibilityLabel={t('common.continue')}
            accessibilityState={{ disabled: saving || !checked }}
          >
            {saving ? <ActivityIndicator size="small" color="#FFFFFF" /> : null}
            <Text className={`text-base font-extrabold ${checked ? 'text-white' : 'text-ink-faint'}`}>
              {saving ? t('common.loading') : t('common.continue')}
            </Text>
          </Pressable>
        </View>
      </SafeAreaView>
    </Modal>
  );
}

// ── Main OTP screen ───────────────────────────────────────────────────────────

export default function OtpScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const { phone, raw } = useLocalSearchParams<{ phone: string; raw: string }>();
  const { setTokens } = useAuthStore();

  const [code, setCode] = useState('');
  const [verifying, setVerifying] = useState(false);
  const [error, setError] = useState(false);
  const [seconds, setSeconds] = useState(RESEND_SECONDS);

  // R3-BE-6: consent state
  const [consentVisible, setConsentVisible] = useState(false);
  const [consentSaving, setConsentSaving] = useState(false);
  // Store tokens after verify so we can set them after consent.
  const [pendingTokens, setPendingTokens] = useState<Parameters<typeof setTokens>[0] | null>(null);

  useEffect(() => {
    if (seconds <= 0) return;
    const timer = setTimeout(() => setSeconds((s) => s - 1), 1000);
    return () => clearTimeout(timer);
  }, [seconds]);

  // DEV convenience: the non-prod backend accepts a master OTP of 123456
  // (see WhatsApp/SMS OTP routing). Prefill it and auto-verify so testers skip
  // manual entry. Guarded by __DEV__ so it is stripped from production builds.
  const devAutofilled = React.useRef(false);
  useEffect(() => {
    if (!__DEV__ || devAutofilled.current || !phone) return;
    devAutofilled.current = true;
    setCode('123456');
    void verify('123456');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phone]);

  // Mask derived from the number the user actually entered — no hardcoded prefix.
  const masked = raw ? maskPhone(raw) : (phone ?? '');

  async function verify(full: string) {
    if (!phone) return;
    setVerifying(true);
    setError(false);
    try {
      const tokens = await verifyOtp(phone, full);
      if (tokens.isNewCustomer) {
        // R3-BE-6: show consent modal before logging in
        setPendingTokens(tokens);
        setConsentVisible(true);
      } else {
        await setTokens(tokens);
        router.replace('/(app)/(tabs)/home');
      }
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

  /** Called when the user accepts the DPDP consent modal. */
  async function handleConsentAccept() {
    if (!pendingTokens) return;
    setConsentSaving(true);

    // Best-effort: record consent server-side. We first set tokens so the API call
    // is authenticated, then navigate regardless of the consent call outcome.
    await setTokens(pendingTokens);

    try {
      await grantConsent({
        purpose: 'service_delivery',
        purposeDescription:
          'Processing of personal data (name, phone, address, order history) to deliver laundry services.',
        dataCategories: ['contact', 'address', 'order_history'],
        consentMethod: 'explicit_checkbox',
        privacyPolicyVersion: PRIVACY_POLICY_VERSION,
        termsVersion: null,
        consentTextSnapshot:
          'I agree to the processing of my personal data as described in the Privacy Policy (DPDP Act 2023).',
      });
    } catch {
      // Non-fatal — backend defaulted marketing opt-ins to false on signup already.
      // TODO: queue for retry if network unavailable.
    } finally {
      setConsentSaving(false);
      setConsentVisible(false);
      router.replace('/(app)/(tabs)/home');
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

      {/* R3-BE-6: DPDP consent modal for new customers */}
      <ConsentModal
        visible={consentVisible}
        onAccept={() => void handleConsentAccept()}
        saving={consentSaving}
      />
    </SafeAreaView>
  );
}
