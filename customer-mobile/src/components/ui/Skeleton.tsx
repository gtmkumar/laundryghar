/**
 * Skeleton loader — reanimated shimmer for content-shaped placeholders.
 * MOB-10: Replaces full-screen ScreenLoader on home/my-orders tab screens.
 *
 * Usage:
 *   <Skeleton width={200} height={20} borderRadius={8} />
 *   <Skeleton width="100%" height={120} borderRadius={16} />
 *   <SkeletonHomeScreen />
 *   <SkeletonOrderList />
 */
import React, { useEffect } from 'react';
import { View } from 'react-native';
import Animated, {
  interpolate,
  useAnimatedStyle,
  useSharedValue,
  withRepeat,
  withTiming,
  Easing,
} from 'react-native-reanimated';

// ── Primitive ─────────────────────────────────────────────────────────────────

interface SkeletonProps {
  width: number | `${number}%` | '100%';
  height: number;
  borderRadius?: number;
  style?: object;
}

const SHIMMER_DURATION = 1200;

export function Skeleton({ width, height, borderRadius = 8, style }: SkeletonProps) {
  const progress = useSharedValue(0);

  useEffect(() => {
    progress.value = withRepeat(
      withTiming(1, { duration: SHIMMER_DURATION, easing: Easing.inOut(Easing.quad) }),
      -1,
      true,
    );
  }, [progress]);

  const animatedStyle = useAnimatedStyle(() => ({
    opacity: interpolate(progress.value, [0, 1], [0.35, 0.7]),
  }));

  return (
    <Animated.View
      style={[
        {
          width: width as number,
          height,
          borderRadius,
          backgroundColor: '#D2C8B2',
        },
        animatedStyle,
        style,
      ]}
    />
  );
}

// ── Home skeleton ─────────────────────────────────────────────────────────────

export function SkeletonHomeScreen() {
  return (
    <View style={{ flex: 1, backgroundColor: '#F3EEE3', paddingHorizontal: 24, paddingTop: 16 }}>
      {/* Header row */}
      <View style={{ flexDirection: 'row', alignItems: 'center', marginBottom: 20 }}>
        <Skeleton width={40} height={40} borderRadius={12} />
        <View style={{ marginLeft: 12 }}>
          <Skeleton width={80} height={12} borderRadius={6} style={{ marginBottom: 6 }} />
          <Skeleton width={120} height={18} borderRadius={8} />
        </View>
      </View>
      {/* Address chip */}
      <Skeleton width={180} height={14} borderRadius={7} style={{ marginBottom: 20 }} />
      {/* Promo banner */}
      <Skeleton width="100%" height={150} borderRadius={24} style={{ marginBottom: 28 }} />
      {/* Section title */}
      <Skeleton width={120} height={18} borderRadius={8} style={{ marginBottom: 16 }} />
      {/* Service tiles row */}
      <View style={{ flexDirection: 'row', justifyContent: 'space-between', marginBottom: 28 }}>
        {[0, 1, 2, 3].map((i) => (
          <View key={i} style={{ alignItems: 'center', width: '22%' }}>
            <Skeleton width={60} height={60} borderRadius={16} style={{ marginBottom: 8 }} />
            <Skeleton width={44} height={10} borderRadius={5} />
          </View>
        ))}
      </View>
      <View style={{ flexDirection: 'row', justifyContent: 'space-between' }}>
        {[0, 1, 2, 3].map((i) => (
          <View key={i} style={{ alignItems: 'center', width: '22%' }}>
            <Skeleton width={60} height={60} borderRadius={16} style={{ marginBottom: 8 }} />
            <Skeleton width={44} height={10} borderRadius={5} />
          </View>
        ))}
      </View>
      {/* CTA card */}
      <Skeleton width="100%" height={130} borderRadius={24} style={{ marginTop: 24 }} />
    </View>
  );
}

// ── Order list skeleton ───────────────────────────────────────────────────────

function SkeletonOrderCard() {
  return (
    <View
      style={{
        backgroundColor: '#FFFFFF',
        borderRadius: 20,
        padding: 16,
        marginBottom: 12,
      }}
    >
      <View style={{ flexDirection: 'row', alignItems: 'center', marginBottom: 12 }}>
        <Skeleton width={36} height={36} borderRadius={10} />
        <View style={{ marginLeft: 12, flex: 1 }}>
          <Skeleton width={100} height={14} borderRadius={6} style={{ marginBottom: 6 }} />
          <Skeleton width={160} height={12} borderRadius={5} />
        </View>
        <Skeleton width={60} height={22} borderRadius={11} />
      </View>
      <Skeleton width="100%" height={1} borderRadius={0} style={{ backgroundColor: '#E8E3D8', marginBottom: 12 }} />
      <View style={{ flexDirection: 'row', justifyContent: 'space-between' }}>
        <Skeleton width={80} height={12} borderRadius={5} />
        <Skeleton width={80} height={32} borderRadius={12} />
      </View>
    </View>
  );
}

export function SkeletonOrderList() {
  return (
    <View style={{ flex: 1, backgroundColor: '#F3EEE3', paddingHorizontal: 20, paddingTop: 16 }}>
      <Skeleton width={160} height={22} borderRadius={10} style={{ marginBottom: 20 }} />
      {[0, 1, 2, 3].map((i) => (
        <SkeletonOrderCard key={i} />
      ))}
    </View>
  );
}
