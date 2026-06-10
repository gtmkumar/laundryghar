/**
 * Earnings screen — rolling 7-day / 30-day earnings breakdown.
 *
 * Header: period total + average payout per task.
 * Body: per-day list (date, task count, payout).
 * Period toggle: 7 days / 30 days.
 *
 * Entry: from the home-screen stats pill ("View today's tasks" area) and the
 * profile screen as a quick-link action.
 */
import React, { useState } from 'react';
import { FlatList, Pressable, RefreshControl, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import { useMyPayouts } from '@/hooks/useEarnings';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { useTranslation } from 'react-i18next';
import type { RiderPayoutDayDto } from '@/types/api';

function formatDate(iso: string): string {
  const d = new Date(iso + 'T00:00:00');
  return d.toLocaleDateString('en-IN', { weekday: 'short', day: 'numeric', month: 'short' });
}

function DayRow({ item }: { item: RiderPayoutDayDto }) {
  const { t } = useTranslation();
  return (
    <View
      className="mb-3 flex-row items-center rounded-2xl bg-white px-4 py-3.5"
      style={{ shadowColor: '#000', shadowOpacity: 0.04, shadowRadius: 6, shadowOffset: { width: 0, height: 2 }, elevation: 1 }}
    >
      <View className="flex-1">
        <Text className="text-sm font-bold text-ink">{formatDate(item.date)}</Text>
        <Text className="mt-0.5 text-xs text-ink-muted">
          {t('earnings.taskCount', { count: item.taskCount })}
        </Text>
      </View>
      <Text className="text-base font-extrabold text-olive-700">
        {item.totalPayout > 0 ? `+₹${item.totalPayout.toFixed(2)}` : '₹0'}
      </Text>
    </View>
  );
}

export default function EarningsScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const [days, setDays] = useState<7 | 30>(30);
  const { data, isLoading, isError, refetch, isRefetching } = useMyPayouts(days);

  if (isLoading) return <ScreenLoader />;
  if (isError)   return <ErrorState message="Could not load earnings. Pull to refresh." />;

  const totalPayout = data?.totalPayout ?? 0;
  const avgPerTask  = data?.avgPerTask  ?? 0;
  const breakdown   = data?.breakdown   ?? [];

  return (
    <View className="flex-1 bg-cream">
      <StatusBar style="light" />

      {/* Olive header */}
      <SafeAreaView edges={['top']} style={{ backgroundColor: '#4A552A' }}>
        <View className="px-5 pb-6 pt-2">
          <View className="flex-row items-center">
            <Pressable
              onPress={() => router.back()}
              hitSlop={8}
              className="mr-2 h-9 w-9 items-center justify-center active:opacity-60"
              accessibilityLabel={t('a11y.back')}
              accessibilityRole="button"
            >
              <Ionicons name="chevron-back" size={24} color="#FFFFFF" />
            </Pressable>
            <Text className="flex-1 text-base font-extrabold text-white">{t('earnings.title')}</Text>
          </View>

          {/* Summary card */}
          <View className="mt-5 flex-row">
            <View className="flex-1">
              <Text className="text-xs uppercase tracking-widest text-olive-200">
                {days === 7 ? t('earnings.last7Days') : t('earnings.last30Days')}
              </Text>
              <Text className="mt-1 text-3xl font-extrabold text-white">
                ₹{totalPayout.toFixed(2)}
              </Text>
            </View>
            <View className="items-end justify-center">
              <Text className="text-xs text-olive-200">{t('earnings.avgPerTask')}</Text>
              <Text className="mt-0.5 text-xl font-extrabold text-gold-300">
                ₹{avgPerTask.toFixed(2)}
              </Text>
            </View>
          </View>

          {/* Period toggle */}
          <View className="mt-4 flex-row gap-2 self-start">
            {([7, 30] as const).map((d) => (
              <Pressable
                key={d}
                onPress={() => setDays(d)}
                accessibilityRole="button"
                accessibilityLabel={`Show last ${d} days`}
                accessibilityState={{ selected: days === d }}
                className={`rounded-full px-4 py-1.5 ${days === d ? 'bg-white' : 'bg-white/20'}`}
              >
                <Text className={`text-sm font-bold ${days === d ? 'text-olive-800' : 'text-white'}`}>
                  {d}d
                </Text>
              </Pressable>
            ))}
          </View>
        </View>
      </SafeAreaView>

      <FlatList
        data={breakdown}
        keyExtractor={(item) => item.date}
        contentContainerStyle={{ paddingHorizontal: 20, paddingTop: 16, paddingBottom: 40 }}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={isRefetching}
            onRefresh={() => void refetch()}
            tintColor="#4A552A"
            colors={['#4A552A']}
          />
        }
        renderItem={({ item }) => <DayRow item={item} />}
        ListEmptyComponent={
          <View className="items-center px-8 pt-16">
            <View className="h-16 w-16 items-center justify-center rounded-full bg-olive-100">
              <Ionicons name="cash-outline" size={28} color="#4A552A" />
            </View>
            <Text className="mt-4 text-base font-bold text-ink">{t('earnings.noEarnings')}</Text>
            <Text className="mt-1 text-center text-sm text-ink-muted">
              {t('earnings.noEarningsMessage')}
            </Text>
          </View>
        }
      />
    </View>
  );
}
