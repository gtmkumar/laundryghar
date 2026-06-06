/**
 * Offers screen — lists the customer's available coupons.
 *
 * Route:  /(app)/offers
 * Params: couponId? (string) — when present, the matching coupon row is
 *         highlighted so the user immediately sees the offer that was
 *         promoted on the banner they tapped.
 *
 * Data:   GET {Commerce}/api/v1/customer/coupons  (Bearer token; auto-attached)
 */
import React, { useCallback, useEffect, useRef } from 'react';
import {
  FlatList,
  RefreshControl,
  ScrollView,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams } from 'expo-router';
import { useCoupons } from '@/hooks/useCommerce';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { EmptyState } from '@/components/ui/EmptyState';
import type { CouponDto } from '@/types/api';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function formatDiscount(coupon: CouponDto): string {
  if (coupon.couponType === 'percent') {
    const suffix =
      coupon.maxDiscountAmount != null ? ` (up to ₹${coupon.maxDiscountAmount.toFixed(0)})` : '';
    return `${coupon.discountValue}% off${suffix}`;
  }
  return `₹${coupon.discountValue.toFixed(0)} off`;
}

function formatExpiry(validUntil?: string | null): string | null {
  if (!validUntil) return null;
  try {
    return `Expires ${new Date(validUntil).toLocaleDateString('en-IN', {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    })}`;
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// CouponCard
// ---------------------------------------------------------------------------

interface CouponCardProps {
  coupon: CouponDto;
  highlighted: boolean;
}

function CouponCard({ coupon, highlighted }: CouponCardProps) {
  const expiry = formatExpiry(coupon.validUntil);
  const discount = formatDiscount(coupon);

  return (
    <View
      accessibilityRole="text"
      accessibilityLabel={`Coupon ${coupon.code}, ${discount}${coupon.description ? `, ${coupon.description}` : ''}${expiry ? `, ${expiry}` : ''}`}
      className={[
        'mb-3 rounded-2xl border bg-white p-4',
        highlighted
          ? 'border-brand-700 shadow-md'
          : 'border-gray-200',
      ].join(' ')}
      style={{ elevation: highlighted ? 4 : 1 }}
    >
      {/* Top row: code + discount badge */}
      <View className="flex-row items-center justify-between mb-2">
        <View className="flex-row items-center gap-2">
          {highlighted && (
            <View className="rounded-full bg-brand-700 px-2 py-0.5">
              <Text className="text-xs font-bold text-white">Featured</Text>
            </View>
          )}
          <Text
            className="font-mono text-base font-bold tracking-widest text-gray-900"
            selectable
          >
            {coupon.code}
          </Text>
        </View>
        <View className="rounded-full bg-green-100 px-3 py-1">
          <Text className="text-sm font-bold text-green-700">{discount}</Text>
        </View>
      </View>

      {/* Description */}
      {coupon.description ? (
        <Text className="text-sm text-gray-600 mb-2">{coupon.description}</Text>
      ) : null}

      {/* Footer row: min-order + expiry */}
      <View className="flex-row flex-wrap gap-x-4 gap-y-1">
        {coupon.minOrderValue != null && coupon.minOrderValue > 0 ? (
          <Text className="text-xs text-gray-400">
            Min. order ₹{coupon.minOrderValue.toFixed(0)}
          </Text>
        ) : null}
        {expiry ? (
          <Text className="text-xs text-gray-400">{expiry}</Text>
        ) : null}
      </View>
    </View>
  );
}

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

export default function OffersScreen() {
  // couponId may arrive as a string or string[] from expo-router params
  const params = useLocalSearchParams<{ couponId?: string }>();
  const targetCouponId = Array.isArray(params.couponId)
    ? params.couponId[0]
    : (params.couponId ?? null);

  const { data: coupons, isLoading, isError, refetch, isFetching } = useCoupons();

  // Scroll-to-highlighted: obtain a ref to the FlatList and auto-scroll once
  // data loads when a targetCouponId is present.
  const flatRef = useRef<FlatList<CouponDto>>(null);
  const hasScrolled = useRef(false);

  const scrollToTarget = useCallback(() => {
    if (!targetCouponId || !coupons || hasScrolled.current) return;
    const idx = coupons.findIndex((c) => c.id === targetCouponId);
    if (idx > 0) {
      // Use a short delay to let the list measure before scrolling
      setTimeout(() => {
        flatRef.current?.scrollToIndex({ index: idx, animated: true, viewPosition: 0.2 });
      }, 300);
    }
    hasScrolled.current = true;
  }, [targetCouponId, coupons]);

  useEffect(() => {
    scrollToTarget();
  }, [scrollToTarget]);

  // ── States ────────────────────────────────────────────────────────────────

  if (isLoading) return <ScreenLoader />;

  if (isError) {
    return (
      <SafeAreaView className="flex-1 bg-surface-muted">
        <View className="px-6 pt-6 pb-4">
          <Text className="text-2xl font-bold text-gray-900">Offers</Text>
        </View>
        <ErrorState onRetry={() => void refetch()} />
      </SafeAreaView>
    );
  }

  const list = coupons ?? [];

  return (
    <SafeAreaView className="flex-1 bg-surface-muted">
      {/* Header */}
      <View className="px-6 pt-6 pb-4">
        <Text className="text-2xl font-bold text-gray-900">Offers</Text>
        <Text className="text-sm text-gray-500 mt-1">
          Apply a coupon code when placing your next order.
        </Text>
      </View>

      {list.length === 0 ? (
        <ScrollView
          contentContainerStyle={{ flexGrow: 1 }}
          refreshControl={
            <RefreshControl
              refreshing={isFetching && !isLoading}
              onRefresh={() => void refetch()}
              tintColor="#1D4ED8"
            />
          }
        >
          <EmptyState
            title="No offers right now"
            message="Check back soon — new coupons are added regularly."
          />
        </ScrollView>
      ) : (
        <FlatList
          ref={flatRef}
          data={list}
          keyExtractor={(c) => c.id}
          renderItem={({ item }) => (
            <CouponCard
              coupon={item}
              highlighted={item.id === targetCouponId}
            />
          )}
          contentContainerStyle={{ paddingHorizontal: 24, paddingBottom: 32 }}
          showsVerticalScrollIndicator={false}
          onScrollToIndexFailed={() => {
            // Fallback: scroll to end so at least the list is visible
            flatRef.current?.scrollToEnd({ animated: true });
          }}
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
