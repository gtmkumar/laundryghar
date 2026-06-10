/**
 * Profile tab — identity card + inline edit + menu + logout.
 * GET   {Identity}/customer/auth/me
 * PATCH {Catalog}/api/v1/customer/profile
 */
import React, { useEffect, useState } from 'react';
import { Alert, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/store/authStore';
import { getMe } from '@/api/auth';
import { patchProfile } from '@/api/catalog';
import { useTranslation } from 'react-i18next';
import { changeLanguage, getActiveLocale, type AppLocale } from '@/i18n';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { Avatar } from '@/components/ui/Avatar';
import { TextInput } from '@/components/ui/TextInput';
import { Button } from '@/components/ui/Button';
import type { PatchProfileRequest } from '@/types/api';

type IoniconName = React.ComponentProps<typeof Ionicons>['name'];

function MenuItem({
  icon,
  label,
  onPress,
  danger,
}: {
  icon: IoniconName;
  label: string;
  onPress: () => void;
  danger?: boolean;
}) {
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={label}
      className="flex-row items-center border-b border-cream-200 py-4 last:border-0 active:opacity-70"
    >
      <View
        className={`mr-3 h-9 w-9 items-center justify-center rounded-xl ${danger ? 'bg-red-50' : 'bg-cream-100'}`}
      >
        <Ionicons name={icon} size={18} color={danger ? '#C0492F' : '#5C6A33'} />
      </View>
      <Text
        className={`flex-1 text-base font-semibold ${danger ? 'text-danger' : 'text-ink'}`}
      >
        {label}
      </Text>
      {!danger ? (
        <Ionicons name="chevron-forward" size={18} color="#A8A493" />
      ) : null}
    </Pressable>
  );
}

// ── Inline edit form ──────────────────────────────────────────────────────────

interface EditFormProps {
  initialFirstName: string;
  initialLastName: string;
  initialEmail: string;
  onClose: () => void;
}

function EditProfileForm({
  initialFirstName,
  initialLastName,
  initialEmail,
  onClose,
}: EditFormProps) {
  const qc = useQueryClient();
  const { t } = useTranslation();
  const setCustomer = useAuthStore((s) => s.setCustomer);

  const [firstName, setFirstName] = useState(initialFirstName);
  const [lastName, setLastName] = useState(initialLastName);
  const [email, setEmail] = useState(initialEmail);

  const mutation = useMutation({
    mutationFn: (req: PatchProfileRequest) => patchProfile(req),
    onSuccess: (updated) => {
      void qc.invalidateQueries({ queryKey: ['customer', 'me'] });
      void qc.invalidateQueries({ queryKey: ['customer', 'profile'] });
      // Mirror the update into the auth store so the identity card refreshes
      const current = useAuthStore.getState().customer;
      if (current) {
        setCustomer({
          ...current,
          firstName: updated?.firstName,
          lastName: updated?.lastName,
          displayName: updated?.displayName,
        });
      }
      onClose();
      Alert.alert(t('profile.savedSuccess'), t('profile.profileUpdated'));
    },
    onError: (err) =>
      Alert.alert(
        'Error',
        err instanceof Error ? err.message : t('profile.saveError'),
      ),
  });

  const hasChanges =
    firstName !== initialFirstName ||
    lastName !== initialLastName ||
    email !== initialEmail;

  const handleSave = () => {
    mutation.mutate({
      firstName: firstName.trim() || undefined,
      lastName: lastName.trim() || undefined,
      email: email.trim() || undefined,
    });
  };

  return (
    <View className="mx-6 mt-2 rounded-3xl bg-white px-4 py-5">
      <View className="flex-row items-center justify-between pb-3">
        <Text className="text-base font-extrabold text-ink">{t('profile.editProfile')}</Text>
        <Pressable
          onPress={onClose}
          accessibilityRole="button"
          className="h-8 w-8 items-center justify-center rounded-full bg-cream-100"
          accessibilityLabel={t('a11y.closeEditForm')}
        >
          <Ionicons name="close" size={16} color="#3C3F35" />
        </Pressable>
      </View>
      <View className="gap-4">
        <View className="flex-row gap-3">
          <View className="flex-1">
            <TextInput
              label={t('profile.firstName')}
              value={firstName}
              onChangeText={setFirstName}
              placeholder={t('profile.firstPlaceholder')}
              autoCapitalize="words"
              returnKeyType="next"
            />
          </View>
          <View className="flex-1">
            <TextInput
              label={t('profile.lastName')}
              value={lastName}
              onChangeText={setLastName}
              placeholder={t('profile.lastPlaceholder')}
              autoCapitalize="words"
              returnKeyType="next"
            />
          </View>
        </View>
        <TextInput
          label={t('profile.email')}
          value={email}
          onChangeText={setEmail}
          placeholder={t('profile.emailPlaceholder')}
          keyboardType="email-address"
          autoCapitalize="none"
          returnKeyType="done"
        />
        <Text className="text-xs text-ink-faint">
          {t('profile.phoneCannotChange')}
        </Text>
        <Button
          title={mutation.isPending ? t('profile.saving') : t('profile.saveChanges')}
          fullWidth
          loading={mutation.isPending}
          disabled={!hasChanges || mutation.isPending}
          onPress={handleSave}
        />
      </View>
    </View>
  );
}

// ── Screen ────────────────────────────────────────────────────────────────────

export default function ProfileScreen() {
  const customer = useAuthStore((s) => s.customer);
  const logout = useAuthStore((s) => s.logout);
  const setCustomer = useAuthStore((s) => s.setCustomer);
  const router = useRouter();
  const { t, i18n } = useTranslation();
  const [editMode, setEditMode] = useState(false);
  const [activeLocale, setActiveLocale] = useState<AppLocale>(getActiveLocale());

  const { data: me, isLoading } = useQuery({
    queryKey: ['customer', 'me'],
    queryFn: async () => {
      const data = await getMe();
      setCustomer(data);
      return data;
    },
    staleTime: 5 * 60_000,
  });

  const handleLogout = () => {
    Alert.alert(t('profile.logOutConfirm'), t('profile.logOutMessage'), [
      { text: t('common.cancel'), style: 'cancel' },
      {
        text: t('profile.logOut'),
        style: 'destructive',
        onPress: async () => {
          await logout();
          router.replace('/(auth)/onboarding');
        },
      },
    ]);
  };

  const handleLanguageChange = async (locale: AppLocale) => {
    await changeLanguage(locale);
    setActiveLocale(locale);
  };

  if (isLoading) return <ScreenLoader />;

  const profile = me ?? customer;
  const name = profile?.displayName ?? profile?.firstName ?? 'Customer';
  const firstName = profile?.firstName ?? '';
  const lastName = profile?.lastName ?? '';
  const email = '';

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={{ paddingBottom: 120 }}
      >
        <View className="px-6 pb-2 pt-3">
          <Text className="text-2xl font-extrabold text-ink">{t('profile.title')}</Text>
        </View>

        {/* Identity card */}
        <View
          className="mx-6 mt-2 rounded-3xl bg-white p-5"
          style={{
            shadowColor: '#2E351C',
            shadowOpacity: 0.05,
            shadowRadius: 10,
            shadowOffset: { width: 0, height: 3 },
            elevation: 2,
          }}
        >
          <View className="flex-row items-center">
            <Avatar name={name} size={56} textClassName="text-xl" />
            <View className="ml-4 flex-1">
              <Text className="text-lg font-extrabold text-ink">{name}</Text>
              <Text className="text-sm text-ink-muted">
                {profile?.phone ?? ''}
              </Text>
            </View>
            <Pressable
              onPress={() => setEditMode((v) => !v)}
              className="h-9 w-9 items-center justify-center rounded-full bg-cream-100"
              accessibilityLabel={t('a11y.editProfile')}
              accessibilityRole="button"
            >
              <Ionicons
                name={editMode ? 'close' : 'pencil-outline'}
                size={16}
                color="#5C6A33"
              />
            </Pressable>
          </View>
        </View>

        {/* Inline edit form */}
        {editMode ? (
          <EditProfileForm
            initialFirstName={firstName}
            initialLastName={lastName}
            initialEmail={email}
            onClose={() => setEditMode(false)}
          />
        ) : null}

        {/* Menu */}
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
          <MenuItem
            icon="pricetags-outline"
            label={t('profile.priceList')}
            onPress={() => router.push('/(app)/price-list')}
          />
          <MenuItem
            icon="gift-outline"
            label={t('profile.offersAndCoupons')}
            onPress={() => router.push('/(app)/offers')}
          />
          <MenuItem
            icon="wallet-outline"
            label={t('profile.wallet')}
            onPress={() => router.push('/(app)/(tabs)/wallet')}
          />
          <MenuItem
            icon="location-outline"
            label={t('profile.savedAddresses')}
            onPress={() => router.push('/(app)/addresses' as never)}
          />
          <MenuItem
            icon="help-circle-outline"
            label={t('profile.helpAndSupport')}
            onPress={() => router.push('/(app)/help' as never)}
          />
        </View>

        {/* Language switcher */}
        <View
          className="mx-6 mt-5 rounded-3xl bg-white px-4 py-4"
          style={{ shadowColor: '#2E351C', shadowOpacity: 0.04, shadowRadius: 8, shadowOffset: { width: 0, height: 2 }, elevation: 1 }}
        >
          <View className="flex-row items-center justify-between">
            <View className="flex-row items-center gap-3">
              <View className="h-9 w-9 items-center justify-center rounded-xl bg-cream-100">
                <Ionicons name="language" size={18} color="#5C6A33" />
              </View>
              <Text className="text-base font-semibold text-ink">{t('language.title')}</Text>
            </View>
            <View className="flex-row gap-2">
              <Pressable
                onPress={() => void handleLanguageChange('en')}
                accessibilityRole="radio"
                accessibilityState={{ selected: activeLocale === 'en' }}
                accessibilityLabel={t('a11y.langEn')}
                className={`rounded-xl px-3 py-1.5 border ${activeLocale === 'en' ? 'border-olive-700 bg-olive-700' : 'border-cream-300 bg-white'}`}
              >
                <Text className={`text-sm font-bold ${activeLocale === 'en' ? 'text-white' : 'text-ink'}`}>EN</Text>
              </Pressable>
              <Pressable
                onPress={() => void handleLanguageChange('hi')}
                accessibilityRole="radio"
                accessibilityState={{ selected: activeLocale === 'hi' }}
                accessibilityLabel={t('a11y.langHi')}
                className={`rounded-xl px-3 py-1.5 border ${activeLocale === 'hi' ? 'border-olive-700 bg-olive-700' : 'border-cream-300 bg-white'}`}
              >
                <Text className={`text-sm font-bold ${activeLocale === 'hi' ? 'text-white' : 'text-ink'}`}>हिन्दी</Text>
              </Pressable>
            </View>
          </View>
        </View>

        {/* Logout */}
        <View className="mx-6 mt-5 rounded-3xl bg-white px-4">
          <MenuItem
            icon="log-out-outline"
            label={t('profile.logOut')}
            danger
            onPress={handleLogout}
          />
        </View>

        <Text className="mt-6 text-center text-xs text-ink-faint">
          {t('common.version')}
        </Text>
      </ScrollView>
    </SafeAreaView>
  );
}
