/**
 * Notifications / activity screen.
 *
 * The engagement backend exposes notifications for the customer app type; the
 * rider app does not yet have a notification-feed endpoint. Until one is wired
 * we render an informative empty-state rather than a fake "All caught up" Alert.
 *
 * When a real feed is available, replace the hardcoded empty-state body with a
 * FlatList over GET /api/v1/rider/notifications (or the engagement equivalent).
 * The screen is registered in _layout.tsx as a card-style push.
 */
import React from 'react';
import { Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import { useTranslation } from 'react-i18next';

/** Static list of the types of in-app alerts a rider will receive once the feed ships. */
const UPCOMING_TYPES: { icon: 'clipboard-outline' | 'cash-outline' | 'alert-circle-outline' | 'star-outline'; label: string }[] = [
  { icon: 'clipboard-outline',    label: 'New task assigned' },
  { icon: 'cash-outline',         label: 'COD settlement confirmed' },
  { icon: 'alert-circle-outline', label: 'Task failed / escalated' },
  { icon: 'star-outline',         label: 'Customer rating received' },
];

export default function NotificationsScreen() {
  const router = useRouter();
  const { t } = useTranslation();

  return (
    <View className="flex-1 bg-cream">
      <StatusBar style="dark" />
      <SafeAreaView className="flex-1" edges={['top', 'left', 'right']}>
        {/* Header */}
        <View className="flex-row items-center px-4 pb-2 pt-1">
          <Pressable
            onPress={() => router.back()}
            hitSlop={8}
            accessibilityRole="button"
            accessibilityLabel={t('a11y.back')}
            className="h-9 w-9 items-center justify-center active:opacity-60"
          >
            <Ionicons name="chevron-back" size={24} color="#1E2119" />
          </Pressable>
          <Text className="flex-1 text-center text-base font-extrabold text-ink">
            {t('home.notifications')}
          </Text>
          <View className="h-9 w-9" />
        </View>

        <ScrollView
          contentContainerStyle={{ paddingHorizontal: 20, paddingTop: 8, paddingBottom: 32 }}
          showsVerticalScrollIndicator={false}
        >
          {/* Empty state */}
          <View className="mt-10 items-center">
            <View
              className="h-20 w-20 items-center justify-center rounded-full bg-olive-100"
              style={{ shadowColor: '#000', shadowOpacity: 0.06, shadowRadius: 10, shadowOffset: { width: 0, height: 4 }, elevation: 2 }}
            >
              <Ionicons name="notifications-outline" size={36} color="#4A552A" />
            </View>
            <Text className="mt-5 text-xl font-extrabold text-ink">No notifications yet</Text>
            <Text className="mt-2 text-center text-sm leading-6 text-ink-muted">
              Activity alerts — new tasks, settlement confirmations, and customer ratings — will
              appear here as you complete rides.
            </Text>
          </View>

          {/* Preview of upcoming notification types */}
          <View
            className="mt-8 rounded-3xl bg-white p-5"
            style={{ shadowColor: '#000', shadowOpacity: 0.04, shadowRadius: 8, shadowOffset: { width: 0, height: 3 }, elevation: 1 }}
          >
            <Text className="mb-4 text-xs font-bold uppercase tracking-widest text-ink-muted">
              You will be notified about
            </Text>
            {UPCOMING_TYPES.map(({ icon, label }, i) => (
              <View
                key={label}
                className={`flex-row items-center py-3 ${i < UPCOMING_TYPES.length - 1 ? 'border-b border-cream-200' : ''}`}
              >
                <View className="h-9 w-9 items-center justify-center rounded-full bg-olive-50">
                  <Ionicons name={icon} size={18} color="#4A552A" />
                </View>
                <Text className="ml-3 text-sm font-medium text-ink">{label}</Text>
              </View>
            ))}
          </View>
        </ScrollView>
      </SafeAreaView>
    </View>
  );
}
