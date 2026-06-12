/**
 * Orders tab — order history + pending pickup requests.
 *
 * Shows two sections, newest-first:
 *   1. Pickup requests (status pending/assigned/…) — "Pickup scheduled" cards
 *   2. Real confirmed orders (placed/in-process/delivered/…)
 *
 * Both lists are loaded in parallel; a single combined spinner/error covers both.
 */
import React, { useMemo } from 'react';
import { Pressable, RefreshControl, SectionList, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useMyOrders, useMyPickupRequests } from '@/hooks/useOrders';
import { hapticImpact } from '@/lib/haptics';
import { SkeletonOrderList } from '@/components/ui/Skeleton';
import { ErrorState } from '@/components/ui/ErrorState';
import { EmptyState } from '@/components/ui/EmptyState';
import { Badge } from '@/components/ui/Badge';
import { rupees, formatDate } from '@/lib/format';
import { useTranslation } from 'react-i18next';
import type { OrderDto, OrderStatus, PickupRequestDto, PickupRequestStatus } from '@/types/api';

const RESCHEDULABLE_STATUSES: PickupRequestStatus[] = ['pending', 'no_response', 'rescheduled'];

// ── Status display maps ───────────────────────────────────────────────────────

const ORDER_STATUS_TONE: Record<OrderStatus, 'olive' | 'gold' | 'neutral' | 'success' | 'danger' | 'info'> = {
  placed: 'info', pickup_scheduled: 'info', pickup_assigned: 'info', picked_up: 'gold',
  received: 'gold', sorting: 'gold', in_process: 'gold', qc: 'gold', ready: 'success',
  delivery_scheduled: 'success', out_for_delivery: 'success', delivered: 'neutral', closed: 'neutral',
  cancelled: 'danger', returned: 'danger', rewash: 'gold', disputed: 'danger',
};

// Status labels are resolved via t() inside components to support locale switching.
// This stub satisfies places that still need a static fallback.
const ORDER_STATUS_LABEL: Record<OrderStatus, string> = {
  placed: 'Placed', pickup_scheduled: 'Pickup scheduled', pickup_assigned: 'Rider assigned',
  picked_up: 'Picked up', received: 'Received', sorting: 'Sorting', in_process: 'In process',
  qc: 'Quality check', ready: 'Ready', delivery_scheduled: 'Out soon', out_for_delivery: 'Out for delivery',
  delivered: 'Delivered', closed: 'Closed', cancelled: 'Cancelled', returned: 'Returned', rewash: 'Rewash', disputed: 'Disputed',
};

const PICKUP_STATUS_TONE: Record<PickupRequestStatus, 'olive' | 'gold' | 'neutral' | 'success' | 'danger' | 'info'> = {
  pending: 'info',
  assigned: 'gold',
  rider_dispatched: 'gold',
  arrived: 'gold',
  completed: 'success',
  converted: 'neutral',
  cancelled: 'danger',
  no_response: 'danger',
  rescheduled: 'info',
};

const PICKUP_STATUS_LABEL: Record<PickupRequestStatus, string> = {
  pending: 'Awaiting rider',
  assigned: 'Rider assigned',
  rider_dispatched: 'Rider on the way',
  arrived: 'Rider arrived',
  completed: 'Picked up',
  converted: 'Order created',
  cancelled: 'Cancelled',
  no_response: 'No response',
  rescheduled: 'Rescheduled',
};

// ── Card components ───────────────────────────────────────────────────────────

function PickupCard({ pickup }: { pickup: PickupRequestDto }) {
  const router = useRouter();
  const { t } = useTranslation();
  const tone = PICKUP_STATUS_TONE[pickup.status] ?? 'info';
  const label = t(`orders.pickupStatusLabels.${pickup.status}`, { defaultValue: pickup.status });
  const itemCount = pickup.estimatedItems ?? pickup.cartItems.reduce((n, i) => n + i.quantity, 0);
  const amount = pickup.estimatedAmount;
  const isActive = !['completed', 'converted', 'cancelled', 'no_response'].includes(pickup.status);

  const goToTracking = () =>
    router.push(`/(app)/orders/tracking/${pickup.id}?kind=pickup` as never);

  return (
    <Pressable
      onPress={goToTracking}
      accessibilityRole="button"
      accessibilityLabel={t('a11y.viewPickup', { number: pickup.requestNumber })}
      className="mb-3 rounded-3xl bg-white p-4"
      style={{ shadowColor: '#2E351C', shadowOpacity: 0.05, shadowRadius: 10, shadowOffset: { width: 0, height: 3 }, elevation: 2 }}
    >
      <View className="mb-1 flex-row items-center gap-2">
        <View className="h-6 w-6 items-center justify-center rounded-lg bg-olive-100">
          <Ionicons name="bicycle-outline" size={14} color="#5C6A33" />
        </View>
        <Text className="text-[11px] font-bold uppercase tracking-wider text-olive-700">{t('orders.pickupScheduled')}</Text>
      </View>

      <View className="mb-2 flex-row items-start justify-between">
        <View>
          <Text className="text-base font-extrabold text-ink">#{pickup.requestNumber}</Text>
          <Text className="text-xs text-ink-muted">{formatDate(pickup.createdAt)}</Text>
        </View>
        <Badge label={label} tone={tone} />
      </View>

      <View className="flex-row items-center justify-between">
        <View className="flex-row items-center gap-1.5">
          <Ionicons name="shirt-outline" size={14} color="#7B7A6C" />
          <Text className="text-sm text-ink-muted">
            ~{itemCount} {t('common.itemCount', { count: itemCount }).replace(/^\d+\s/, '')}{pickup.isExpress ? ` · ${t('booking.express')}` : ''}
          </Text>
        </View>
        {amount != null ? (
          <Text className="text-sm font-bold text-ink-soft">Est. {rupees(amount)}</Text>
        ) : null}
      </View>

      {isActive ? (
        <View className="mt-3 flex-row gap-3">
          <Pressable
            onPress={(e) => {
              e.stopPropagation?.();
              goToTracking();
            }}
            accessibilityRole="button"
            accessibilityLabel={t('a11y.trackPickup', { number: pickup.requestNumber })}
            className="flex-row items-center gap-1"
          >
            <Text className="text-sm font-bold text-olive-700">{t('orders.trackPickup')}</Text>
            <Ionicons name="arrow-forward" size={14} color="#4A552A" />
          </Pressable>
          {(RESCHEDULABLE_STATUSES as string[]).includes(pickup.status) ? (
            <Pressable
              onPress={(e) => {
                e.stopPropagation?.();
                hapticImpact();
                router.push(`/(app)/orders/reschedule/${pickup.id}` as never);
              }}
              accessibilityRole="button"
              accessibilityLabel={t('reschedule.action')}
              className="flex-row items-center gap-1"
            >
              <Ionicons name="calendar-outline" size={14} color="#7B7A6C" />
              <Text className="text-sm font-semibold text-ink-muted">{t('reschedule.action')}</Text>
            </Pressable>
          ) : null}
        </View>
      ) : null}
    </Pressable>
  );
}

function OrderCard({ order }: { order: OrderDto }) {
  const router = useRouter();
  const { t } = useTranslation();
  const canTrack = !['delivered', 'cancelled', 'returned'].includes(order.status);
  const itemCount = order.items?.length ?? 0;

  return (
    <Pressable
      onPress={() => router.push(`/(app)/orders/${order.id}` as never)}
      accessibilityRole="button"
      accessibilityLabel={t('a11y.viewOrder', { number: order.orderNumber })}
      className="mb-3 rounded-3xl bg-white p-4"
      style={{ shadowColor: '#2E351C', shadowOpacity: 0.05, shadowRadius: 10, shadowOffset: { width: 0, height: 3 }, elevation: 2 }}
    >
      <View className="mb-2 flex-row items-start justify-between">
        <View>
          <Text className="text-base font-extrabold text-ink">#{order.orderNumber}</Text>
          <Text className="text-xs text-ink-muted">{formatDate(order.placedAt)}</Text>
        </View>
        <Badge label={t(`orders.statusLabels.${order.status}`, { defaultValue: ORDER_STATUS_LABEL[order.status] ?? order.status })} tone={ORDER_STATUS_TONE[order.status] ?? 'neutral'} />
      </View>

      <View className="flex-row items-center justify-between">
        <View className="flex-row items-center gap-1.5">
          <Ionicons name="shirt-outline" size={14} color="#7B7A6C" />
          <Text className="text-sm text-ink-muted">{t('common.itemCount', { count: itemCount })}{order.isExpress ? ` · ${t('booking.express')}` : ''}</Text>
        </View>
        <Text className="text-base font-extrabold text-ink">{rupees(order.grandTotal)}</Text>
      </View>

      {canTrack ? (
        <Pressable
          onPress={(e) => {
            e.stopPropagation?.();
            router.push(`/(app)/orders/tracking/${order.id}` as never);
          }}
          accessibilityRole="button"
          accessibilityLabel={t('a11y.trackOrder', { number: order.orderNumber })}
          className="mt-3 flex-row items-center gap-1 self-start"
        >
          <Text className="text-sm font-bold text-olive-700">{t('orders.trackOrder')}</Text>
          <Ionicons name="arrow-forward" size={14} color="#4A552A" />
        </Pressable>
      ) : null}
    </Pressable>
  );
}

// ── Screen ────────────────────────────────────────────────────────────────────

export default function MyOrdersScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const ordersQuery   = useMyOrders();
  const pickupsQuery  = useMyPickupRequests();

  const isLoading = ordersQuery.isLoading || pickupsQuery.isLoading;
  const isError   = ordersQuery.isError   || pickupsQuery.isError;
  const isFetching = ordersQuery.isFetching || pickupsQuery.isFetching;

  const refetch = () => {
    void ordersQuery.refetch();
    void pickupsQuery.refetch();
  };

  // CUST-BUG-03: useMemo MUST be declared before any conditional return.
  // Placing it after early-return guards violates the Rules of Hooks and causes
  // "Rendered more hooks than during the previous render" when isLoading/isError
  // flips between renders, crashing the entire tab navigator.
  const rawOrders  = ordersQuery.data?.list;
  const rawPickups = pickupsQuery.data?.list;

  const sections = useMemo(() => {
    const pickupSection = rawPickups && rawPickups.length > 0
      ? [{ title: t('orders.sectionPickups'), data: rawPickups as (PickupRequestDto | OrderDto)[], isPickup: true }]
      : [];
    const orderSection = rawOrders && rawOrders.length > 0
      ? [{ title: t('orders.sectionOrders'), data: rawOrders as (PickupRequestDto | OrderDto)[], isPickup: false }]
      : [];
    return [...pickupSection, ...orderSection];
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rawPickups, rawOrders, t]);

  const isEmpty = sections.length === 0;

  // Early returns come AFTER all hooks (Rules of Hooks).
  if (isLoading) return <SkeletonOrderList />;
  if (isError) return <ErrorState onRetry={refetch} />;

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      <View className="px-6 pb-3 pt-3">
        <Text className="text-2xl font-extrabold text-ink">{t('orders.title')}</Text>
      </View>

      {isEmpty ? (
        <EmptyState
          icon="receipt-outline"
          title={t('orders.noOrders')}
          message={t('orders.noOrdersMessage')}
          action={{ label: t('home.schedulePickup'), onPress: () => router.push('/(app)/booking/items' as never) }}
        />
      ) : (
        <SectionList
          sections={sections}
          keyExtractor={(item) => (item as { id: string }).id}
          renderSectionHeader={({ section }) =>
            sections.length > 1 ? (
              <Text className="mx-6 mb-2 mt-4 text-[11px] font-bold uppercase tracking-wider text-ink-faint">
                {section.title}
              </Text>
            ) : null
          }
          renderItem={({ item, section }) =>
            section.isPickup
              ? <PickupCard pickup={item as PickupRequestDto} />
              : <OrderCard order={item as OrderDto} />
          }
          contentContainerStyle={{ paddingHorizontal: 24, paddingBottom: 120 }}
          showsVerticalScrollIndicator={false}
          refreshControl={
            <RefreshControl
              refreshing={isFetching && !isLoading}
              onRefresh={refetch}
              tintColor="#4A552A"
            />
          }
        />
      )}
    </SafeAreaView>
  );
}
