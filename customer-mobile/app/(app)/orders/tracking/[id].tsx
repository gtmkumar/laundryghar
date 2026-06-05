/**
 * Order Tracking screen — wired to:
 *   GET {Orders}/api/v1/customer/orders/{id}/tracking
 * Renders a timeline of status history events.
 */
import React from 'react';
import {
  FlatList,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams } from 'expo-router';
import { useOrderTracking } from '@/hooks/useOrders';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { EmptyState } from '@/components/ui/EmptyState';
import type { OrderStatusHistoryDto, OrderStatus } from '@/types/api';

const STATUS_LABELS: Record<OrderStatus, string> = {
  placed:             'Order Placed',
  pickup_scheduled:   'Pickup Scheduled',
  pickup_assigned:    'Rider Assigned',
  picked_up:          'Picked Up',
  received:           'Received at Store',
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

const STATUS_EMOJIS: Partial<Record<OrderStatus, string>> = {
  placed:             '📋',
  pickup_scheduled:   '📅',
  pickup_assigned:    '🛵',
  picked_up:          '✅',
  received:           '🏪',
  sorting:            '🗂️',
  in_process:         '🧺',
  qc:                 '🔍',
  ready:              '📦',
  delivery_scheduled: '🚚',
  out_for_delivery:   '🚗',
  delivered:          '🎉',
  cancelled:          '❌',
  returned:           '↩️',
  rewash:             '♻️',
  disputed:           '⚠️',
};

function formatTime(iso: string): string {
  try {
    return new Date(iso).toLocaleString('en-IN', {
      day: 'numeric', month: 'short',
      hour: '2-digit', minute: '2-digit',
    });
  } catch {
    return iso;
  }
}

function TrackingEvent({
  event,
  isFirst,
  isLast,
}: {
  event: OrderStatusHistoryDto;
  isFirst: boolean;
  isLast: boolean;
}) {
  const label = STATUS_LABELS[event.status] ?? event.status.replace(/_/g, ' ');
  const emoji = STATUS_EMOJIS[event.status] ?? '•';

  return (
    <View className="flex-row">
      {/* Timeline column */}
      <View className="items-center w-10">
        <View className={`w-8 h-8 rounded-full items-center justify-center ${isFirst ? 'bg-brand-700' : 'bg-gray-200'}`}>
          <Text className="text-sm" accessibilityElementsHidden>{emoji}</Text>
        </View>
        {!isLast && <View className="w-0.5 flex-1 bg-gray-200 mt-1" style={{ minHeight: 24 }} />}
      </View>

      {/* Event info */}
      <View className="flex-1 pb-6 pl-3">
        <Text className={`text-sm font-semibold ${isFirst ? 'text-brand-700' : 'text-gray-800'}`}>
          {label}
        </Text>
        <Text className="text-xs text-gray-500">{formatTime(event.changedAt)}</Text>
        {event.reason ? (
          <Text className="mt-1 text-xs text-gray-400">{event.reason}</Text>
        ) : null}
      </View>
    </View>
  );
}

export default function OrderTrackingScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { data, isLoading, isError, refetch } = useOrderTracking(id ?? '');

  if (isLoading) return <ScreenLoader />;
  if (isError)   return <ErrorState onRetry={() => void refetch()} />;
  if (!data?.length) return <EmptyState title="No tracking info yet" />;

  // Sort newest first for display; reverse so latest is at top
  const sorted = [...data].sort(
    (a, b) => new Date(b.changedAt).getTime() - new Date(a.changedAt).getTime(),
  );

  return (
    <SafeAreaView className="flex-1 bg-white" edges={['bottom']}>
      <FlatList
        data={sorted}
        keyExtractor={(e) => e.id}
        contentContainerStyle={{ padding: 24 }}
        renderItem={({ item, index }) => (
          <TrackingEvent
            event={item}
            isFirst={index === 0}
            isLast={index === sorted.length - 1}
          />
        )}
        ListHeaderComponent={
          <Text className="mb-6 text-lg font-bold text-gray-900">Order Timeline</Text>
        }
      />
    </SafeAreaView>
  );
}
