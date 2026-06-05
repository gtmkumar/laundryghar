/**
 * Profile tab
 *
 * Wired to: GET {Logistics}/api/v1/rider/me
 * Shows rider profile info + logout button.
 */
import React from 'react';
import {
  Alert,
  ScrollView,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useMyRiderProfile } from '@/hooks/useRider';
import { useAuthStore } from '@/store/authStore';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { Button } from '@/components/ui/Button';

function ProfileRow({
  label,
  value,
}: {
  label: string;
  value?: string | number | boolean | null;
}) {
  let display: string;
  if (value == null) {
    display = '—';
  } else if (typeof value === 'boolean') {
    display = value ? 'Yes' : 'No';
  } else {
    display = String(value);
  }
  return (
    <View className="flex-row items-center justify-between border-b border-gray-100 py-4">
      <Text className="text-sm font-medium text-gray-500">{label}</Text>
      <Text className="text-sm text-gray-900 text-right flex-1 ml-4" numberOfLines={1}>
        {display}
      </Text>
    </View>
  );
}

export default function ProfileScreen() {
  const { logout, rider, setRider } = useAuthStore();
  const router = useRouter();

  const { data: me, isLoading } = useMyRiderProfile();

  // Sync fetched profile into auth store so other screens can use it
  React.useEffect(() => {
    if (me) setRider(me);
  }, [me, setRider]);

  const handleLogout = () => {
    Alert.alert('Log Out', 'Are you sure you want to log out?', [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Log Out',
        style: 'destructive',
        onPress: async () => {
          await logout();
          router.replace('/(auth)/login');
        },
      },
    ]);
  };

  if (isLoading) return <ScreenLoader />;

  const profile = me ?? rider;

  const initials = (profile?.riderCode ?? 'R').charAt(0).toUpperCase();

  return (
    <SafeAreaView className="flex-1 bg-surface-muted">
      <ScrollView showsVerticalScrollIndicator={false}>
        {/* Avatar + code */}
        <View className="items-center bg-brand-700 px-6 py-10">
          <View className="mb-4 h-20 w-20 items-center justify-center rounded-full bg-brand-500">
            <Text className="text-3xl font-bold text-white">{initials}</Text>
          </View>
          <Text className="text-xl font-bold text-white">
            {profile?.riderCode ?? 'Rider'}
          </Text>
          <Text className="mt-1 text-sm text-brand-200">
            {profile?.employmentType ?? ''}
          </Text>
        </View>

        {/* Profile details */}
        <View
          className="mx-6 mt-6 rounded-2xl bg-white px-4 shadow-sm"
          style={{ elevation: 2 }}
        >
          <ProfileRow label="Rider Code"    value={profile?.riderCode} />
          <ProfileRow label="Vehicle Type"  value={profile?.vehicleType} />
          <ProfileRow label="Vehicle No."   value={profile?.vehicleNumber} />
          <ProfileRow label="KYC Status"    value={profile?.kycStatus} />
          <ProfileRow label="Status"        value={profile?.status} />
          <ProfileRow label="Is Online"     value={profile?.isOnline} />
          <ProfileRow label="Is On Duty"    value={profile?.isOnDuty} />
          <ProfileRow label="Rating"        value={
            profile?.ratingAverage != null
              ? `${profile.ratingAverage.toFixed(1)} (${profile.ratingCount} reviews)`
              : null
          } />
          <ProfileRow label="Lifetime Deliveries" value={profile?.lifetimeDeliveries} />
          <ProfileRow label="Completion Rate" value={
            profile?.completionRate != null
              ? `${(profile.completionRate * 100).toFixed(0)}%`
              : null
          } />
          <ProfileRow label="Daily Pickup Cap."    value={profile?.dailyPickupCapacity} />
          <ProfileRow label="Daily Delivery Cap."  value={profile?.dailyDeliveryCapacity} />
        </View>

        {/* Actions */}
        <View className="mx-6 mt-6 mb-8 gap-3">
          <Button
            title="Log Out"
            variant="danger"
            fullWidth
            onPress={handleLogout}
            accessibilityLabel="Log out of Rider Portal"
          />
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}
