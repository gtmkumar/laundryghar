/**
 * Dismissible "complete your profile" ribbon for the dashboard.
 *
 * Deliberately non-blocking: sign-up collects nothing beyond the credential used to sign
 * in, so most accounts start incomplete and that is fine. This is a nudge, not a gate —
 * order placement collects the address and anything else mandatory at the point it needs
 * it. Dismissal is remembered per missing-field set, so the ribbon comes back only if
 * something new is missing (or the customer fills something in and still has gaps),
 * rather than nagging on every launch.
 */
import React, { useEffect, useState } from 'react';
import { Pressable, Text, View } from 'react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import type { ProfileCompletionDto } from '@/types/api';

/** AsyncStorage, not SecureStore — a dismissal preference is not a secret. */
const KEY_DISMISSED = 'lg_profile_banner_dismissed';

interface Props {
  completion: ProfileCompletionDto | undefined;
  onPress: () => void;
}

export function ProfileCompletionBanner({ completion, onPress }: Props) {
  const { t } = useTranslation();
  const [dismissedFor, setDismissedFor] = useState<string | null>(null);
  const [loaded, setLoaded] = useState(false);

  // Identifies the current gap set, so dismissing "email missing" does not also
  // silence a later "email and phone missing".
  const signature = completion?.missingFields?.slice().sort().join(',') ?? '';

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const stored = await AsyncStorage.getItem(KEY_DISMISSED);
        if (!cancelled) setDismissedFor(stored);
      } catch {
        // Treat a read failure as "not dismissed" — showing the nudge once too often
        // is a smaller failure than hiding it forever.
      } finally {
        if (!cancelled) setLoaded(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  async function dismiss() {
    setDismissedFor(signature);
    try {
      await AsyncStorage.setItem(KEY_DISMISSED, signature);
    } catch {
      // best-effort — it reappears next launch at worst
    }
  }

  // Render nothing until the dismissal state is known, so the banner never flashes in
  // and out on a cold start.
  if (!loaded) return null;
  if (!completion || completion.isComplete) return null;
  if (dismissedFor === signature) return null;

  const labelFor = (field: string): string =>
    field === 'name'  ? t('profile.completion.missingName')
    : field === 'email' ? t('profile.completion.missingEmail')
    : field === 'phone' ? t('profile.completion.missingPhone')
    : field;

  const missingLabel = completion.missingFields.map(labelFor).join(', ');

  return (
    <View className="mx-5 mb-4 flex-row items-center gap-3 rounded-2xl border border-gold-200 bg-gold-50 p-4">
      <View className="h-10 w-10 items-center justify-center rounded-2xl bg-gold-200">
        <Ionicons name="person-circle-outline" size={22} color="#6B5A16" />
      </View>

      <Pressable
        onPress={onPress}
        className="flex-1"
        accessibilityRole="button"
        accessibilityLabel={`${t('profile.completion.title')}. ${t('profile.completion.cta')}`}
      >
        <Text className="text-sm font-extrabold text-ink">{t('profile.completion.title')}</Text>
        <Text className="mt-0.5 text-xs leading-4 text-ink-muted">
          {t('profile.completion.body')}
        </Text>
        {missingLabel ? (
          <Text className="mt-1 text-xs font-semibold text-olive-700">
            {t('profile.completion.percent', { percent: completion.percentComplete })} · {missingLabel}
          </Text>
        ) : null}
      </Pressable>

      <Pressable
        onPress={() => void dismiss()}
        hitSlop={10}
        accessibilityRole="button"
        accessibilityLabel={t('profile.completion.dismiss')}
        className="h-8 w-8 items-center justify-center rounded-full"
      >
        <Ionicons name="close" size={18} color="#8A8578" />
      </Pressable>
    </View>
  );
}
