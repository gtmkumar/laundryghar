/**
 * Today's Assignments tab
 *
 * Wired to: GET {Logistics}/api/v1/rider/assignments/today
 * Returns a flat list of RiderAssignmentDto for today's shift.
 *
 * Tap a row → push /(app)/assignments/[id] for status update.
 */
import React from 'react';
import {
  FlatList,
  Pressable,
  RefreshControl,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useTodaysAssignments } from '@/hooks/useRider';
import { useAuthStore } from '@/store/authStore';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { EmptyState } from '@/components/ui/EmptyState';
import type { RiderAssignmentDto, RiderAssignmentStatus } from '@/types/api';

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

  return (
    <SafeAreaView className="flex-1 bg-surface-muted">
      {/* Header */}
      <View className="bg-brand-700 px-6 pb-6 pt-5">
        <Text className="text-sm font-medium text-brand-200">Welcome back,</Text>
        <Text className="text-xl font-bold text-white">
          {rider?.riderCode ?? 'Rider'}
        </Text>
        <Text className="mt-1 text-sm text-brand-200">{today}</Text>
      </View>

      <FlatList
        data={assignments}
        keyExtractor={(a) => a.id}
        renderItem={({ item }) => <AssignmentCard item={item} />}
        contentContainerStyle={{ padding: 16, paddingBottom: 32 }}
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
          <EmptyState
            title="No assignments today"
            message="You have no shifts scheduled for today. Check back later or contact your manager."
          />
        }
      />
    </SafeAreaView>
  );
}
