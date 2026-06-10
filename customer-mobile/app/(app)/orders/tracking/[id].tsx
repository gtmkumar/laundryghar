/**
 * Order / pickup tracking — vertical lifecycle timeline.
 *
 * Route param `id` is either:
 *   - A real order UUID     → LiveOrderTracking (uses GET /customer/orders/{id}/tracking)
 *   - A pickup-request UUID → PickupTracking    (uses GET /customer/pickup-requests/{id})
 *   - A legacy LG-##### id  → DemoTracking      (demo timeline; shown when bookingApi was false)
 *
 * UUID detection: tests against UUID_RE; non-UUID → DemoTracking.
 * Pickup-vs-order disambiguation: the confirm screen stores the pickup request id in
 * bookingStore.confirmed.pickupRequestId; the tracking screen also receives a `kind`
 * param from the my-orders PickupCard. When neither is set, we try order first then
 * pickup (order is more common).
 */
import React from 'react';
import { Alert, Pressable, ScrollView, Text, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { useOrderDetail, useOrderTracking, usePickupRequestDetail, useRateOrder } from '@/hooks/useOrders';
import { useBookingStore } from '@/store/bookingStore';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { rupees, formatDateTime } from '@/lib/format';
import type { OrderStatus, PickupRequestStatus } from '@/types/api';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// ── Order lifecycle timeline ──────────────────────────────────────────────────

const ORDER_RANK: Record<OrderStatus, number> = {
  placed: 0, pickup_scheduled: 1, pickup_assigned: 2, picked_up: 3,
  received: 4, sorting: 5, in_process: 6, qc: 7, ready: 8,
  delivery_scheduled: 9, out_for_delivery: 10, delivered: 11, closed: 12,
  cancelled: 99, returned: 98, rewash: 97, disputed: 96,
};

const ORDER_STEPS: { status: OrderStatus; label: string }[] = [
  { status: 'placed',           label: 'tracking.steps.placed' },
  { status: 'picked_up',        label: 'tracking.steps.picked_up' },
  { status: 'received',         label: 'tracking.steps.received' },
  { status: 'in_process',       label: 'tracking.steps.in_process' },
  { status: 'qc',               label: 'tracking.steps.qc' },
  { status: 'out_for_delivery', label: 'tracking.steps.out_for_delivery' },
  { status: 'delivered',        label: 'tracking.steps.delivered' },
];

// ── Pickup-request lifecycle timeline ─────────────────────────────────────────

const PICKUP_RANK: Record<PickupRequestStatus, number> = {
  pending: 0, assigned: 1, rider_dispatched: 2, arrived: 3,
  completed: 4, converted: 5,
  // Terminal / non-forward states — high rank so they don't mark forward steps as done.
  cancelled: 99, no_response: 98, rescheduled: 97,
};

const PICKUP_STEPS: { status: PickupRequestStatus; label: string }[] = [
  { status: 'pending',          label: 'tracking.pickupSteps.pending' },
  { status: 'assigned',         label: 'tracking.pickupSteps.assigned' },
  { status: 'rider_dispatched', label: 'tracking.pickupSteps.rider_dispatched' },
  { status: 'arrived',          label: 'tracking.pickupSteps.arrived' },
  { status: 'completed',        label: 'tracking.pickupSteps.completed' },
];

// ── Shared types ──────────────────────────────────────────────────────────────

interface TimelineRow {
  label: string;
  time?: string;
  state: 'done' | 'active' | 'pending';
}

function buildTimeline<S extends string>(
  steps: { status: S; label: string }[],
  rank: Record<S, number>,
  currentRank: number,
  times: Partial<Record<S, string>>,
): TimelineRow[] {
  let activeIdx = -1;
  steps.forEach((s, i) => {
    if (rank[s.status] <= currentRank) activeIdx = i;
  });
  return steps.map((s, i) => ({
    label: s.label,
    time: times[s.status],
    state: i < activeIdx ? 'done' : i === activeIdx ? 'active' : 'pending',
  }));
}

// ── Shared UI ─────────────────────────────────────────────────────────────────

function TimelineNode({ row, isLast }: { row: TimelineRow; isLast: boolean }) {
  const { t } = useTranslation();
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
          {t(row.label, { defaultValue: row.label })}
        </Text>
        {row.time ? <Text className="text-xs text-ink-muted">{row.time}</Text> : null}
        {row.state === 'active' && !row.time ? (
          <Text className="text-xs font-semibold text-gold-600">{t('tracking.active')}</Text>
        ) : null}
      </View>
    </View>
  );
}

function Header({ orderNumber, banner }: { orderNumber: string; banner: string }) {
  const { t } = useTranslation();
  const router = useRouter();
  return (
    <View className="px-5 pt-2">
      <View className="flex-row items-center gap-3 pb-3">
        <Pressable
          onPress={() => router.back()}
          className="h-10 w-10 items-center justify-center rounded-full bg-white"
          accessibilityRole="button"
          accessibilityLabel={t('a11y.back')}
        >
          <Ionicons name="chevron-back" size={22} color="#3C3F35" />
        </Pressable>
        <Text className="text-lg font-extrabold text-ink">#{orderNumber}</Text>
      </View>
      <View className="rounded-2xl bg-gold-100 p-4">
        <Text className="text-[11px] font-bold uppercase tracking-wider text-gold-700">{t('tracking.inProcess')}</Text>
        <Text className="mt-1 text-2xl font-extrabold text-ink">{banner}</Text>
        <Text className="mt-1 text-xs text-ink-muted">{t('tracking.whatsappNote')}</Text>
      </View>
    </View>
  );
}

// ── Demo (legacy LG-##### ids from local fallback path) ───────────────────────

function DemoTracking({ orderNumber }: { orderNumber: string }) {
  const { t } = useTranslation();
  const times: Partial<Record<OrderStatus, string>> = {};
  const timeline = buildTimeline(ORDER_STEPS, ORDER_RANK, ORDER_RANK.in_process, {
    ...times,
    placed: 'Just now',
  });
  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 40 }}>
        <Header orderNumber={orderNumber} banner="Ready by Sat · 4 PM" />
        <Text className="mx-5 mb-4 mt-7 text-lg font-extrabold text-ink">{t('tracking.timeline')}</Text>
        <View className="mx-5">
          {timeline.map((row, i) => (
            <TimelineNode key={row.label} row={row} isLast={i === timeline.length - 1} />
          ))}
        </View>
        <View className="mx-5 mt-2 rounded-2xl bg-white p-4">
          <Text className="text-xs text-ink-muted">
            {t('tracking.garmentNoteOrder')}
          </Text>
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

// ── Pickup-request tracking (real, status-based) ──────────────────────────────

function PickupTracking({ id }: { id: string }) {
  const { t } = useTranslation();
  const { data: pickup, isLoading, isError, refetch } = usePickupRequestDetail(id);

  if (isLoading) return <ScreenLoader />;
  if (isError || !pickup) return <ErrorState onRetry={() => void refetch()} />;

  const currentRank = PICKUP_RANK[pickup.status] ?? 0;
  const times: Partial<Record<PickupRequestStatus, string>> = {
    pending: formatDateTime(pickup.createdAt),
  };
  const timeline = buildTimeline(PICKUP_STEPS, PICKUP_RANK, currentRank, times);

  const isCancelled = pickup.status === 'cancelled';

  const banner = pickup.status === 'completed'
    ? 'Items collected!'
    : pickup.status === 'converted'
      ? 'Order created at store'
      : isCancelled
        ? 'Pickup cancelled'
        : pickup.status === 'no_response'
          ? 'No response'
          : `Pickup on ${pickup.pickupDate}`;

  const itemCount = pickup.estimatedItems
    ?? pickup.cartItems.reduce((n, i) => n + i.quantity, 0);

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 40 }}>
        <Header orderNumber={pickup.requestNumber} banner={banner} />

        {/* Cancelled / rejected notice */}
        {isCancelled ? (
          <View className="mx-5 mt-4 rounded-2xl border border-red-200 bg-red-50 p-4">
            <Text className="text-sm font-extrabold text-red-700">{t('tracking.cancelledTitle')}</Text>
            <Text className="mt-1 text-xs text-red-600">
              {t('tracking.cancelledMessage')}
            </Text>
          </View>
        ) : null}

        <Text className="mx-5 mb-4 mt-7 text-lg font-extrabold text-ink">{t('tracking.timeline')}</Text>
        <View className="mx-5">
          {timeline.map((row, i) => (
            <TimelineNode key={row.label} row={row} isLast={i === timeline.length - 1} />
          ))}
        </View>

        {/* Estimated items */}
        {pickup.cartItems.length > 0 ? (
          <>
            <Text className="mx-5 mb-3 mt-4 text-lg font-extrabold text-ink">
              {t('tracking.estimatedItems', { count: itemCount })}
            </Text>
            <View className="mx-5 rounded-2xl bg-white px-4">
              {pickup.cartItems.map((it, idx) => (
                <View
                  // eslint-disable-next-line react/no-array-index-key
                  key={`${it.displayLabel}-${idx}`}
                  className="flex-row items-center justify-between border-b border-cream-200 py-3 last:border-0"
                >
                  <View className="flex-1">
                    <Text className="text-base font-bold text-ink">{it.displayLabel}</Text>
                  </View>
                  <Text className="mr-3 text-sm text-ink-muted">{it.quantity}×</Text>
                  {it.estimatedUnitPrice != null ? (
                    <Text className="text-sm font-bold text-ink">{rupees(it.estimatedUnitPrice * it.quantity)}</Text>
                  ) : null}
                </View>
              ))}
            </View>
          </>
        ) : itemCount > 0 ? (
          <View className="mx-5 mt-2 rounded-2xl bg-white p-4">
            <Text className="text-sm text-ink-muted">{t('tracking.itemsEstimated', { count: itemCount, plural: itemCount !== 1 ? 's' : '' })}</Text>
          </View>
        ) : null}

        <View className="mx-5 mt-4 rounded-2xl bg-white p-4">
          <Text className="text-xs text-ink-muted">
            {t('tracking.garmentNote')}
          </Text>
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

// ── Inline rating widget (tracking screen) ────────────────────────────────────

function TrackingRatingWidget({ orderId, existingRating }: { orderId: string; existingRating?: number | null }) {
  const { t } = useTranslation();
  const [score, setScore] = React.useState<number>(existingRating ?? 0);
  const [comment, setComment] = React.useState('');
  const [submitted, setSubmitted] = React.useState(!!existingRating);
  const rateMutation = useRateOrder(orderId);

  const handleSubmit = () => {
    if (!score) {
      Alert.alert(t('orderDetail.selectStars'), t('tracking.selectStarsMessage'));
      return;
    }
    rateMutation.mutate(
      { score, comment: comment.trim() || null },
      {
        onSuccess: () => setSubmitted(true),
        onError: () => Alert.alert(t('error.generic'), t('tracking.ratingError')),
      },
    );
  };

  if (submitted) {
    return (
      <View className="mx-5 mt-4 items-center gap-2 rounded-2xl bg-gold-100 px-4 py-5">
        <Ionicons name="checkmark-circle" size={36} color="#D4A62A" />
        <Text className="text-base font-extrabold text-ink">{t('tracking.ratingThankYou')}</Text>
        <View className="flex-row gap-0.5">
          {[1, 2, 3, 4, 5].map((n) => (
            <Ionicons key={n} name={n <= score ? 'star' : 'star-outline'} size={20} color={n <= score ? '#D4A62A' : '#D2C8B2'} />
          ))}
        </View>
      </View>
    );
  }

  return (
    <View className="mx-5 mt-4 rounded-2xl bg-white p-4">
      <Text className="mb-3 text-base font-extrabold text-ink">{t('tracking.rateExperience')}</Text>
      <View className="mb-3 flex-row justify-center gap-1">
        {[1, 2, 3, 4, 5].map((n) => (
          <Pressable
            key={n}
            onPress={() => setScore(n)}
            hitSlop={8}
            accessibilityRole="radio"
            accessibilityState={{ selected: n <= score }}
            accessibilityLabel={t('a11y.starRating', { n })}
          >
            <Ionicons name={n <= score ? 'star' : 'star-outline'} size={30} color={n <= score ? '#D4A62A' : '#D2C8B2'} />
          </Pressable>
        ))}
      </View>
      <TextInput
        className="mb-3 rounded-xl border border-cream-300 bg-cream-50 px-3 py-2.5 text-sm text-ink"
        placeholder={t('tracking.commentPlaceholder')}
        placeholderTextColor="#A8A493"
        value={comment}
        onChangeText={setComment}
        multiline
        maxLength={500}
        textAlignVertical="top"
        editable={!rateMutation.isPending}
        accessibilityLabel="Rating comment"
      />
      <Pressable
        onPress={handleSubmit}
        disabled={rateMutation.isPending}
        accessibilityRole="button"
        accessibilityLabel={t('a11y.submitRating')}
        accessibilityState={{ disabled: rateMutation.isPending }}
        className={`items-center rounded-2xl py-3 ${score > 0 ? 'bg-gold-400' : 'bg-cream-300'}`}
      >
        <Text className={`text-sm font-extrabold ${score > 0 ? 'text-olive-900' : 'text-ink-faint'}`}>
          {rateMutation.isPending ? t('tracking.submitting') : t('tracking.submitRating')}
        </Text>
      </Pressable>
    </View>
  );
}

// ── Live order tracking (real UUID, full lifecycle) ───────────────────────────

function LiveOrderTracking({ id }: { id: string }) {
  const { t } = useTranslation();
  const { data: order, isLoading: orderLoading, isError, refetch } = useOrderDetail(id);
  const { data: history, isLoading: histLoading } = useOrderTracking(id);

  if (orderLoading || histLoading) return <ScreenLoader />;
  if (isError || !order) return <ErrorState onRetry={() => void refetch()} />;

  const times: Partial<Record<OrderStatus, string>> = {};
  (history ?? []).forEach((e) => {
    times[e.toStatus as OrderStatus] = formatDateTime(e.changedAt);
  });
  const currentRank = ORDER_RANK[order.status] ?? 0;
  const timeline = buildTimeline(ORDER_STEPS, ORDER_RANK, currentRank, times);

  const banner = order.readyAt
    ? `Ready by ${formatDateTime(order.readyAt)}`
    : order.status === 'delivered'
      ? 'Delivered'
      : 'In process';

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 40 }}>
        <Header orderNumber={order.orderNumber} banner={banner} />

        <Text className="mx-5 mb-4 mt-7 text-lg font-extrabold text-ink">{t('tracking.timeline')}</Text>
        <View className="mx-5">
          {timeline.map((row, i) => (
            <TimelineNode key={row.label} row={row} isLast={i === timeline.length - 1} />
          ))}
        </View>

        {/* Garments */}
        {order.items && order.items.length > 0 ? (
          <>
            <Text className="mx-5 mb-3 mt-4 text-lg font-extrabold text-ink">
              {t('tracking.garments', { count: order.items.reduce((n, it) => n + it.quantity, 0) })}
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

        {/* Rating prompt — only for delivered orders */}
        {order.status === 'delivered' || order.status === 'closed' ? (
          <TrackingRatingWidget orderId={order.id} existingRating={order.rating} />
        ) : null}
      </ScrollView>
    </SafeAreaView>
  );
}

// ── Root ──────────────────────────────────────────────────────────────────────

export default function OrderTrackingScreen() {
  const { id, kind } = useLocalSearchParams<{ id: string; kind?: string }>();
  const confirmed = useBookingStore((s) => s.confirmed);
  const safeId = id ?? '';

  if (!UUID_RE.test(safeId)) {
    // Legacy local-flow id (LG-#####) or empty
    return <DemoTracking orderNumber={safeId || 'LG-00000'} />;
  }

  // Determine whether this UUID is a pickup-request or an order.
  // Priority: explicit `kind` param > bookingStore.confirmed.pickupRequestId match > default order.
  const isPickup =
    kind === 'pickup' ||
    (kind !== 'order' && confirmed?.pickupRequestId === safeId);

  if (isPickup) {
    return <PickupTracking id={safeId} />;
  }
  return <LiveOrderTracking id={safeId} />;
}
