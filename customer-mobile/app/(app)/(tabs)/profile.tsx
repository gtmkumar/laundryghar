/**
 * Profile tab — shows customer info and logout.
 * GET /api/v1/customer/auth/me on mount.
 */
import React from 'react';
import {
  Alert,
  Pressable,
  ScrollView,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useQuery } from '@tanstack/react-query';
import { useAuthStore } from '@/store/authStore';
import { getMe } from '@/api/auth';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { Button } from '@/components/ui/Button';

function ProfileRow({ label, value }: { label: string; value?: string }) {
  return (
    <View className="flex-row items-center justify-between border-b border-gray-100 py-4">
      <Text className="text-sm font-medium text-gray-500">{label}</Text>
      <Text className="text-sm text-gray-900">{value ?? '—'}</Text>
    </View>
  );
}

export default function ProfileScreen() {
  const { logout, customer, setCustomer } = useAuthStore();
  const router = useRouter();

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
    Alert.alert('Log Out', 'Are you sure you want to log out?', [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Log Out',
        style: 'destructive',
        onPress: async () => {
          await logout();
          router.replace('/(auth)/onboarding');
        },
      },
    ]);
  };

  if (isLoading) return <ScreenLoader />;

  const profile = me ?? customer;

  return (
    <SafeAreaView className="flex-1 bg-surface-muted">
      <ScrollView showsVerticalScrollIndicator={false}>
        {/* Avatar + name */}
        <View className="items-center bg-brand-700 px-6 py-10">
          <View className="mb-4 h-20 w-20 items-center justify-center rounded-full bg-brand-500">
            <Text className="text-3xl font-bold text-white">
              {(profile?.displayName ?? profile?.firstName ?? 'U').charAt(0).toUpperCase()}
            </Text>
          </View>
          <Text className="text-xl font-bold text-white">
            {profile?.displayName ?? profile?.firstName ?? 'Customer'}
          </Text>
          <Text className="mt-1 text-sm text-brand-200">
            {profile?.phone ?? ''}
          </Text>
        </View>

        {/* Details card */}
        <View className="mx-6 mt-6 rounded-2xl bg-white px-4 shadow-sm" style={{ elevation: 2 }}>
          <ProfileRow label="Phone"  value={profile?.phone} />
          <ProfileRow label="Name"   value={profile?.displayName ?? profile?.firstName} />
          <ProfileRow label="Status" value={profile?.status} />
        </View>

        {/* Actions */}
        <View className="mx-6 mt-6 gap-3">
          <Button
            title="Log Out"
            variant="danger"
            fullWidth
            onPress={handleLogout}
          />
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}
