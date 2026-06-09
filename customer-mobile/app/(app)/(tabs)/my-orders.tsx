/**
 * Orders tab — order history.
 * GET {Orders}/customer/orders?page=&pageSize=
 */
import React from 'react';
import { FlatList, Pressable, RefreshControl, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useMyOrders } from '@/hooks/useOrders';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { EmptyState } from '@/components/ui/EmptyState';
import { Badge } from '@/components/ui/Badge';
import { rupees, formatDate } from '@/lib/format';
import type { OrderDto, OrderStatus } from '@/types/api';

const STATUS_TONE: Record<OrderStatus, 'olive' | 'gold' | 'neutral' | 'success' | 'danger' | 'info'> = {
  placed: 'info', pickup_scheduled: 'info', pickup_assigned: 'info', picked_up: 'gold',
  received: 'gold', sorting: 'gold', in_process: 'gold', qc: 'gold', ready: 'success',
  delivery_scheduled: 'success', out_for_delivery: 'success', delivered: 'neutral',
  cancelled: 'danger', returned: 'danger', rewash: 'gold', disputed: 'danger',
};

const STATUS_LABEL: Record<OrderStatus, string> = {
  placed: 'Placed', pickup_scheduled: 'Pickup scheduled', pickup_assigned: 'Rider assigned',
  picked_up: 'Picked up', received: 'Received', sorting: 'Sorting', in_process: 'In process',
  qc: 'Quality check', ready: 'Ready', delivery_scheduled: 'Out soon', out_for_delivery: 'Out for delivery',
  delivered: 'Delivered', cancelled: 'Cancelled', returned: 'Returned', rewash: 'Rewash', disputed: 'Disputed',
};

function OrderCard({ order }: { order: OrderDto }) {
  const router = useRouter();
  const canTrack = !['delivered', 'cancelled', 'returned'].includes(order.status);
  const itemCount = order.items?.length ?? 0;

  return (
    <Pressable
      onPress={() => router.push(`/(app)/orders/${order.id}` as never)}
      accessibilityRole="button"
      accessibilityLabel={`Order ${order.orderNumber}`}
      className="mb-3 rounded-3xl bg-white p-4"
      style={{ shadowColor: '#2E351C', shadowOpacity: 0.05, shadowRadius: 10, shadowOffset: { width: 0, height: 3 }, elevation: 2 }}
    >
      <View className="mb-2 flex-row items-start justify-between">
        <View>
          <Text className="text-base font-extrabold text-ink">#{order.orderNumber}</Text>
          <Text className="text-xs text-ink-muted">{formatDate(order.placedAt)}</Text>
        </View>
        <Badge label={STATUS_LABEL[order.status] ?? order.status} tone={STATUS_TONE[order.status] ?? 'neutral'} />
      </View>

      <View className="flex-row items-center justify-between">
        <View className="flex-row items-center gap-1.5">
          <Ionicons name="shirt-outline" size={14} color="#7B7A6C" />
          <Text className="text-sm text-ink-muted">{itemCount} item{itemCount !== 1 ? 's' : ''}{order.isExpress ? ' · Express' : ''}</Text>
        </View>
        <Text className="text-base font-extrabold text-ink">{rupees(order.grandTotal)}</Text>
      </View>

      {canTrack ? (
        <Pressable
          onPress={(e) => {
            e.stopPropagation?.();
            router.push(`/(app)/orders/tracking/${order.id}` as never);
          }}
          className="mt-3 flex-row items-center gap-1 self-start"
        >
          <Text className="text-sm font-bold text-olive-700">Track order</Text>
          <Ionicons name="arrow-forward" size={14} color="#4A552A" />
        </Pressable>
      ) : null}
    </Pressable>
  );
}

export default function MyOrdersScreen() {
  const { data, isLoading, isError, refetch, isFetching } = useMyOrders();

  if (isLoading) return <ScreenLoader />;
  if (isError) return <ErrorState onRetry={() => void refetch()} />;

  const orders = data?.list ?? [];

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      <View className="px-6 pb-3 pt-3">
        <Text className="text-2xl font-extrabold text-ink">Your orders</Text>
      </View>

      {orders.length === 0 ? (
        <EmptyState
          icon="receipt-outline"
          title="No orders yet"
          message="Tap the + button to schedule your first pickup."
        />
      ) : (
        <FlatList
          data={orders}
          keyExtractor={(o) => o.id}
          renderItem={({ item }) => <OrderCard order={item} />}
          contentContainerStyle={{ paddingHorizontal: 24, paddingBottom: 120 }}
          showsVerticalScrollIndicator={false}
          refreshControl={
            <RefreshControl
              refreshing={isFetching && !isLoading}
              onRefresh={() => void refetch()}
              tintColor="#4A552A"
            />
          }
        />
      )}
    </SafeAreaView>
  );
}
