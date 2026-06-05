/**
 * Assignment Detail screen
 *
 * Wired to:
 *   GET  {Logistics}/api/v1/rider/assignments/today  (re-uses cached list)
 *   PATCH {Logistics}/api/v1/rider/assignments/{id}/status
 *         body: { status: RiderAssignmentStatus }
 *
 * Status flow a rider can trigger:
 *   scheduled → active       (start shift)
 *   active    → on_break     (take a break)
 *   on_break  → active       (resume)
 *   active    → completed    (end shift)
 *   active    → cancelled    (abandon — shows confirmation)
 *
 * Backend returns 404 if the assignment belongs to a different rider.
 */
import React, { useMemo } from 'react';
import {
  Alert,
  ScrollView,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams } from 'expo-router';
import {
  useTodaysAssignments,
  useUpdateAssignmentStatus,
} from '@/hooks/useRider';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { Button } from '@/components/ui/Button';
import type { RiderAssignmentDto, RiderAssignmentStatus } from '@/types/api';

// ---------------------------------------------------------------------------
// Allowed transitions
// ---------------------------------------------------------------------------

type Transition = {
  label:   string;
  to:      RiderAssignmentStatus;
  variant: 'primary' | 'warning' | 'secondary' | 'danger';
};

const TRANSITIONS: Record<RiderAssignmentStatus, Transition[]> = {
  scheduled: [
    { label: 'Start Shift', to: 'active',    variant: 'primary' },
  ],
  active: [
    { label: 'Take a Break', to: 'on_break',  variant: 'warning'   },
    { label: 'End Shift',    to: 'completed', variant: 'secondary'  },
    { label: 'Cancel Shift', to: 'cancelled', variant: 'danger'     },
  ],
  on_break: [
    { label: 'Resume Shift', to: 'active',    variant: 'primary'   },
    { label: 'End Shift',    to: 'completed', variant: 'secondary'  },
  ],
  completed: [],
  cancelled: [],
};

// ---------------------------------------------------------------------------
// Row component
// ---------------------------------------------------------------------------

function DetailRow({ label, value }: { label: string; value: string }) {
  return (
    <View className="flex-row items-center justify-between border-b border-gray-100 py-3">
      <Text className="text-sm text-gray-500">{label}</Text>
      <Text className="text-sm font-medium text-gray-900">{value}</Text>
    </View>
  );
}

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

export default function AssignmentDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();

  const {
    data: assignments,
    isLoading,
    isError,
    refetch,
  } = useTodaysAssignments();

  const {
    mutateAsync: updateStatus,
    isPending,
  } = useUpdateAssignmentStatus();

  const assignment: RiderAssignmentDto | undefined = useMemo(
    () => assignments?.find((a) => a.id === id),
    [assignments, id],
  );

  if (isLoading) return <ScreenLoader />;
  if (isError) {
    return (
      <ErrorState
        message="Failed to load assignment details."
        onRetry={() => void refetch()}
      />
    );
  }
  if (!assignment) {
    return (
      <ErrorState message="Assignment not found or does not belong to you." />
    );
  }

  const currentStatus = assignment.status as RiderAssignmentStatus;
  const transitions = TRANSITIONS[currentStatus] ?? [];

  // Capture id in a stable const so closures below don't touch `assignment` directly
  const assignmentId = assignment.id;

  function onTransitionPress(transition: Transition) {
    const { to, label } = transition;
    const doUpdate = () => {
      void updateStatus({ id: assignmentId, status: to }).catch((err: unknown) => {
        const message = err instanceof Error ? err.message : 'Failed to update status';
        Alert.alert('Update Failed', message);
      });
    };

    if (to === 'cancelled' || to === 'completed') {
      Alert.alert(
        `Confirm: ${label}`,
        to === 'cancelled'
          ? 'Are you sure you want to cancel this shift? This cannot be undone.'
          : 'Mark this shift as completed?',
        [
          { text: 'Back', style: 'cancel' },
          {
            text: 'Confirm',
            style: to === 'cancelled' ? 'destructive' : 'default',
            onPress: doUpdate,
          },
        ],
      );
    } else {
      doUpdate();
    }
  }

  return (
    <SafeAreaView className="flex-1 bg-surface-muted" edges={['bottom']}>
      <ScrollView contentContainerStyle={{ padding: 16, paddingBottom: 32 }}>
        {/* Summary card */}
        <View
          className="rounded-2xl bg-white p-5 shadow-sm mb-4"
          style={{ elevation: 2 }}
        >
          <Text className="mb-1 text-xs font-semibold text-gray-400 uppercase tracking-wide">
            Assignment
          </Text>
          <Text className="text-base font-bold text-gray-900 mb-4">
            {assignment.shiftDate}  {assignment.shiftStart.slice(0, 5)} – {assignment.shiftEnd.slice(0, 5)}
          </Text>

          <DetailRow label="Status"          value={currentStatus} />
          <DetailRow label="Pickups"         value={`${assignment.completedPickups} / ${assignment.maxPickups}`} />
          <DetailRow label="Deliveries"      value={`${assignment.completedDeliveries} / ${assignment.maxDeliveries}`} />
          <DetailRow label="Failed Attempts" value={String(assignment.failedAttempts)} />
          {assignment.totalDistanceKm != null && (
            <DetailRow label="Distance" value={`${assignment.totalDistanceKm.toFixed(1)} km`} />
          )}
          {assignment.earnings != null && (
            <DetailRow label="Earnings" value={`₹${assignment.earnings.toFixed(2)}`} />
          )}
          {assignment.actualStartAt && (
            <DetailRow
              label="Started At"
              value={new Date(assignment.actualStartAt).toLocaleTimeString()}
            />
          )}
          {assignment.actualEndAt && (
            <DetailRow
              label="Ended At"
              value={new Date(assignment.actualEndAt).toLocaleTimeString()}
            />
          )}
          {assignment.notes ? (
            <View className="mt-3 rounded-xl bg-amber-50 p-3">
              <Text className="text-xs font-medium text-amber-700">Notes</Text>
              <Text className="mt-0.5 text-sm text-amber-600">{assignment.notes}</Text>
            </View>
          ) : null}
        </View>

        {/* Status transitions */}
        {transitions.length > 0 ? (
          <View
            className="rounded-2xl bg-white p-5 shadow-sm"
            style={{ elevation: 2 }}
          >
            <Text className="mb-4 text-sm font-bold text-gray-700">Update Status</Text>
            <View className="gap-3">
              {transitions.map((t) => (
                <Button
                  key={t.to}
                  title={t.label}
                  variant={t.variant}
                  fullWidth
                  size="md"
                  loading={isPending}
                  onPress={() => onTransitionPress(t)}
                  accessibilityLabel={`${t.label} this assignment`}
                />
              ))}
            </View>
          </View>
        ) : (
          <View className="rounded-2xl bg-gray-50 p-5">
            <Text className="text-center text-sm text-gray-500">
              No further status changes available.
            </Text>
          </View>
        )}
      </ScrollView>
    </SafeAreaView>
  );
}
