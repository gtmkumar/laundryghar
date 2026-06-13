/**
 * Notifications — honest, backend-free notification centre.
 *
 * The home-screen bell used to dead-end into My Orders; this screen shows:
 *   1. Push-permission status (expo-notifications) with a path to Settings.
 *   2. Recent order / pickup status updates DERIVED from the existing
 *      orders + pickup-requests queries (no new backend API).
 *
 * Tapping an update opens the matching tracking screen.
 */
import React, { useCallback, useMemo, useState } from 'react';
import { FlatList, Linking, Platform, Pressable, RefreshControl, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import * as Notifications from 'expo-notifications';
import { useTranslation } from 'react-i18next';
import { useMyOrders, useMyPickupRequests } from '@/hooks/useOrders';
import { formatDateTime } from '@/lib/format';
import type { OrderDto, PickupRequestDto } from '@/types/api';

interface UpdateEntry {
  key: string;
  kind: 'order' | 'pickup';
  id: string;
  number: string;
  /** i18n key path resolved at render time. */
  statusLabel: string;
  /** Best-known timestamp of the latest status change. */
  time?: string;
  tone: 'active' | 'done' | 'danger';
}

function orderToEntry(o: OrderDto): UpdateEntry {
  const time = o.deliveredAt ?? o.readyAt ?? o.pickedUpAt ?? o.cancelledAt ?? o.placedAt;
  const tone =
    o.status === 'cancelled' || o.status === 'returned' || o.status === 'disputed'
      ? 'danger'
      : o.status === 'delivered' || o.status === 'closed'
        ? 'done'
        : 'active';
  return {
    key: `order-${o.id}`,
    kind: 'order',
    id: o.id,
    number: o.orderNumber,
    statusLabel: `orders.statusLabels.${o.status}`,
    time,
    tone,
  };
}

function pickupToEntry(p: PickupRequestDto): UpdateEntry {
  const tone =
    p.status === 'cancelled' || p.status === 'no_response'
      ? 'danger'
      : p.status === 'completed' || p.status === 'converted'
        ? 'done'
        : 'active';
  return {
    key: `pickup-${p.id}`,
    kind: 'pickup',
    id: p.id,
    number: p.requestNumber,
    statusLabel: `orders.pickupStatusLabels.${p.status}`,
    time: p.createdAt,
    tone,
  };
}

const TONE_STYLES: Record<UpdateEntry['tone'], { bg: string; color: string; icon: 'sync-outline' | 'checkmark-circle-outline' | 'alert-circle-outline' }> = {
  active: { bg: 'bg-olive-100', color: '#5C6A33', icon: 'sync-outline' },
  done:   { bg: 'bg-gold-100',  color: '#8A641D', icon: 'checkmark-circle-outline' },
  danger: { bg: 'bg-red-50',    color: '#C0492F', icon: 'alert-circle-outline' },
};

function PermissionCard() {
  const { t } = useTranslation();
  const [status, setStatus] = useState<Notifications.PermissionStatus | null>(null);

  // Re-check on focus — the user may return from Settings.
  useFocusEffect(
    useCallback(() => {
      let alive = true;
      Notifications.getPermissionsAsync()
        .then((p) => { if (alive) setStatus(p.status); })
        .catch(() => { if (alive) setStatus(null); });
      return () => { alive = false; };
    }, []),
  );

  const granted = status === 'granted';

  return (
    <View className="mx-5 mb-4 flex-row items-center gap-3 rounded-2xl bg-white p-4">
      <View className={`h-10 w-10 items-center justify-center rounded-xl ${granted ? 'bg-olive-100' : 'bg-gold-100'}`}>
        <Ionicons
          name={granted ? 'notifications' : 'notifications-off-outline'}
          size={20}
          color={granted ? '#5C6A33' : '#8A641D'}
        />
      </View>
      <View className="flex-1">
        <Text className="text-sm font-bold text-ink">
          {granted ? t('notifications.pushOnTitle') : t('notifications.pushOffTitle')}
        </Text>
        <Text className="mt-0.5 text-xs leading-4 text-ink-muted">
          {granted ? t('notifications.pushOnBody') : t('notifications.pushOffBody')}
        </Text>
      </View>
      {!granted && status != null ? (
        <Pressable
          onPress={() => {
            if (Platform.OS === 'ios' || Platform.OS === 'android') {
              void Linking.openSettings().catch(() => undefined);
            }
          }}
          className="rounded-xl bg-olive-700 px-3 py-2"
          accessibilityRole="button"
          accessibilityLabel={t('notifications.openSettings')}
        >
          <Text className="text-xs font-bold text-white">{t('notifications.openSettings')}</Text>
        </Pressable>
      ) : null}
    </View>
  );
}

export default function NotificationsScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const ordersQuery = useMyOrders();
  const pickupsQuery = useMyPickupRequests();

  const entries = useMemo<UpdateEntry[]>(() => {
    const orders = (ordersQuery.data?.list ?? []).map(orderToEntry);
    const pickups = (pickupsQuery.data?.list ?? []).map(pickupToEntry);
    return [...orders, ...pickups]
      .sort((a, b) => (b.time ?? '').localeCompare(a.time ?? ''))
      .slice(0, 30);
  }, [ordersQuery.data, pickupsQuery.data]);

  const isFetching = ordersQuery.isFetching || pickupsQuery.isFetching;
  const refetch = () => {
    void ordersQuery.refetch();
    void pickupsQuery.refetch();
  };

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      {/* Header */}
      <View className="flex-row items-center gap-3 px-5 pb-3 pt-2">
        <Pressable
          onPress={() => router.back()}
          className="h-10 w-10 items-center justify-center rounded-full bg-white"
          accessibilityRole="button"
          accessibilityLabel={t('a11y.back')}
        >
          <Ionicons name="chevron-back" size={22} color="#3C3F35" />
        </Pressable>
        <Text className="text-xl font-extrabold text-ink">{t('notifications.title')}</Text>
      </View>

      <FlatList
        data={entries}
        keyExtractor={(e) => e.key}
        ListHeaderComponent={
          <>
            <PermissionCard />
            {entries.length > 0 ? (
              <Text className="mx-5 mb-2 text-[11px] font-bold uppercase tracking-wider text-ink-faint">
                {t('notifications.recentUpdates')}
              </Text>
            ) : null}
          </>
        }
        renderItem={({ item }) => {
          const tone = TONE_STYLES[item.tone];
          return (
            <Pressable
              onPress={() =>
                router.push(
                  item.kind === 'pickup'
                    ? (`/(app)/orders/tracking/${item.id}?kind=pickup` as never)
                    : (`/(app)/orders/tracking/${item.id}` as never),
                )
              }
              accessibilityRole="button"
              accessibilityLabel={`#${item.number}: ${t(item.statusLabel, { defaultValue: item.statusLabel })}`}
              className="mx-5 mb-3 flex-row items-center gap-3 rounded-2xl bg-white p-4"
            >
              <View className={`h-10 w-10 items-center justify-center rounded-xl ${tone.bg}`}>
                <Ionicons name={tone.icon} size={18} color={tone.color} />
              </View>
              <View className="flex-1">
                <Text className="text-sm font-bold text-ink">
                  {t(item.statusLabel, { defaultValue: item.statusLabel })}
                </Text>
                <Text className="mt-0.5 text-xs text-ink-muted">
                  #{item.number}{item.time ? ` · ${formatDateTime(item.time)}` : ''}
                </Text>
              </View>
              <Ionicons name="chevron-forward" size={16} color="#A8A493" />
            </Pressable>
          );
        }}
        ListEmptyComponent={
          <View className="items-center px-10 pt-14">
            <View className="h-16 w-16 items-center justify-center rounded-full bg-olive-100">
              <Ionicons name="notifications-outline" size={28} color="#4A552A" />
            </View>
            <Text className="mt-4 text-base font-bold text-ink">{t('notifications.emptyTitle')}</Text>
            <Text className="mt-1 text-center text-sm text-ink-muted">
              {t('notifications.emptyMessage')}
            </Text>
          </View>
        }
        contentContainerStyle={{ paddingBottom: 40 }}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl refreshing={isFetching} onRefresh={refetch} tintColor="#4A552A" />
        }
      />
    </SafeAreaView>
  );
}
