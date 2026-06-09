/**
 * Order detail — summary, financials, line items, cancel + track.
 *   GET {Orders}/customer/orders/{id}
 *   POST {Orders}/customer/orders/{id}/cancel
 */
import React from 'react';
import { Alert, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useOrderDetail, useCancelOrder } from '@/hooks/useOrders';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { Button } from '@/components/ui/Button';
import { Badge } from '@/components/ui/Badge';
import { rupees, formatDateTime } from '@/lib/format';
import type { OrderDto, OrderStatus } from '@/types/api';

const CANCELLABLE: OrderStatus[] = ['placed', 'pickup_scheduled'];

function Row({ label, value }: { label: string; value: string }) {
  return (
    <View className="flex-row items-start justify-between py-2.5">
      <Text className="flex-1 text-sm text-ink-muted">{label}</Text>
      <Text className="flex-1 text-right text-sm font-bold text-ink">{value}</Text>
    </View>
  );
}

function Items({ order }: { order: OrderDto }) {
  if (!order.items?.length) return null;
  return (
    <View className="mx-6 mt-4 rounded-3xl bg-white px-4 py-1">
      <Text className="py-3 text-xs font-bold uppercase tracking-wider text-ink-faint">Items</Text>
      {order.items.map((item) => (
        <View key={item.id} className="flex-row justify-between border-t border-cream-200 py-3">
          <View className="mr-2 flex-1">
            <Text className="text-sm font-bold text-ink">{item.itemName}</Text>
            <Text className="text-xs text-ink-muted">{item.serviceName}</Text>
          </View>
          <View className="items-end">
            <Text className="text-xs text-ink-muted">×{item.quantity}</Text>
            <Text className="text-sm font-bold text-ink">{rupees(item.lineTotal)}</Text>
          </View>
        </View>
      ))}
    </View>
  );
}

export default function OrderDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const { data: order, isLoading, isError, refetch } = useOrderDetail(id ?? '');
  const cancelMutation = useCancelOrder();

  if (isLoading) return <ScreenLoader />;
  if (isError || !order) return <ErrorState onRetry={() => void refetch()} />;

  const canCancel = CANCELLABLE.includes(order.status);
  const canTrack = !['delivered', 'cancelled', 'returned'].includes(order.status);

  const handleCancel = () => {
    Alert.alert('Cancel order', 'Are you sure you want to cancel this order?', [
      { text: 'Keep order', style: 'cancel' },
      {
        text: 'Cancel order',
        style: 'destructive',
        onPress: () =>
          cancelMutation.mutate(order.id, {
            onSuccess: () => Alert.alert('Cancelled', 'Your order has been cancelled.'),
            onError: (err) => Alert.alert('Error', err instanceof Error ? err.message : 'Cancellation failed'),
          }),
      },
    ]);
  };

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      {/* Header */}
      <View className="flex-row items-center gap-3 px-5 pb-2 pt-2">
        <Pressable
          onPress={() => router.back()}
          className="h-10 w-10 items-center justify-center rounded-full bg-white"
          accessibilityLabel="Go back"
        >
          <Ionicons name="chevron-back" size={22} color="#3C3F35" />
        </Pressable>
        <Text className="text-lg font-extrabold text-ink">#{order.orderNumber}</Text>
      </View>

      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 40 }}>
        {/* Summary */}
        <View className="mx-6 mt-2 rounded-3xl bg-white px-4 py-1">
          <View className="flex-row items-center justify-between py-3">
            <Text className="text-sm text-ink-muted">Status</Text>
            <Badge label={order.status.replace(/_/g, ' ')} tone="gold" />
          </View>
          <View className="h-px bg-cream-200" />
          <Row label="Placed" value={formatDateTime(order.placedAt)} />
          {order.deliveredAt ? <Row label="Delivered" value={formatDateTime(order.deliveredAt)} /> : null}
          {order.cancelledAt ? <Row label="Cancelled" value={formatDateTime(order.cancelledAt)} /> : null}
        </View>

        {/* Financials */}
        <View className="mx-6 mt-4 rounded-3xl bg-white px-4 py-1">
          <Row label="Subtotal" value={rupees(order.subtotal)} />
          {order.discountTotal > 0 ? <Row label="Discount" value={`−${rupees(order.discountTotal)}`} /> : null}
          {order.taxTotal > 0 ? <Row label="Tax" value={rupees(order.taxTotal)} /> : null}
          <View className="flex-row justify-between border-t border-cream-200 py-3">
            <Text className="text-base font-extrabold text-ink">Total</Text>
            <Text className="text-base font-extrabold text-olive-700">{rupees(order.grandTotal)}</Text>
          </View>
        </View>

        <Items order={order} />

        {/* Actions */}
        <View className="mx-6 mt-6 gap-3">
          {canTrack ? (
            <Button
              title="Track order"
              fullWidth
              iconRight="arrow-forward"
              onPress={() => router.push(`/(app)/orders/tracking/${order.id}` as never)}
            />
          ) : null}
          {canCancel ? (
            <Button
              title="Cancel order"
              variant="danger"
              fullWidth
              loading={cancelMutation.isPending}
              onPress={handleCancel}
            />
          ) : null}
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}
