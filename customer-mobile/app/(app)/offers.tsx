/**
 * Offers — the customer's available coupons.
 * Route param `couponId` highlights & scrolls to a banner-promoted offer.
 *   GET {Commerce}/customer/coupons
 */
import React, { useCallback, useEffect, useRef } from 'react';
import { FlatList, Pressable, RefreshControl, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useCoupons } from '@/hooks/useCommerce';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { EmptyState } from '@/components/ui/EmptyState';
import { Badge } from '@/components/ui/Badge';
import { rupees, formatDate } from '@/lib/format';
import type { CouponDto } from '@/types/api';

function formatDiscount(coupon: CouponDto): string {
  if (coupon.couponType === 'percent') {
    const suffix = coupon.maxDiscountAmount != null ? ` (up to ${rupees(coupon.maxDiscountAmount)})` : '';
    return `${coupon.discountValue}% off${suffix}`;
  }
  return `${rupees(coupon.discountValue)} off`;
}

function CouponCard({ coupon, highlighted }: { coupon: CouponDto; highlighted: boolean }) {
  return (
    <View
      className={`mb-3 overflow-hidden rounded-3xl bg-white ${highlighted ? 'border-2 border-gold-400' : ''}`}
      style={{ shadowColor: '#2E351C', shadowOpacity: highlighted ? 0.12 : 0.04, shadowRadius: 10, shadowOffset: { width: 0, height: 3 }, elevation: highlighted ? 4 : 1 }}
    >
      <View className="flex-row">
        {/* Stub */}
        <View className="w-16 items-center justify-center bg-olive-700">
          <Ionicons name="pricetag" size={22} color="#E6C260" />
        </View>
        <View className="flex-1 p-4">
          <View className="mb-1.5 flex-row items-center gap-2">
            {highlighted ? <Badge label="Featured" tone="gold" /> : null}
            <Text className="font-mono text-base font-extrabold tracking-widest text-ink" selectable>
              {coupon.code}
            </Text>
          </View>
          <Text className="text-sm font-bold text-success">{formatDiscount(coupon)}</Text>
          {coupon.description ? <Text className="mt-1 text-sm text-ink-muted">{coupon.description}</Text> : null}
          <View className="mt-2 flex-row flex-wrap gap-x-4 gap-y-1">
            {coupon.minOrderValue ? (
              <Text className="text-xs text-ink-faint">Min. order {rupees(coupon.minOrderValue)}</Text>
            ) : null}
            {coupon.validUntil ? (
              <Text className="text-xs text-ink-faint">Expires {formatDate(coupon.validUntil)}</Text>
            ) : null}
          </View>
        </View>
      </View>
    </View>
  );
}

export default function OffersScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ couponId?: string }>();
  const targetCouponId = Array.isArray(params.couponId) ? params.couponId[0] : (params.couponId ?? null);

  const { data: coupons, isLoading, isError, refetch, isFetching } = useCoupons();

  const flatRef = useRef<FlatList<CouponDto>>(null);
  const hasScrolled = useRef(false);

  const scrollToTarget = useCallback(() => {
    if (!targetCouponId || !coupons || hasScrolled.current) return;
    const idx = coupons.findIndex((c) => c.id === targetCouponId);
    if (idx > 0) {
      setTimeout(() => flatRef.current?.scrollToIndex({ index: idx, animated: true, viewPosition: 0.2 }), 300);
    }
    hasScrolled.current = true;
  }, [targetCouponId, coupons]);

  useEffect(() => {
    scrollToTarget();
  }, [scrollToTarget]);

  const Header = (
    <View className="flex-row items-center gap-3 px-5 pb-2 pt-2">
      <Pressable
        onPress={() => (router.canGoBack() ? router.back() : router.replace('/(app)/(tabs)/home'))}
        className="h-10 w-10 items-center justify-center rounded-full bg-white"
        accessibilityRole="button"
        accessibilityLabel="Go back"
      >
        <Ionicons name="chevron-back" size={22} color="#3C3F35" />
      </Pressable>
      <Text className="text-xl font-extrabold text-ink">Offers</Text>
    </View>
  );

  if (isLoading) return <ScreenLoader />;

  if (isError) {
    return (
      <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
        {Header}
        <ErrorState onRetry={() => void refetch()} />
      </SafeAreaView>
    );
  }

  const list = coupons ?? [];

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      {Header}
      {list.length === 0 ? (
        <ScrollView
          contentContainerStyle={{ flexGrow: 1 }}
          refreshControl={<RefreshControl refreshing={isFetching && !isLoading} onRefresh={() => void refetch()} tintColor="#4A552A" />}
        >
          <EmptyState icon="gift-outline" title="No offers right now" message="Check back soon — new coupons are added regularly." />
        </ScrollView>
      ) : (
        <FlatList
          ref={flatRef}
          data={list}
          keyExtractor={(c) => c.id}
          renderItem={({ item }) => <CouponCard coupon={item} highlighted={item.id === targetCouponId} />}
          contentContainerStyle={{ paddingHorizontal: 20, paddingBottom: 32, paddingTop: 8 }}
          showsVerticalScrollIndicator={false}
          onScrollToIndexFailed={() => flatRef.current?.scrollToEnd({ animated: true })}
          refreshControl={<RefreshControl refreshing={isFetching && !isLoading} onRefresh={() => void refetch()} tintColor="#4A552A" />}
        />
      )}
    </SafeAreaView>
  );
}
