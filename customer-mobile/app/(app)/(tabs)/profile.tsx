/**
 * Profile tab — identity card + menu + logout.
 * GET {Identity}/customer/auth/me
 */
import React from 'react';
import { Alert, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useQuery } from '@tanstack/react-query';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/store/authStore';
import { getMe } from '@/api/auth';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { Avatar } from '@/components/ui/Avatar';

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
      <View className={`mr-3 h-9 w-9 items-center justify-center rounded-xl ${danger ? 'bg-red-50' : 'bg-cream-100'}`}>
        <Ionicons name={icon} size={18} color={danger ? '#C0492F' : '#5C6A33'} />
      </View>
      <Text className={`flex-1 text-base font-semibold ${danger ? 'text-danger' : 'text-ink'}`}>{label}</Text>
      {!danger ? <Ionicons name="chevron-forward" size={18} color="#A8A493" /> : null}
    </Pressable>
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
    Alert.alert('Log out', 'Are you sure you want to log out?', [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Log out',
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
  const name = profile?.displayName ?? profile?.firstName ?? 'Customer';

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 120 }}>
        <View className="px-6 pb-2 pt-3">
          <Text className="text-2xl font-extrabold text-ink">Profile</Text>
        </View>

        {/* Identity card */}
        <View className="mx-6 mt-2 flex-row items-center rounded-3xl bg-white p-5" style={{
          shadowColor: '#2E351C', shadowOpacity: 0.05, shadowRadius: 10, shadowOffset: { width: 0, height: 3 }, elevation: 2,
        }}>
          <Avatar name={name} size={56} textClassName="text-xl" />
          <View className="ml-4 flex-1">
            <Text className="text-lg font-extrabold text-ink">{name}</Text>
            <Text className="text-sm text-ink-muted">{profile?.phone ?? ''}</Text>
          </View>
        </View>

        {/* Menu */}
        <View className="mx-6 mt-5 rounded-3xl bg-white px-4" style={{
          shadowColor: '#2E351C', shadowOpacity: 0.04, shadowRadius: 8, shadowOffset: { width: 0, height: 2 }, elevation: 1,
        }}>
          <MenuItem icon="pricetags-outline" label="Price list" onPress={() => router.push('/(app)/price-list')} />
          <MenuItem icon="gift-outline" label="Offers & coupons" onPress={() => router.push('/(app)/offers')} />
          <MenuItem icon="wallet-outline" label="Wallet" onPress={() => router.push('/(app)/(tabs)/wallet')} />
          <MenuItem icon="location-outline" label="Saved addresses" onPress={() => Alert.alert('Addresses', 'Address management is coming soon.')} />
          <MenuItem icon="help-circle-outline" label="Help & support" onPress={() => Alert.alert('Support', 'Reach us at care@laundryghar.in')} />
        </View>

        {/* Logout */}
        <View className="mx-6 mt-5 rounded-3xl bg-white px-4">
          <MenuItem icon="log-out-outline" label="Log out" danger onPress={handleLogout} />
        </View>

        <Text className="mt-6 text-center text-xs text-ink-faint">Laundry Ghar · v2.0</Text>
      </ScrollView>
    </SafeAreaView>
  );
}
