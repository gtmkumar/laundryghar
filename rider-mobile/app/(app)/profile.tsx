/**
 * Profile — rider identity, performance and account actions.
 * Wired to GET /api/v1/rider/me.
 */
import React from 'react';
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
  const { logout, rider, setRider } = useAuthStore();
  const { setOnDuty } = useDutyStore();
  const { data: me, isLoading } = useMyRiderProfile();

  React.useEffect(() => { if (me) setRider(me); }, [me, setRider]);

  const profile = me ?? rider;

  function handleLogout() {
    Alert.alert('Log out', 'Are you sure you want to log out?', [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Log out',
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
          <Pressable onPress={() => router.back()} hitSlop={8} className="h-9 w-9 items-center justify-center active:opacity-60">
            <Ionicons name="chevron-back" size={24} color="#1E2119" />
          </Pressable>
          <Text className="flex-1 text-center text-base font-extrabold text-ink">Profile</Text>
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
                  KYC {profile.kycStatus}
                </Text>
              </View>
            ) : null}
          </View>

          {/* Stats */}
          <View className="mt-6 flex-row gap-3">
            <StatCard icon="star" value={rating} label="Rating" />
            <StatCard icon="cube" value={String(profile?.lifetimeDeliveries ?? 0)} label="Deliveries" />
            <StatCard icon="checkmark-done" value={completion} label="Completion" />
          </View>

          {/* Details */}
          <View className="mt-6 rounded-3xl bg-white px-4" style={{ elevation: 1 }}>
            <Row label="Phone" value={profile?.phone} />
            <Row label="Email" value={profile?.email} />
            <Row label="Franchise" value={profile?.franchiseName} />
            <Row label="Store" value={profile?.primaryStoreName} />
            <Row label="Vehicle" value={[profile?.vehicleType, profile?.vehicleNumber].filter(Boolean).join(' · ') || null} />
            <Row label="Employment" value={profile?.employmentType} />
            <Row label="Status" value={profile?.status} />
          </View>

          <View className="mt-6">
            <Button title="Log out" variant="danger" fullWidth iconLeft="log-out-outline" onPress={handleLogout} />
          </View>

          <Text className="mt-5 text-center text-xs text-ink-faint">Laundry Ghar Rider · v2.0</Text>
        </ScrollView>
      </SafeAreaView>
    </View>
  );
}
