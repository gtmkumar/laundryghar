/**
 * Skeleton — Reanimated shimmer placeholder for content-shaped loading states.
 *
 * Usage:
 *   <Skeleton width="100%" height={20} borderRadius={8} />
 *   <Skeleton style={{ width: 44, height: 44, borderRadius: 22 }} />
 *
 * The shimmer animates a linear gradient sweep from left to right repeatedly,
 * achieving the standard skeleton-loader effect without installing extra libs.
 * Reanimated 3 worklets run on the UI thread so this is 60 fps even on JS-
 * thread-heavy screens.
 */
import React, { useEffect } from 'react';
import { StyleSheet, View, type ViewStyle } from 'react-native';
import Animated, {
  interpolate,
  useAnimatedStyle,
  useSharedValue,
  withRepeat,
  withTiming,
} from 'react-native-reanimated';
import { LinearGradient } from 'expo-linear-gradient';

interface SkeletonProps {
  width?:        number | `${number}%` | 'auto';
  height?:       number;
  borderRadius?: number;
  style?:        ViewStyle;
}

const SHIMMER_DURATION = 1200;

const BASE_COLOR     = '#E8E2D6';
const SHIMMER_COLOR  = '#F3EDE1';
const SHIMMER_COLOR2 = '#EDE7DB';

export function Skeleton({ width, height, borderRadius = 8, style }: SkeletonProps) {
  const progress = useSharedValue(0);

  useEffect(() => {
    progress.value = withRepeat(
      withTiming(1, { duration: SHIMMER_DURATION }),
      -1,    // repeat forever
      false, // no reverse — sweep always goes left→right
    );
  }, [progress]);

  const animatedStyle = useAnimatedStyle(() => ({
    transform: [
      {
        translateX: interpolate(progress.value, [0, 1], [-300, 300]),
      },
    ],
  }));

  const containerStyle: ViewStyle = {
    backgroundColor: BASE_COLOR,
    overflow:        'hidden',
    borderRadius,
    width:           width as ViewStyle['width'],
    height,
    ...style,
  };

  return (
    <View style={containerStyle}>
      <Animated.View style={[StyleSheet.absoluteFill, animatedStyle]}>
        <LinearGradient
          colors={[BASE_COLOR, SHIMMER_COLOR, SHIMMER_COLOR2, BASE_COLOR]}
          start={{ x: 0, y: 0 }}
          end={{ x: 1, y: 0 }}
          style={StyleSheet.absoluteFill}
        />
      </Animated.View>
    </View>
  );
}

// ---------------------------------------------------------------------------
// Pre-composed skeleton layouts for specific screens.
// ---------------------------------------------------------------------------

/** Home screen skeleton — matches the duty circle + checklist + task pill layout */
export function HomeScreenSkeleton() {
  return (
    <View style={{ flex: 1, backgroundColor: '#F3EEE3', paddingHorizontal: 20 }}>
      {/* header row */}
      <View style={{ flexDirection: 'row', alignItems: 'center', paddingTop: 16, gap: 12 }}>
        <Skeleton width={44} height={44} borderRadius={22} />
        <View style={{ gap: 6, flex: 1 }}>
          <Skeleton width={80} height={12} />
          <Skeleton width={140} height={18} />
        </View>
        <Skeleton width={44} height={44} borderRadius={12} />
      </View>

      {/* duty circle */}
      <View style={{ alignItems: 'center', paddingVertical: 40 }}>
        <Skeleton width={200} height={200} borderRadius={100} />
        <View style={{ marginTop: 16 }}>
          <Skeleton width={180} height={14} />
        </View>
      </View>

      {/* checklist card */}
      <View style={{ backgroundColor: '#FFFFFF', borderRadius: 24, padding: 20, gap: 14, marginBottom: 16 }}>
        <Skeleton width={120} height={12} />
        {[1, 2, 3, 4].map((i) => (
          <View key={i} style={{ flexDirection: 'row', alignItems: 'center', gap: 12 }}>
            <Skeleton width={24} height={24} borderRadius={12} />
            <Skeleton width={140} height={14} />
          </View>
        ))}
      </View>

      {/* tasks pill */}
      <Skeleton width="100%" height={64} borderRadius={16} />
    </View>
  );
}

/** Tasks list screen skeleton — header stats + 3 task cards */
export function TasksListSkeleton() {
  return (
    <View style={{ flex: 1, backgroundColor: '#F3EEE3', paddingHorizontal: 20 }}>
      {/* stat tiles */}
      <View style={{ flexDirection: 'row', gap: 16, paddingTop: 16, paddingBottom: 20 }}>
        {[1, 2, 3].map((i) => (
          <View key={i} style={{ flex: 1, alignItems: 'center', gap: 6 }}>
            <Skeleton width={50} height={22} />
            <Skeleton width={40} height={12} />
          </View>
        ))}
      </View>

      {/* tab strip */}
      <View style={{ flexDirection: 'row', gap: 8, marginBottom: 16 }}>
        <Skeleton width={90} height={36} borderRadius={18} />
        <Skeleton width={90} height={36} borderRadius={18} />
      </View>

      {/* task cards */}
      {[1, 2, 3].map((i) => (
        <View
          key={i}
          style={{
            backgroundColor: '#FFFFFF',
            borderRadius: 24,
            padding: 16,
            marginBottom: 12,
            gap: 10,
          }}
        >
          <View style={{ flexDirection: 'row', gap: 8, alignItems: 'center' }}>
            <Skeleton width={24} height={24} borderRadius={8} />
            <Skeleton width={80} height={14} />
            <View style={{ flex: 1 }} />
            <Skeleton width={50} height={22} borderRadius={8} />
          </View>
          <Skeleton width={160} height={18} />
          <Skeleton width={200} height={12} />
          <View style={{ flexDirection: 'row', gap: 12 }}>
            <Skeleton width={60} height={12} />
            <Skeleton width={60} height={12} />
            <View style={{ flex: 1 }} />
            <Skeleton width={40} height={14} />
          </View>
        </View>
      ))}
    </View>
  );
}
