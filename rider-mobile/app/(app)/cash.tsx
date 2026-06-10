/**
 * Cash screen — COD cash in hand + settlement history.
 *
 * Shows:
 *   - Cash-in-hand card (amber when > 0, showing outstanding COD balance)
 *   - Last settlement timestamp
 *   - Up to 10 recent settlements
 *   - Note: 'Settle with your store admin'
 *
 * Entry: from home screen (quick-action row) and profile.
 */
import React from 'react';
import { FlatList, RefreshControl, Text, View, Pressable } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import { useMyCashSummary } from '@/hooks/useEarnings';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { useTranslation } from 'react-i18next';
import type { RiderCashSettlementItemDto } from '@/types/api';

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString('en-IN', {
    day: 'numeric', month: 'short',
    hour: '2-digit', minute: '2-digit',
  });
}

function SettlementRow({ item }: { item: RiderCashSettlementItemDto }) {
  const { t } = useTranslation();
  return (
    <View className="flex-row items-center border-b border-cream-200 py-3.5">
      <View className="h-8 w-8 items-center justify-center rounded-full bg-olive-100">
        <Ionicons name="checkmark" size={16} color="#4A552A" />
      </View>
      <View className="ml-3 flex-1">
        <Text className="text-sm font-semibold text-ink">{t('cash.settlement')}</Text>
        <Text className="text-xs text-ink-muted">{formatDateTime(item.settledAt)}</Text>
      </View>
      <Text className="text-sm font-bold text-olive-700">₹{item.amount.toFixed(2)}</Text>
    </View>
  );
}

export default function CashScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const { data, isLoading, isError, refetch, isRefetching } = useMyCashSummary();

  if (isLoading) return <ScreenLoader />;
  if (isError)   return <ErrorState message="Could not load cash summary. Pull to refresh." />;

  const cashInHand   = data?.cashInHand ?? 0;
  const lastSettled  = data?.lastSettlementAt ?? null;
  const settlements  = data?.recentSettlements ?? [];
  const hasCash      = cashInHand > 0;

  return (
    <View className="flex-1 bg-cream">
      <StatusBar style="dark" />
      <SafeAreaView className="flex-1" edges={['top', 'left', 'right']}>
        {/* Header */}
        <View className="flex-row items-center px-4 pb-2 pt-1">
          <Pressable
            onPress={() => router.back()}
            hitSlop={8}
            className="h-9 w-9 items-center justify-center active:opacity-60"
            accessibilityLabel={t('a11y.back')}
            accessibilityRole="button"
          >
            <Ionicons name="chevron-back" size={24} color="#1E2119" />
          </Pressable>
          <Text className="flex-1 text-center text-base font-extrabold text-ink">{t('cash.title')}</Text>
          <View className="h-9 w-9" />
        </View>

        <FlatList
          data={settlements}
          keyExtractor={(_, i) => String(i)}
          contentContainerStyle={{ paddingHorizontal: 20, paddingBottom: 40 }}
          showsVerticalScrollIndicator={false}
          refreshControl={
            <RefreshControl
              refreshing={isRefetching}
              onRefresh={() => void refetch()}
              tintColor="#4A552A"
              colors={['#4A552A']}
            />
          }
          ListHeaderComponent={
            <View>
              {/* Cash-in-hand card */}
              <View
                className={`mt-2 rounded-3xl p-6 ${hasCash ? 'bg-gold-100 border border-gold-300' : 'bg-white'}`}
                style={{ shadowColor: '#000', shadowOpacity: 0.06, shadowRadius: 10, shadowOffset: { width: 0, height: 4 }, elevation: 2 }}
                accessibilityRole="summary"
                accessibilityLabel={`Cash in hand: ${cashInHand} rupees`}
              >
                <View className="flex-row items-center gap-2">
                  <Ionicons
                    name="cash-outline"
                    size={22}
                    color={hasCash ? '#8A641D' : '#4A552A'}
                  />
                  <Text className={`text-sm font-bold ${hasCash ? 'text-gold-800' : 'text-ink-muted'}`}>
                    {t('cash.cashInHand')}
                  </Text>
                </View>
                <Text className={`mt-2 text-4xl font-extrabold ${hasCash ? 'text-gold-900' : 'text-ink'}`}>
                  ₹{cashInHand.toFixed(2)}
                </Text>
                {hasCash ? (
                  <View className="mt-3 flex-row items-center gap-1.5 rounded-2xl bg-gold-200 px-3 py-2">
                    <Ionicons name="information-circle-outline" size={15} color="#8A641D" />
                    <Text className="flex-1 text-xs font-semibold text-gold-800">
                      {t('cash.settleWithAdmin')}
                    </Text>
                  </View>
                ) : (
                  <Text className="mt-2 text-xs text-ink-muted">{t('cash.noCash')}</Text>
                )}
              </View>

              {/* Last settlement */}
              {lastSettled ? (
                <View className="mt-4 flex-row items-center rounded-2xl bg-white px-4 py-3" style={{ elevation: 1 }}>
                  <Ionicons name="time-outline" size={16} color="#4A552A" />
                  <Text className="ml-2 text-sm text-ink-muted">
                    {t('cash.lastSettled')}{' '}
                    <Text className="font-semibold text-ink">{formatDateTime(lastSettled)}</Text>
                  </Text>
                </View>
              ) : null}

              {/* Section header */}
              {settlements.length > 0 ? (
                <Text className="mt-6 mb-1 text-xs font-bold uppercase tracking-widest text-ink-faint">
                  {t('cash.recentSettlements')}
                </Text>
              ) : null}
            </View>
          }
          renderItem={({ item }) => <SettlementRow item={item} />}
          ListEmptyComponent={
            settlements.length === 0 ? (
              <View className="mt-6 items-center rounded-3xl bg-white px-6 py-10" style={{ elevation: 1 }}>
                <Ionicons name="receipt-outline" size={32} color="#A8A493" />
                <Text className="mt-3 text-sm font-bold text-ink-muted">{t('cash.noSettlements')}</Text>
                <Text className="mt-1 text-center text-xs text-ink-faint">
                  {t('cash.noSettlementsMessage')}
                </Text>
              </View>
            ) : null
          }
        />
      </SafeAreaView>
    </View>
  );
}
