/**
 * Order tracking — vertical lifecycle timeline + garments.
 * Real orders (UUID id) are wired to:
 *   GET {Orders}/customer/orders/{id}          (header + garments)
 *   GET {Orders}/customer/orders/{id}/tracking (event timestamps)
 * A non-UUID id (e.g. the "LG-#####" from the booking flow) renders a demo
 * timeline so the just-placed order shows something meaningful.
 */
import React from 'react';
import { Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useOrderDetail, useOrderTracking } from '@/hooks/useOrders';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { rupees, formatDateTime } from '@/lib/format';
import type { OrderStatus } from '@/types/api';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// Canonical ranking across the whole lifecycle (used to mark steps done/active).
const RANK: Record<OrderStatus, number> = {
  placed: 0, pickup_scheduled: 1, pickup_assigned: 2, picked_up: 3,
  received: 4, sorting: 5, in_process: 6, qc: 7, ready: 8,
  delivery_scheduled: 9, out_for_delivery: 10, delivered: 11,
  cancelled: 99, returned: 98, rewash: 97, disputed: 96,
};

// Display steps shown in the timeline (curated subset).
const STEPS: { status: OrderStatus; label: string }[] = [
  { status: 'placed',           label: 'Order placed' },
  { status: 'picked_up',        label: 'Picked up by rider' },
  { status: 'received',         label: 'Received at store' },
  { status: 'in_process',       label: 'In wash' },
  { status: 'qc',               label: 'Quality check' },
  { status: 'out_for_delivery', label: 'Out for delivery' },
  { status: 'delivered',        label: 'Delivered' },
];

interface TimelineRow {
  label: string;
  time?: string;
  state: 'done' | 'active' | 'pending';
}

function buildTimeline(currentRank: number, times: Partial<Record<OrderStatus, string>>): TimelineRow[] {
  // The active step = the last display step whose rank <= currentRank.
  let activeIdx = -1;
  STEPS.forEach((s, i) => {
    if (RANK[s.status] <= currentRank) activeIdx = i;
  });
  return STEPS.map((s, i) => ({
    label: s.label,
    time: times[s.status],
    state: i < activeIdx ? 'done' : i === activeIdx ? 'active' : 'pending',
  }));
}

function TimelineNode({ row, isLast }: { row: TimelineRow; isLast: boolean }) {
  const dot =
    row.state === 'done'
      ? 'bg-olive-600'
      : row.state === 'active'
        ? 'bg-gold-400'
        : 'bg-cream-300';
  const line = row.state === 'done' ? 'bg-olive-600' : 'bg-cream-300';

  return (
    <View className="flex-row">
      <View className="w-8 items-center">
        <View className={`h-4 w-4 rounded-full ${dot}`}>
          {row.state === 'done' ? (
            <Ionicons name="checkmark" size={12} color="#FFFFFF" style={{ marginTop: 1, marginLeft: 1 }} />
          ) : null}
        </View>
        {!isLast ? <View className={`w-0.5 flex-1 ${line}`} style={{ minHeight: 34 }} /> : null}
      </View>
      <View className="flex-1 pb-6 pl-3">
        <Text className={`text-base font-bold ${row.state === 'pending' ? 'text-ink-faint' : 'text-ink'}`}>
          {row.label}
        </Text>
        {row.time ? <Text className="text-xs text-ink-muted">{row.time}</Text> : null}
        {row.state === 'active' && !row.time ? (
          <Text className="text-xs font-semibold text-gold-600">In progress</Text>
        ) : null}
      </View>
    </View>
  );
}

function Header({ orderNumber, banner }: { orderNumber: string; banner: string }) {
  const router = useRouter();
  return (
    <View className="px-5 pt-2">
      <View className="flex-row items-center gap-3 pb-3">
        <Pressable
          onPress={() => router.back()}
          className="h-10 w-10 items-center justify-center rounded-full bg-white"
          accessibilityLabel="Go back"
        >
          <Ionicons name="chevron-back" size={22} color="#3C3F35" />
        </Pressable>
        <Text className="text-lg font-extrabold text-ink">#{orderNumber}</Text>
      </View>
      <View className="rounded-2xl bg-gold-100 p-4">
        <Text className="text-[11px] font-bold uppercase tracking-wider text-gold-700">In process</Text>
        <Text className="mt-1 text-2xl font-extrabold text-ink">{banner}</Text>
        <Text className="mt-1 text-xs text-ink-muted">We’ll WhatsApp when the rider is on the way.</Text>
      </View>
    </View>
  );
}

// ---------------------------------------------------------------------------
// Demo (just-placed order from the booking flow — non-UUID id)
// ---------------------------------------------------------------------------

function DemoTracking({ orderNumber }: { orderNumber: string }) {
  const timeline = buildTimeline(RANK.in_process, {
    placed: 'Just now',
  });
  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 40 }}>
        <Header orderNumber={orderNumber} banner="Ready by Sat · 4 PM" />
        <Text className="mx-5 mb-4 mt-7 text-lg font-extrabold text-ink">Timeline</Text>
        <View className="mx-5">
          {timeline.map((row, i) => (
            <TimelineNode key={row.label} row={row} isLast={i === timeline.length - 1} />
          ))}
        </View>
        <View className="mx-5 mt-2 rounded-2xl bg-white p-4">
          <Text className="text-xs text-ink-muted">
            Garment-by-garment details appear here once your items are received and sorted at the store.
          </Text>
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

// ---------------------------------------------------------------------------
// Live (real order — UUID id)
// ---------------------------------------------------------------------------

function LiveTracking({ id }: { id: string }) {
  const { data: order, isLoading: orderLoading, isError, refetch } = useOrderDetail(id);
  const { data: history, isLoading: histLoading } = useOrderTracking(id);

  if (orderLoading || histLoading) return <ScreenLoader />;
  if (isError || !order) return <ErrorState onRetry={() => void refetch()} />;

  const times: Partial<Record<OrderStatus, string>> = {};
  (history ?? []).forEach((e) => {
    times[e.status] = formatDateTime(e.changedAt);
  });
  const currentRank = RANK[order.status] ?? 0;
  const timeline = buildTimeline(currentRank, times);

  const banner = order.readyAt
    ? `Ready by ${formatDateTime(order.readyAt)}`
    : order.status === 'delivered'
      ? 'Delivered'
      : 'In process';

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 40 }}>
        <Header orderNumber={order.orderNumber} banner={banner} />

        <Text className="mx-5 mb-4 mt-7 text-lg font-extrabold text-ink">Timeline</Text>
        <View className="mx-5">
          {timeline.map((row, i) => (
            <TimelineNode key={row.label} row={row} isLast={i === timeline.length - 1} />
          ))}
        </View>

        {/* Garments */}
        {order.items && order.items.length > 0 ? (
          <>
            <Text className="mx-5 mb-3 mt-4 text-lg font-extrabold text-ink">
              Garments · {order.items.reduce((n, it) => n + it.quantity, 0)}
            </Text>
            <View className="mx-5 rounded-2xl bg-white px-4">
              {order.items.map((it) => (
                <View key={it.id} className="flex-row items-center justify-between border-b border-cream-200 py-3 last:border-0">
                  <View className="flex-1">
                    <Text className="text-base font-bold text-ink">{it.itemName}</Text>
                    <Text className="text-xs text-ink-muted">{it.serviceName}</Text>
                  </View>
                  <Text className="mr-3 text-sm text-ink-muted">{it.quantity}×</Text>
                  <Text className="text-sm font-bold text-ink">{rupees(it.lineTotal)}</Text>
                </View>
              ))}
            </View>
          </>
        ) : null}
      </ScrollView>
    </SafeAreaView>
  );
}

export default function OrderTrackingScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const safeId = id ?? '';
  if (UUID_RE.test(safeId)) {
    return <LiveTracking id={safeId} />;
  }
  return <DemoTracking orderNumber={safeId || 'LG-00000'} />;
}
