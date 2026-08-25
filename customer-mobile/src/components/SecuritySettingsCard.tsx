/**
 * "Sign-in & security" card for Account settings.
 *
 * This is the voluntary counterpart to the sign-up flow: everything skipped during
 * onboarding — the mobile number, the unlock PIN, biometrics — can be completed here at
 * any time, and nothing is ever demanded.
 *
 * Biometric unlock is a device preference, so its toggle lives entirely on-device; the
 * PIN and phone number are account state and round-trip to Identity.
 */
import React, { useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, Switch, Text, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { FEATURES } from '@/constants/config';
import {
  biometricLabel,
  getBiometricKind,
  isBiometricAvailable,
  isBiometricUnlockEnabled,
  setBiometricUnlockEnabled,
  type BiometricKind,
} from '@/lib/biometrics';
import type { CustomerMeResponse } from '@/types/api';

type IoniconName = React.ComponentProps<typeof Ionicons>['name'];

function Row({
  icon,
  label,
  value,
  onPress,
  right,
}: {
  icon: IoniconName;
  label: string;
  value?: string;
  onPress?: () => void;
  right?: React.ReactNode;
}) {
  const body = (
    <View className="flex-row items-center border-b border-cream-200 py-4 last:border-0">
      <View className="mr-3 h-9 w-9 items-center justify-center rounded-xl bg-cream-100">
        <Ionicons name={icon} size={18} color="#5C6A33" />
      </View>
      <View className="flex-1">
        <Text className="text-base font-semibold text-ink">{label}</Text>
        {value ? <Text className="mt-0.5 text-xs text-ink-muted">{value}</Text> : null}
      </View>
      {right ?? (onPress ? <Ionicons name="chevron-forward" size={18} color="#A8A493" /> : null)}
    </View>
  );

  if (!onPress) return body;

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={label}
      className="active:opacity-70"
    >
      {body}
    </Pressable>
  );
}

export function SecuritySettingsCard({ me }: { me: CustomerMeResponse | null | undefined }) {
  const router = useRouter();
  const { t } = useTranslation();

  const [biometricKind, setBiometricKind] = useState<BiometricKind>('none');
  const [biometricOn, setBiometricOn] = useState(false);
  const [loadingBiometrics, setLoadingBiometrics] = useState(true);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        if (!(await isBiometricAvailable())) return;
        const [kind, enabled] = await Promise.all([
          getBiometricKind(),
          isBiometricUnlockEnabled(),
        ]);
        if (cancelled) return;
        setBiometricKind(kind);
        setBiometricOn(enabled);
      } finally {
        if (!cancelled) setLoadingBiometrics(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  async function toggleBiometrics(next: boolean) {
    setBiometricOn(next);
    await setBiometricUnlockEnabled(next);
  }

  const hasPhone = Boolean(me?.phone);
  const hasPin = Boolean(me?.hasPin);
  const googleLinked = me?.linkedProviders?.includes('google') ?? false;

  return (
    <View
      className="mx-6 mt-5 rounded-3xl bg-white px-4"
      style={{
        shadowColor: '#2E351C',
        shadowOpacity: 0.04,
        shadowRadius: 8,
        shadowOffset: { width: 0, height: 2 },
        elevation: 1,
      }}
    >
      <Text className="pb-1 pt-4 text-xs font-bold uppercase tracking-wider text-ink-muted">
        {t('security.sectionTitle')}
      </Text>

      {/* Mobile number — the step a Google sign-up is allowed to skip. */}
      <Row
        icon="call-outline"
        label={hasPhone ? t('security.mobileNumber') : t('security.addMobileNumber')}
        value={hasPhone ? me?.phone ?? undefined : t('security.notAddedYet')}
        onPress={hasPhone ? undefined : () => router.push('/(auth)/link-phone')}
        right={
          hasPhone && me?.phoneVerified ? (
            <Ionicons name="checkmark-circle" size={20} color="#5C6A33" />
          ) : undefined
        }
      />

      {/* Google link — read-only status; unlinking would need a second sign-in method
          to exist first, so it is deliberately not offered here. */}
      {googleLinked ? (
        <Row
          icon="logo-google"
          label={t('security.googleLinked')}
          value={me?.email ?? undefined}
          right={<Ionicons name="checkmark-circle" size={20} color="#5C6A33" />}
        />
      ) : null}

      {FEATURES.pinUnlock ? (
        <>
          <Row
            icon="lock-closed-outline"
            label={hasPin ? t('security.changePin') : t('security.setPin')}
            value={hasPin ? t('security.pinActive') : t('security.pinInactive')}
            onPress={() => router.push('/(auth)/secure')}
          />

          {biometricKind !== 'none' ? (
            <Row
              icon={biometricKind === 'face' ? 'scan-outline' : 'finger-print-outline'}
              label={t('security.biometricUnlock', { method: biometricLabel(biometricKind) })}
              // Without a PIN there is no fallback when the sensor fails, so the toggle
              // stays disabled until one exists.
              value={hasPin ? undefined : t('security.biometricNeedsPin')}
              right={
                loadingBiometrics ? (
                  <ActivityIndicator size="small" color="#5C6A33" />
                ) : (
                  <Switch
                    value={biometricOn && hasPin}
                    disabled={!hasPin}
                    onValueChange={(next) => void toggleBiometrics(next)}
                    trackColor={{ false: '#E3DDCD', true: '#8C9A5B' }}
                    thumbColor="#FFFFFF"
                    accessibilityLabel={t('security.biometricUnlock', {
                      method: biometricLabel(biometricKind),
                    })}
                  />
                )
              }
            />
          ) : null}
        </>
      ) : null}
    </View>
  );
}
