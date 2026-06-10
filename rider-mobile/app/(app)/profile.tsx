/**
 * Profile — rider identity, performance and account actions.
 * Wired to GET /api/v1/rider/me.
 */
import React, { useState } from 'react';
import { Alert, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import { useMyRiderProfile } from '@/hooks/useRider';
import { useAuthStore } from '@/store/authStore';
import { useDutyStore } from '@/store/dutyStore';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { Avatar } from '@/components/ui/Avatar';
import { Button } from '@/components/ui/Button';
import { useTranslation } from 'react-i18next';
import { changeLanguage, getActiveLocale, type AppLocale } from '@/i18n';

function StatCard({ icon, value, label }: { icon: React.ComponentProps<typeof Ionicons>['name']; value: string; label: string }) {
  return (
    <View className="flex-1 items-center rounded-2xl bg-white py-4" style={{ elevation: 1 }}>
      <Ionicons name={icon} size={18} color="#4A552A" />
      <Text className="mt-1 text-lg font-extrabold text-ink">{value}</Text>
      <Text className="text-[11px] text-ink-muted">{label}</Text>
    </View>
  );
}

function Row({ label, value }: { label: string; value?: string | null }) {
  return (
    <View className="flex-row items-center justify-between border-b border-cream-200 py-3.5">
      <Text className="text-sm text-ink-muted">{label}</Text>
      <Text className="ml-4 flex-1 text-right text-sm font-semibold text-ink" numberOfLines={1}>
        {value || '—'}
      </Text>
    </View>
  );
}

export default function ProfileScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const { logout, rider, setRider } = useAuthStore();
  const { setOnDuty } = useDutyStore();
  const { data: me, isLoading } = useMyRiderProfile();
  const [activeLocale, setActiveLocale] = useState<AppLocale>(getActiveLocale());

  React.useEffect(() => { if (me) setRider(me); }, [me, setRider]);

  const profile = me ?? rider;

  const handleLanguageChange = async (locale: AppLocale) => {
    await changeLanguage(locale);
    setActiveLocale(locale);
  };

  function handleLogout() {
    Alert.alert(t('profile.logOutConfirm'), t('profile.logOutMessage'), [
      { text: t('common.cancel'), style: 'cancel' },
      {
        text: t('profile.logOut'),
        style: 'destructive',
        onPress: async () => {
          setOnDuty(false);
          await logout();
          router.replace('/(auth)/login');
        },
      },
    ]);
  }

  if (isLoading && !profile) return <ScreenLoader />;

  const name = profile?.riderName?.trim() || profile?.riderCode || 'Rider';
  const rating = profile?.ratingAverage != null ? profile.ratingAverage.toFixed(1) : '—';
  const completion = profile?.completionRate != null ? `${Math.round(profile.completionRate * 100)}%` : '—';

  return (
    <View className="flex-1 bg-cream">
      <StatusBar style="dark" />
      <SafeAreaView className="flex-1" edges={['top', 'left', 'right']}>
        <View className="flex-row items-center px-4 pb-1 pt-1">
          <Pressable onPress={() => router.back()} hitSlop={8} accessibilityRole="button" accessibilityLabel={t('a11y.back')} className="h-9 w-9 items-center justify-center active:opacity-60">
            <Ionicons name="chevron-back" size={24} color="#1E2119" />
          </Pressable>
          <Text className="flex-1 text-center text-base font-extrabold text-ink">{t('profile.title')}</Text>
          <View className="h-9 w-9" />
        </View>

        <ScrollView contentContainerStyle={{ padding: 20, paddingBottom: 40 }} showsVerticalScrollIndicator={false}>
          {/* Identity */}
          <View className="items-center">
            <Avatar name={name} size={84} textClassName="text-2xl" />
            <Text className="mt-3 text-xl font-extrabold text-ink">{name}</Text>
            <Text className="mt-0.5 text-sm text-ink-muted">{profile?.riderCode}</Text>
            {profile?.kycStatus ? (
              <View className={`mt-2 flex-row items-center gap-1 rounded-full px-3 py-1 ${profile.kycStatus === 'verified' ? 'bg-olive-100' : 'bg-gold-100'}`}>
                <Ionicons name={profile.kycStatus === 'verified' ? 'shield-checkmark' : 'shield-outline'} size={12} color={profile.kycStatus === 'verified' ? '#4A552A' : '#8A641D'} />
                <Text className={`text-[11px] font-bold ${profile.kycStatus === 'verified' ? 'text-olive-800' : 'text-gold-700'}`}>
                  {profile.kycStatus === 'verified' ? t('profile.kycVerified') : t('profile.kycPending')}
                </Text>
              </View>
            ) : null}
          </View>

          {/* Stats */}
          <View className="mt-6 flex-row gap-3">
            <StatCard icon="star" value={rating} label={t('profile.rating')} />
            <StatCard icon="cube" value={String(profile?.lifetimeDeliveries ?? 0)} label={t('profile.deliveries')} />
            <StatCard icon="checkmark-done" value={completion} label={t('profile.completion')} />
          </View>

          {/* Details */}
          <View className="mt-6 rounded-3xl bg-white px-4" style={{ elevation: 1 }}>
            <Row label={t('profile.phone')} value={profile?.phone} />
            <Row label={t('profile.email')} value={profile?.email} />
            <Row label={t('profile.franchise')} value={profile?.franchiseName} />
            <Row label={t('profile.store')} value={profile?.primaryStoreName} />
            <Row label={t('profile.vehicle')} value={[profile?.vehicleType, profile?.vehicleNumber].filter(Boolean).join(' · ') || null} />
            <Row label={t('profile.employment')} value={profile?.employmentType} />
            <Row label={t('profile.status')} value={profile?.status} />
          </View>

          {/* Quick links */}
          <View className="mt-6 flex-row gap-3">
            <Pressable
              onPress={() => router.push('/(app)/earnings')}
              className="flex-1 flex-row items-center gap-2 rounded-2xl bg-white px-4 py-3.5 active:opacity-70"
              style={{ elevation: 1 }}
              accessibilityRole="button"
              accessibilityLabel={t('a11y.earnings')}
            >
              <Ionicons name="trending-up-outline" size={18} color="#4A552A" />
              <Text className="flex-1 text-sm font-bold text-ink">{t('profile.earnings')}</Text>
              <Ionicons name="chevron-forward" size={14} color="#A8A493" />
            </Pressable>
            <Pressable
              onPress={() => router.push('/(app)/cash')}
              className="flex-1 flex-row items-center gap-2 rounded-2xl bg-white px-4 py-3.5 active:opacity-70"
              style={{ elevation: 1 }}
              accessibilityRole="button"
              accessibilityLabel={t('a11y.cash')}
            >
              <Ionicons name="cash-outline" size={18} color="#8A641D" />
              <Text className="flex-1 text-sm font-bold text-ink">{t('profile.cash')}</Text>
              <Ionicons name="chevron-forward" size={14} color="#A8A493" />
            </Pressable>
          </View>

          {/* Language switcher */}
          <View
            className="mt-4 rounded-3xl bg-white px-4 py-4"
            accessibilityRole="radiogroup"
            accessibilityLabel={t('language.title')}
          >
            <View className="flex-row items-center justify-between">
              <View className="flex-row items-center gap-3">
                <Ionicons name="language" size={18} color="#4A552A" />
                <Text className="text-sm font-bold text-ink">{t('language.title')}</Text>
              </View>
              <View className="flex-row gap-2">
                <Pressable
                  onPress={() => void handleLanguageChange('en')}
                  accessibilityRole="radio"
                  accessibilityState={{ checked: activeLocale === 'en' }}
                  accessibilityLabel={t('a11y.langEn')}
                  className={`rounded-full px-4 py-1.5 ${activeLocale === 'en' ? 'bg-olive-700' : 'border border-cream-300 bg-white'}`}
                >
                  <Text className={`text-sm font-bold ${activeLocale === 'en' ? 'text-white' : 'text-ink-muted'}`}>EN</Text>
                </Pressable>
                <Pressable
                  onPress={() => void handleLanguageChange('hi')}
                  accessibilityRole="radio"
                  accessibilityState={{ checked: activeLocale === 'hi' }}
                  accessibilityLabel={t('a11y.langHi')}
                  className={`rounded-full px-4 py-1.5 ${activeLocale === 'hi' ? 'bg-olive-700' : 'border border-cream-300 bg-white'}`}
                >
                  <Text className={`text-sm font-bold ${activeLocale === 'hi' ? 'text-white' : 'text-ink-muted'}`}>हिन्दी</Text>
                </Pressable>
              </View>
            </View>
          </View>

          <View className="mt-4">
            <Button title={t('profile.logOut')} variant="danger" fullWidth iconLeft="log-out-outline" onPress={handleLogout} />
          </View>

          <Text className="mt-5 text-center text-xs text-ink-faint">{t('common.version')}</Text>
        </ScrollView>
      </SafeAreaView>
    </View>
  );
}
