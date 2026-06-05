/**
 * Order Detail screen — wired to:
 *   GET {Orders}/api/v1/customer/orders/{id}
 * Also provides Cancel Order action and link to Tracking.
 */
import React from 'react';
import {
  Alert,
  ScrollView,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useOrderDetail, useCancelOrder } from '@/hooks/useOrders';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { Button } from '@/components/ui/Button';
import type { OrderDto, OrderStatus } from '@/types/api';

function formatDate(iso?: string): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleString('en-IN', {
      day: 'numeric', month: 'short', year: 'numeric',
      hour: '2-digit', minute: '2-digit',
    });
  } catch {
    return iso;
  }
}

const CANCELLABLE_STATUSES: OrderStatus[] = [
  'placed', 'pickup_scheduled',
];

function DetailRow({ label, value }: { label: string; value: string }) {
  return (
    <View className="flex-row items-start justify-between border-b border-gray-100 py-3">
      <Text className="text-sm font-medium text-gray-500 flex-1">{label}</Text>
      <Text className="text-sm text-gray-900 flex-1 text-right">{value}</Text>
    </View>
  );
}

function OrderItems({ order }: { order: OrderDto }) {
  if (!order.items?.length) return null;
  return (
    <View className="mt-4 rounded-2xl bg-white px-4 shadow-sm" style={{ elevation: 1 }}>
      <Text className="py-3 text-sm font-bold text-gray-700 uppercase tracking-wider">
        Items
      </Text>
      {order.items.map((item) => (
        <View key={item.id} className="flex-row justify-between border-b border-gray-100 py-3">
          <View className="flex-1 mr-2">
            <Text className="text-sm font-semibold text-gray-900">{item.itemName}</Text>
            <Text className="text-xs text-gray-500">{item.serviceName}</Text>
          </View>
          <View className="items-end">
            <Text className="text-xs text-gray-500">x{item.quantity}</Text>
            <Text className="text-sm font-bold text-gray-900">
              ₹{item.lineTotal.toFixed(0)}
            </Text>
          </View>
        </View>
      ))}
    </View>
  );
}

export default function OrderDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router  = useRouter();
  const { data: order, isLoading, isError, refetch } = useOrderDetail(id ?? '');
  const cancelMutation = useCancelOrder();

  if (isLoading) return <ScreenLoader />;
  if (isError || !order) return <ErrorState onRetry={() => void refetch()} />;

  const canCancel = CANCELLABLE_STATUSES.includes(order.status);
  const canTrack  = !['delivered', 'cancelled', 'returned'].includes(order.status);

  const handleCancel = () => {
    Alert.alert(
      'Cancel Order',
      'Are you sure you want to cancel this order?',
      [
        { text: 'Keep Order', style: 'cancel' },
        {
          text: 'Cancel Order',
          style: 'destructive',
          onPress: () => {
            cancelMutation.mutate(order.id, {
              onSuccess: () => Alert.alert('Cancelled', 'Your order has been cancelled.'),
              onError: (err) =>
                Alert.alert('Error', err instanceof Error ? err.message : 'Cancellation failed'),
            });
          },
        },
      ],
    );
  };

  return (
    <SafeAreaView className="flex-1 bg-surface-muted" edges={['bottom']}>
      <ScrollView showsVerticalScrollIndicator={false}>
        {/* Summary card */}
        <View className="mx-6 mt-6 rounded-2xl bg-white px-4 shadow-sm" style={{ elevation: 1 }}>
          <DetailRow label="Order Number" value={order.orderNumber} />
          <DetailRow label="Status"       value={order.status.replace(/_/g, ' ').toUpperCase()} />
          <DetailRow label="Placed At"    value={formatDate(order.placedAt)} />
          {order.deliveredAt && <DetailRow label="Delivered At" value={formatDate(order.deliveredAt)} />}
          {order.cancelledAt && <DetailRow label="Cancelled At" value={formatDate(order.cancelledAt)} />}
        </View>

        {/* Financials */}
        <View className="mx-6 mt-4 rounded-2xl bg-white px-4 shadow-sm" style={{ elevation: 1 }}>
          <DetailRow label="Subtotal"  value={`₹${order.subtotal.toFixed(0)}`} />
          {order.discountTotal > 0 &&
            <DetailRow label="Discount" value={`-₹${order.discountTotal.toFixed(0)}`} />}
          {order.taxTotal > 0 &&
            <DetailRow label="Tax" value={`₹${order.taxTotal.toFixed(0)}`} />}
          <View className="flex-row justify-between py-3">
            <Text className="text-base font-bold text-gray-900">Total</Text>
            <Text className="text-base font-bold text-brand-700">
              ₹{order.grandTotal.toFixed(0)}
            </Text>
          </View>
        </View>

        {/* Line items */}
        <View className="mx-6">
          <OrderItems order={order} />
        </View>

        {/* Actions */}
        <View className="mx-6 mt-6 gap-3 pb-8">
          {canTrack && (
            <Button
              title="Track Order"
              variant="primary"
              fullWidth
              onPress={() => router.push(`/(app)/orders/tracking/${order.id}` as never)}
            />
          )}
          {canCancel && (
            <Button
              title="Cancel Order"
              variant="danger"
              fullWidth
              loading={cancelMutation.isPending}
              onPress={handleCancel}
            />
          )}
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}
