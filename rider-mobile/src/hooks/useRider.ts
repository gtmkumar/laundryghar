/**
 * TanStack Query hooks for rider self-service API calls.
 * All hooks share a flat query-key namespace under 'rider'.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  getMyRiderProfile,
  getMyAssignmentsToday,
  updateAssignmentStatus,
  postLocationPings,
} from '@/api/rider';
import type { LocationPingInput, RiderAssignmentStatus } from '@/types/api';

// ---------------------------------------------------------------------------
// Query keys
// ---------------------------------------------------------------------------
export const riderKeys = {
  me:          () => ['rider', 'me']           as const,
  assignments: () => ['rider', 'assignments', 'today'] as const,
  assignment:  (id: string) => ['rider', 'assignment', id] as const,
} as const;

// ---------------------------------------------------------------------------
// GET /api/v1/rider/me
// ---------------------------------------------------------------------------
export function useMyRiderProfile() {
  return useQuery({
    queryKey: riderKeys.me(),
    queryFn:  getMyRiderProfile,
    staleTime: 5 * 60_000,
  });
}

// ---------------------------------------------------------------------------
// GET /api/v1/rider/assignments/today
// ---------------------------------------------------------------------------
export function useTodaysAssignments() {
  return useQuery({
    queryKey: riderKeys.assignments(),
    queryFn:  getMyAssignmentsToday,
    // Poll every 60 s so status stays fresh without manual refresh
    refetchInterval: 60_000,
    staleTime:        30_000,
  });
}

// ---------------------------------------------------------------------------
// PATCH /api/v1/rider/assignments/{id}/status
// ---------------------------------------------------------------------------
export function useUpdateAssignmentStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, status }: { id: string; status: RiderAssignmentStatus }) =>
      updateAssignmentStatus(id, status),
    onSuccess: (updated) => {
      // Invalidate today's list so the updated status is reflected
      void qc.invalidateQueries({ queryKey: riderKeys.assignments() });
    },
  });
}

// ---------------------------------------------------------------------------
// POST /api/v1/rider/location/ping
// ---------------------------------------------------------------------------
export function usePostLocationPings() {
  return useMutation({
    mutationFn: (pings: LocationPingInput[]) => postLocationPings(pings),
  });
}
