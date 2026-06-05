/**
 * Today's Assignments tab
 *
 * Wired to: GET {Logistics}/api/v1/rider/assignments/today
 * Returns a flat list of RiderAssignmentDto for today's shift.
 *
 * Tap a row → push /(app)/assignments/[id] for status update.
 */
import React, { useRef, useState } from 'react';
import {
  Dimensions,
  FlatList,
  Image,
  Linking,
  Pressable,
  RefreshControl,
  Text,
  View,
  ViewToken,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useTodaysAssignments } from '@/hooks/useRider';
import { useHomeBanners } from '@/hooks/useEngagement';
import { useAuthStore } from '@/store/authStore';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { EmptyState } from '@/components/ui/EmptyState';
import type { AppBannerDto, RiderAssignmentDto, RiderAssignmentStatus } from '@/types/api';

const { width: SCREEN_WIDTH } = Dimensions.get('window');

// ---------------------------------------------------------------------------
// Banner carousel — home_top CMS banners, renders nothing on failure/empty
// ---------------------------------------------------------------------------

function BannerCard({ item }: { item: AppBannerDto }) {
  const hasImage = !!item.imageUrl;
  const bgColor = item.backgroundColor ?? '#15803D'; // brand-700 green

  const handlePress = () => {
    if (item.ctaDeeplink) {
      void Linking.openURL(item.ctaDeeplink).catch(() => undefined);
    } else if (item.externalUrl) {
      void Linking.openURL(item.externalUrl).catch(() => undefined);
    }
  };

  return (
    <Pressable
      onPress={handlePress}
      accessibilityRole="button"
      accessibilityLabel={item.title ?? item.subtitle ?? 'Promotional banner'}
      style={{ width: SCREEN_WIDTH - 48 }}
      className="h-40 items-center justify-center rounded-3xl overflow-hidden active:opacity-80"
    >
      {hasImage ? (
        <Image
          source={{ uri: item.imageUrl }}
          style={{ width: '100%', height: '100%', borderRadius: 24 }}
          resizeMode="cover"
          accessibilityLabel={item.title ?? 'Banner'}
        />
      ) : (
        <View
          style={{ backgroundColor: bgColor, borderRadius: 24 }}
          className="flex-1 w-full items-center justify-center px-6"
        >
          {item.title ? (
            <Text className="text-lg font-bold text-white text-center">{item.title}</Text>
          ) : null}
          {item.subtitle ? (
            <Text className="mt-1 text-sm text-white/80 text-center">{item.subtitle}</Text>
          ) : null}
          {item.ctaText ? (
            <View className="mt-3 rounded-full bg-white/20 px-4 py-1">
              <Text className="text-sm font-semibold text-white">{item.ctaText}</Text>
            </View>
          ) : null}
        </View>
      )}
    </Pressable>
  );
}

function BannerSection() {
  const { data: banners } = useHomeBanners('home_top');
  const [currentIndex, setCurrentIndex] = useState(0);
  const flatRef = useRef<FlatList<AppBannerDto>>(null);

  const onViewableItemsChanged = useRef(
    ({ viewableItems }: { viewableItems: ViewToken[] }) => {
      if (viewableItems[0]?.index != null) {
        setCurrentIndex(viewableItems[0].index);
      }
    },
  ).current;

  // No live banners — render nothing (graceful empty fallback, never crash)
  if (!banners || banners.length === 0) return null;

  return (
    <View className="mb-4">
      <FlatList
        ref={flatRef}
        data={banners}
        keyExtractor={(b) => b.id}
        horizontal
        pagingEnabled
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={{ paddingHorizontal: 24, gap: 12 }}
        onViewableItemsChanged={onViewableItemsChanged}
        viewabilityConfig={{ viewAreaCoveragePercentThreshold: 50 }}
        renderItem={({ item }) => <BannerCard item={item} />}
      />
      {banners.length > 1 && (
        <View className="mt-3 flex-row justify-center gap-1.5">
          {banners.map((_, i) => (
            <View
              key={i}
              className={`h-1.5 rounded-full ${
                i === currentIndex ? 'w-4 bg-brand-700' : 'w-1.5 bg-gray-300'
              }`}
            />
          ))}
        </View>
      )}
    </View>
  );
}

// ---------------------------------------------------------------------------
// Status badge
// ---------------------------------------------------------------------------

const STATUS_STYLE: Record<
  RiderAssignmentStatus,
  { bg: string; text: string; label: string }
> = {
  scheduled: { bg: 'bg-gray-100',   text: 'text-gray-600',   label: 'Scheduled'  },
  active:    { bg: 'bg-green-100',  text: 'text-green-700',  label: 'Active'      },
  on_break:  { bg: 'bg-amber-100',  text: 'text-amber-700',  label: 'On Break'    },
  completed: { bg: 'bg-blue-100',   text: 'text-blue-700',   label: 'Completed'   },
  cancelled: { bg: 'bg-red-100',    text: 'text-red-700',    label: 'Cancelled'   },
};

function StatusBadge({ status }: { status: RiderAssignmentStatus }) {
  const s = STATUS_STYLE[status] ?? STATUS_STYLE.scheduled;
  return (
    <View className={`rounded-full px-3 py-1 ${s.bg}`}>
      <Text className={`text-xs font-semibold ${s.text}`}>{s.label}</Text>
    </View>
  );
}

// ---------------------------------------------------------------------------
// Assignment card
// ---------------------------------------------------------------------------

function AssignmentCard({ item }: { item: RiderAssignmentDto }) {
  const router = useRouter();

  const canTap = item.status !== 'completed' && item.status !== 'cancelled';

  return (
    <Pressable
      onPress={() => canTap && router.push(`/(app)/assignments/${item.id}`)}
      accessibilityRole="button"
      accessibilityLabel={`Assignment ${item.shiftDate} — status ${item.status}`}
      accessibilityState={{ disabled: !canTap }}
      className={[
        'mb-3 rounded-2xl bg-white p-4 shadow-sm',
        canTap ? 'active:opacity-70' : 'opacity-60',
      ].join(' ')}
      style={{ elevation: 2 }}
    >
      {/* Header row */}
      <View className="flex-row items-center justify-between mb-3">
        <Text className="text-base font-bold text-gray-900">
          {formatShift(item.shiftStart, item.shiftEnd)}
        </Text>
        <StatusBadge status={item.status as RiderAssignmentStatus} />
      </View>

      {/* Stats */}
      <View className="flex-row gap-4">
        <StatCell
          label="Pickups"
          value={`${item.completedPickups} / ${item.maxPickups}`}
        />
        <StatCell
          label="Deliveries"
          value={`${item.completedDeliveries} / ${item.maxDeliveries}`}
        />
        {item.totalDistanceKm != null && (
          <StatCell label="Distance" value={`${item.totalDistanceKm.toFixed(1)} km`} />
        )}
      </View>

      {item.notes ? (
        <Text className="mt-3 text-xs text-gray-500" numberOfLines={2}>
          {item.notes}
        </Text>
      ) : null}

      {canTap ? (
        <Text className="mt-2 text-right text-xs text-brand-600">
          Tap to update status
        </Text>
      ) : null}
    </Pressable>
  );
}

function StatCell({ label, value }: { label: string; value: string }) {
  return (
    <View className="items-center">
      <Text className="text-lg font-bold text-gray-900">{value}</Text>
      <Text className="text-xs text-gray-500">{label}</Text>
    </View>
  );
}

function formatShift(start: string, end: string): string {
  const fmt = (t: string) => {
    // "HH:mm:ss" → "HH:mm"
    return t.slice(0, 5);
  };
  return `${fmt(start)} – ${fmt(end)}`;
}

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

export default function AssignmentsScreen() {
  const { rider } = useAuthStore();
  const {
    data: assignments,
    isLoading,
    isError,
    refetch,
    isRefetching,
  } = useTodaysAssignments();

  if (isLoading) return <ScreenLoader />;
  if (isError) {
    return (
      <ErrorState
        message="Failed to load today's assignments."
        onRetry={() => void refetch()}
      />
    );
  }

  const today = new Date().toLocaleDateString('en-IN', {
    weekday: 'long',
    day:     'numeric',
    month:   'long',
  });

  const ListHeader = (
    <>
      {/* Green header band */}
      <View className="bg-brand-700 px-6 pb-8 pt-5">
        <Text className="text-sm font-medium text-brand-200">Welcome back,</Text>
        <Text className="text-xl font-bold text-white">
          {rider?.riderCode ?? 'Rider'}
        </Text>
        <Text className="mt-1 text-sm text-brand-200">{today}</Text>
      </View>

      {/* Banner carousel — overlaps the header band, renders nothing when empty/errored */}
      <View className="-mt-4">
        <BannerSection />
      </View>
    </>
  );

  return (
    <SafeAreaView className="flex-1 bg-surface-muted">
      <FlatList
        data={assignments}
        keyExtractor={(a) => a.id}
        renderItem={({ item }) => <AssignmentCard item={item} />}
        ListHeaderComponent={ListHeader}
        contentContainerStyle={{ paddingHorizontal: 16, paddingBottom: 32 }}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={isRefetching}
            onRefresh={() => void refetch()}
            tintColor="#15803D"
            colors={['#15803D']}
          />
        }
        ListEmptyComponent={
          <View className="px-4 pt-4">
            <EmptyState
              title="No assignments today"
              message="You have no shifts scheduled for today. Check back later or contact your manager."
            />
          </View>
        }
      />
    </SafeAreaView>
  );
}
