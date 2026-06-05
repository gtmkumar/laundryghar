/**
 * My Orders tab — wired to:
 *   GET {Orders}/api/v1/customer/orders?page=&pageSize=
 * Taps into order detail / tracking via stack screens pushed from (app).
 */
import React from 'react';
import {
  FlatList,
  Pressable,
  RefreshControl,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useMyOrders } from '@/hooks/useOrders';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { EmptyState } from '@/components/ui/EmptyState';
import type { OrderDto, OrderStatus } from '@/types/api';

const STATUS_COLORS: Record<OrderStatus, string> = {
  placed:             'bg-blue-100 text-blue-700',
  pickup_scheduled:   'bg-blue-100 text-blue-700',
  pickup_assigned:    'bg-blue-100 text-blue-700',
  picked_up:          'bg-yellow-100 text-yellow-700',
  received:           'bg-yellow-100 text-yellow-700',
  sorting:            'bg-purple-100 text-purple-700',
  in_process:         'bg-purple-100 text-purple-700',
  qc:                 'bg-indigo-100 text-indigo-700',
  ready:              'bg-green-100 text-green-700',
  delivery_scheduled: 'bg-green-100 text-green-700',
  out_for_delivery:   'bg-green-100 text-green-700',
  delivered:          'bg-gray-100 text-gray-600',
  cancelled:          'bg-red-100 text-red-700',
  returned:           'bg-orange-100 text-orange-700',
  rewash:             'bg-orange-100 text-orange-700',
  disputed:           'bg-red-100 text-red-700',
};

const STATUS_LABELS: Record<OrderStatus, string> = {
  placed:             'Placed',
  pickup_scheduled:   'Pickup Scheduled',
  pickup_assigned:    'Rider Assigned',
  picked_up:          'Picked Up',
  received:           'Received',
  sorting:            'Sorting',
  in_process:         'In Process',
  qc:                 'Quality Check',
  ready:              'Ready',
  delivery_scheduled: 'Delivery Scheduled',
  out_for_delivery:   'Out for Delivery',
  delivered:          'Delivered',
  cancelled:          'Cancelled',
  returned:           'Returned',
  rewash:             'Rewash',
  disputed:           'Disputed',
};

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString('en-IN', {
      day: 'numeric', month: 'short', year: 'numeric',
    });
  } catch {
    return iso;
  }
}

function OrderCard({ order }: { order: OrderDto }) {
  const router = useRouter();
  const colorClass = STATUS_COLORS[order.status] ?? 'bg-gray-100 text-gray-700';
  const label      = STATUS_LABELS[order.status] ?? order.status;

  return (
    <Pressable
      onPress={() => router.push(`/(app)/orders/${order.id}` as never)}
      accessibilityRole="button"
      accessibilityLabel={`Order ${order.orderNumber}, status ${label}`}
      className="mb-3 rounded-2xl border border-gray-200 bg-white p-4 active:opacity-80"
      style={{ elevation: 1 }}
    >
      <View className="flex-row items-start justify-between mb-2">
        <View>
          <Text className="text-base font-bold text-gray-900">
            {order.orderNumber}
          </Text>
          <Text className="text-xs text-gray-500">{formatDate(order.placedAt)}</Text>
        </View>
        <View className={`rounded-full px-3 py-1 ${colorClass.split(' ')[0]}`}>
          <Text className={`text-xs font-semibold ${colorClass.split(' ')[1]}`}>
            {label}
          </Text>
        </View>
      </View>

      <View className="flex-row items-center justify-between">
        <Text className="text-sm text-gray-600">
          {order.items?.length ?? 0} item{(order.items?.length ?? 0) !== 1 ? 's' : ''}
        </Text>
        <Text className="text-base font-bold text-gray-900">
          ₹{order.grandTotal.toFixed(0)}
        </Text>
      </View>

      {/* Track link */}
      {!['delivered', 'cancelled', 'returned'].includes(order.status) && (
        <Pressable
          onPress={(e) => {
            e.stopPropagation?.();
            router.push(`/(app)/orders/tracking/${order.id}` as never);
          }}
          accessibilityRole="button"
          accessibilityLabel="Track this order"
          className="mt-3 self-start"
        >
          <Text className="text-sm font-medium text-brand-700">Track Order →</Text>
        </Pressable>
      )}
    </Pressable>
  );
}

export default function MyOrdersScreen() {
  const { data, isLoading, isError, refetch, isFetching } = useMyOrders();

  if (isLoading) return <ScreenLoader />;
  if (isError)   return <ErrorState onRetry={() => void refetch()} />;

  const orders = data?.list ?? [];

  return (
    <SafeAreaView className="flex-1 bg-surface-muted">
      <View className="px-6 pt-6 pb-4">
        <Text className="text-2xl font-bold text-gray-900">My Orders</Text>
      </View>

      {orders.length === 0 ? (
        <EmptyState
          title="No orders yet"
          message="Schedule a pickup to place your first order"
        />
      ) : (
        <FlatList
          data={orders}
          keyExtractor={(o) => o.id}
          renderItem={({ item }) => <OrderCard order={item} />}
          contentContainerStyle={{ paddingHorizontal: 24, paddingBottom: 32 }}
          showsVerticalScrollIndicator={false}
          refreshControl={
            <RefreshControl
              refreshing={isFetching && !isLoading}
              onRefresh={() => void refetch()}
              tintColor="#1D4ED8"
            />
          }
        />
      )}
    </SafeAreaView>
  );
}
