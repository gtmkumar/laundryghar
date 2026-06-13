/**
 * Order detail — summary, financials, line items, cancel + track + rate.
 *   GET  {Orders}/customer/orders/{id}
 *   POST {Orders}/customer/orders/{id}/cancel
 *   POST {Orders}/customer/orders/{id}/rate
 */
import React, { useState } from 'react';
import { Alert, Pressable, ScrollView, Text, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import {
  useOrderDetail,
  useCancelOrder,
  useRateOrder,
  useRateRider,
} from '@/hooks/useOrders';
import { ApiError } from '@/api/client';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { Button } from '@/components/ui/Button';
import { Badge } from '@/components/ui/Badge';
import { rupees, formatDateTime } from '@/lib/format';
import type { OrderDto, OrderStatus } from '@/types/api';

const CANCELLABLE: OrderStatus[] = ['placed', 'pickup_scheduled'];
const RATEABLE: OrderStatus[]    = ['delivered', 'closed'];

function Row({ label, value }: { label: string; value: string }) {
  return (
    <View className="flex-row items-start justify-between py-2.5">
      <Text className="flex-1 text-sm text-ink-muted">{label}</Text>
      <Text className="flex-1 text-right text-sm font-bold text-ink">{value}</Text>
    </View>
  );
}

function Items({ order }: { order: OrderDto }) {
  const { t } = useTranslation();
  if (!order.items?.length) return null;
  return (
    <View className="mx-6 mt-4 rounded-3xl bg-white px-4 py-1">
      <Text className="py-3 text-xs font-bold uppercase tracking-wider text-ink-faint">
        {t('orderDetail.items')}
      </Text>
      {order.items.map((item) => (
        <View
          key={item.id}
          className="flex-row justify-between border-t border-cream-200 py-3"
        >
          <View className="mr-2 flex-1">
            <Text className="text-sm font-bold text-ink">{item.itemName}</Text>
            <Text className="text-xs text-ink-muted">{item.serviceName}</Text>
          </View>
          <View className="items-end">
            <Text className="text-xs text-ink-muted">×{item.quantity}</Text>
            <Text className="text-sm font-bold text-ink">
              {rupees(item.lineTotal)}
            </Text>
          </View>
        </View>
      ))}
    </View>
  );
}

// ── Rating widget ─────────────────────────────────────────────────────────────

function StarRow({
  score,
  onSelect,
  disabled,
}: {
  score: number;
  onSelect: (n: number) => void;
  disabled: boolean;
}) {
  const { t } = useTranslation();
  return (
    <View className="flex-row gap-1">
      {[1, 2, 3, 4, 5].map((n) => (
        <Pressable
          key={n}
          onPress={() => !disabled && onSelect(n)}
          hitSlop={6}
          accessibilityLabel={t('a11y.starRating', { n })}
          accessibilityRole="radio"
          accessibilityState={{ selected: n <= score }}
        >
          <Ionicons
            name={n <= score ? 'star' : 'star-outline'}
            size={32}
            color={n <= score ? '#D4A62A' : '#D2C8B2'}
          />
        </Pressable>
      ))}
    </View>
  );
}

interface RatingCardProps {
  order: OrderDto;
}

function RatingCard({ order }: RatingCardProps) {
  const { t } = useTranslation();
  const [score, setScore] = useState<number>(order.rating ?? 0);
  const [comment, setComment] = useState<string>(order.ratingComment ?? '');
  const [submitted, setSubmitted] = useState(!!order.rating);

  const rateMutation = useRateOrder(order.id);

  const handleSubmit = () => {
    if (!score) {
      Alert.alert(t('orderDetail.selectStars'), t('orderDetail.selectStarsMessage'));
      return;
    }
    rateMutation.mutate(
      { score, comment: comment.trim() || null },
      {
        onSuccess: () => setSubmitted(true),
        onError: (err) =>
          Alert.alert(
            t('error.generic'),
            err instanceof Error ? err.message : t('orderDetail.cancelError'),
          ),
      },
    );
  };

  if (submitted) {
    return (
      <View className="mx-6 mt-4 items-center gap-2 rounded-3xl bg-gold-100 px-4 py-6">
        <Ionicons name="checkmark-circle" size={40} color="#D4A62A" />
        <Text className="text-base font-extrabold text-ink">{t('orderDetail.ratingThankYou')}</Text>
        <Text className="text-center text-sm text-ink-muted">
          {t('orderDetail.ratingFeedback')}
        </Text>
        <View className="mt-1 flex-row gap-0.5">
          {[1, 2, 3, 4, 5].map((n) => (
            <Ionicons
              key={n}
              name={n <= score ? 'star' : 'star-outline'}
              size={20}
              color={n <= score ? '#D4A62A' : '#D2C8B2'}
            />
          ))}
        </View>
      </View>
    );
  }

  return (
    <View
      className="mx-6 mt-4 rounded-3xl bg-white px-4 py-5"
      style={{
        shadowColor: '#2E351C',
        shadowOpacity: 0.05,
        shadowRadius: 10,
        shadowOffset: { width: 0, height: 3 },
        elevation: 2,
      }}
    >
      <Text className="mb-3 text-base font-extrabold text-ink">
        {t('orderDetail.rateExperience')}
      </Text>
      <View className="items-center gap-4">
        <StarRow
          score={score}
          onSelect={setScore}
          disabled={rateMutation.isPending}
        />
        <TextInput
          className="w-full rounded-2xl border border-cream-300 bg-cream-50 px-4 py-3 text-sm text-ink"
          placeholder={t('orderDetail.ratingCommentPlaceholder')}
          placeholderTextColor="#A8A493"
          value={comment}
          onChangeText={setComment}
          multiline
          numberOfLines={3}
          textAlignVertical="top"
          maxLength={500}
          editable={!rateMutation.isPending}
          accessibilityLabel="Rating comment"
        />
        <Button
          title={rateMutation.isPending ? t('orderDetail.submitting') : t('orderDetail.submitRating')}
          fullWidth
          loading={rateMutation.isPending}
          onPress={handleSubmit}
          variant="primary"
        />
      </View>
    </View>
  );
}

// ── Rider rating widget ───────────────────────────────────────────────────────

function RiderRatingCard({ order }: { order: OrderDto }) {
  const { t } = useTranslation();
  const [score, setScore] = useState(0);
  const [comment, setComment] = useState('');
  // null = unknown, true = rated, false = backend says no rider on this order
  const [result, setResult] = useState<{ average: number; count: number } | null>(
    null,
  );
  const [noRider, setNoRider] = useState(false);

  const rateRiderMutation = useRateRider(order.id);

  // The customer order DTO does not expose rider identity, so we optimistically
  // offer rider rating on delivered/closed orders and let the backend tell us
  // (422 NoRider) when there is in fact no rider to rate.
  if (noRider) return null;

  const handleSubmit = () => {
    if (!score) {
      Alert.alert(t('orderDetail.selectStars'), t('orderDetail.selectStarsMessage'));
      return;
    }
    rateRiderMutation.mutate(
      { score, comment: comment.trim() || null },
      {
        onSuccess: (res) =>
          setResult({ average: res.riderAverage, count: res.riderCount }),
        onError: (err) => {
          // 422 from the backend (no rider / not delivered) → unwrapSingle throws
          // an ApiError with status=false. Hide the widget rather than nag.
          if (err instanceof ApiError) {
            setNoRider(true);
            return;
          }
          Alert.alert(t('error.generic'), err.message || t('orderDetail.cancelError'));
        },
      },
    );
  };

  if (result) {
    return (
      <View className="mx-6 mt-4 items-center gap-2 rounded-3xl bg-olive-100 px-4 py-6">
        <Ionicons name="bicycle" size={36} color="#5C6A33" />
        <Text className="text-base font-extrabold text-ink">
          {t('orderDetail.rateRiderThankYou')}
        </Text>
        <View className="mt-1 flex-row gap-0.5">
          {[1, 2, 3, 4, 5].map((n) => (
            <Ionicons
              key={n}
              name={n <= score ? 'star' : 'star-outline'}
              size={20}
              color={n <= score ? '#D4A62A' : '#D2C8B2'}
            />
          ))}
        </View>
        <Text className="text-center text-sm text-ink-muted">
          {t('orderDetail.rateRiderAverage', {
            average: result.average.toFixed(1),
            count: result.count,
          })}
        </Text>
      </View>
    );
  }

  return (
    <View
      className="mx-6 mt-4 rounded-3xl bg-white px-4 py-5"
      style={{
        shadowColor: '#2E351C',
        shadowOpacity: 0.05,
        shadowRadius: 10,
        shadowOffset: { width: 0, height: 3 },
        elevation: 2,
      }}
    >
      <View className="mb-1 flex-row items-center gap-2">
        <Ionicons name="bicycle-outline" size={18} color="#5C6A33" />
        <Text className="text-base font-extrabold text-ink">
          {t('orderDetail.rateRiderTitle')}
        </Text>
      </View>
      <Text className="mb-3 text-sm text-ink-muted">
        {t('orderDetail.rateRiderSubtitle')}
      </Text>
      <View className="items-center gap-4">
        <StarRow
          score={score}
          onSelect={setScore}
          disabled={rateRiderMutation.isPending}
        />
        <TextInput
          className="w-full rounded-2xl border border-cream-300 bg-cream-50 px-4 py-3 text-sm text-ink"
          placeholder={t('orderDetail.rateRiderCommentPlaceholder')}
          placeholderTextColor="#A8A493"
          value={comment}
          onChangeText={setComment}
          multiline
          numberOfLines={3}
          textAlignVertical="top"
          maxLength={500}
          editable={!rateRiderMutation.isPending}
          accessibilityLabel={t('orderDetail.rateRiderCommentPlaceholder')}
        />
        <Button
          title={
            rateRiderMutation.isPending
              ? t('orderDetail.submitting')
              : t('orderDetail.submitRating')
          }
          fullWidth
          loading={rateRiderMutation.isPending}
          onPress={handleSubmit}
          variant="olive"
        />
      </View>
    </View>
  );
}

// ── Screen ────────────────────────────────────────────────────────────────────

export default function OrderDetailScreen() {
  const { t } = useTranslation();
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const { data: order, isLoading, isError, refetch } = useOrderDetail(id ?? '');
  const cancelMutation = useCancelOrder();

  if (isLoading) return <ScreenLoader />;
  if (isError || !order) return <ErrorState onRetry={() => void refetch()} />;

  const canCancel  = CANCELLABLE.includes(order.status);
  const canTrack   = !['delivered', 'cancelled', 'returned'].includes(order.status);
  const canRate    = RATEABLE.includes(order.status);
  const alreadyRated = !!order.rating;

  const handleCancel = () => {
    Alert.alert(t('orderDetail.cancelConfirm'), t('orderDetail.cancelMessage'), [
      { text: t('orderDetail.keepOrder'), style: 'cancel' },
      {
        text: t('orderDetail.cancelOrder'),
        style: 'destructive',
        onPress: () =>
          cancelMutation.mutate(order.id, {
            onSuccess: () => Alert.alert(t('orderDetail.cancelled'), t('orderDetail.cancelSuccess')),
            onError: (err) =>
              Alert.alert(t('error.generic'), err instanceof Error ? err.message : t('orderDetail.cancelError')),
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
          accessibilityRole="button"
          accessibilityLabel={t('a11y.back')}
        >
          <Ionicons name="chevron-back" size={22} color="#3C3F35" />
        </Pressable>
        <Text className="text-lg font-extrabold text-ink">
          #{order.orderNumber}
        </Text>
      </View>

      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={{ paddingBottom: 40 }}
      >
        {/* Summary */}
        <View className="mx-6 mt-2 rounded-3xl bg-white px-4 py-1">
          <View className="flex-row items-center justify-between py-3">
            <Text className="text-sm text-ink-muted">{t('orderDetail.status')}</Text>
            <Badge label={order.status.replace(/_/g, ' ')} tone="gold" />
          </View>
          <View className="h-px bg-cream-200" />
          <Row label={t('orderDetail.placed')} value={formatDateTime(order.placedAt)} />
          {order.deliveredAt ? (
            <Row label={t('orderDetail.delivered')} value={formatDateTime(order.deliveredAt)} />
          ) : null}
          {order.cancelledAt ? (
            <Row label={t('orderDetail.cancelled')} value={formatDateTime(order.cancelledAt)} />
          ) : null}
        </View>

        {/* Financials */}
        <View className="mx-6 mt-4 rounded-3xl bg-white px-4 py-1">
          <Row label={t('orderDetail.subtotal')} value={rupees(order.subtotal)} />
          {order.discountTotal > 0 ? (
            <Row label={t('orderDetail.discount')} value={`−${rupees(order.discountTotal)}`} />
          ) : null}
          {order.taxTotal > 0 ? (
            <Row label={t('orderDetail.tax')} value={rupees(order.taxTotal)} />
          ) : null}
          <View className="flex-row justify-between border-t border-cream-200 py-3">
            <Text className="text-base font-extrabold text-ink">{t('orderDetail.total')}</Text>
            <Text className="text-base font-extrabold text-olive-700">
              {rupees(order.grandTotal)}
            </Text>
          </View>
        </View>

        <Items order={order} />

        {/* Rating — show for delivered orders */}
        {canRate ? (
          <RatingCard order={order} />
        ) : null}

        {/* Rider rating — delivered/closed orders. Self-hides if the backend
            reports there is no rider on this order (422). */}
        {canRate ? (
          <RiderRatingCard order={order} />
        ) : null}

        {/* Already rated summary when not showing the widget */}
        {!canRate && alreadyRated ? (
          <View className="mx-6 mt-4 flex-row items-center gap-2 rounded-2xl bg-gold-100 px-4 py-3">
            <Ionicons name="star" size={16} color="#D4A62A" />
            <Text className="text-sm font-semibold text-gold-800">
              {t('orderDetail.ratingPrompt', { score: order.rating })}
            </Text>
          </View>
        ) : null}

        {/* Actions */}
        <View className="mx-6 mt-6 gap-3">
          {canTrack ? (
            <Button
              title={t('orderDetail.trackOrder')}
              fullWidth
              iconRight="arrow-forward"
              onPress={() =>
                router.push(`/(app)/orders/tracking/${order.id}` as never)
              }
            />
          ) : null}
          {canCancel ? (
            <Button
              title={t('orderDetail.cancelOrder')}
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
