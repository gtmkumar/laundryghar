/**
 * Rider self-service API — maps to LogisticsEndpoints.cs MapRiderSelfEndpoints
 * Endpoint prefix: {Logistics}/api/v1/rider/
 *
 * All endpoints require RiderOnly policy (Bearer token with user_type=rider).
 * Rider identity is resolved from the JWT — no rider id in the path.
 */
import { logisticsClient, unwrapList, unwrapSingle } from '@/api/client';
import type {
  LocationPingInput,
  PingBatchResponse,
  RiderAssignmentDto,
  RiderAssignmentStatus,
  RiderDto,
  SingleResponse,
  ListResponse,
} from '@/types/api';

// ---------------------------------------------------------------------------
// Types for the duty toggle endpoint
// ---------------------------------------------------------------------------

export interface DutyToggleResponse {
  onDuty:        boolean;
  openTaskCount: number;
}

// ---------------------------------------------------------------------------
// PATCH /api/v1/rider/duty
// Persists the rider's on/off-duty state server-side.
// Returns { onDuty, openTaskCount } — openTaskCount > 0 means the rider has
// open tasks while going off duty (caller decides whether to warn the user).
// ---------------------------------------------------------------------------
export async function patchRiderDuty(onDuty: boolean): Promise<DutyToggleResponse> {
  const res = await logisticsClient.patch<SingleResponse<DutyToggleResponse>>(
    '/rider/duty',
    { onDuty },
  );
  return unwrapSingle(res.data);
}

// ---------------------------------------------------------------------------
// GET /api/v1/rider/me
// Returns the full rider profile for the authenticated rider.
// ---------------------------------------------------------------------------
export async function getMyRiderProfile(): Promise<RiderDto> {
  const res = await logisticsClient.get<SingleResponse<RiderDto>>('/rider/me');
  return unwrapSingle(res.data);
}

// ---------------------------------------------------------------------------
// GET /api/v1/rider/assignments/today
// Returns today's assignments list for the authenticated rider.
// Backend response: ListResponse<RiderAssignmentDto>
// ---------------------------------------------------------------------------
export async function getMyAssignmentsToday(): Promise<RiderAssignmentDto[]> {
  const res = await logisticsClient.get<ListResponse<RiderAssignmentDto>>(
    '/rider/assignments/today',
  );
  return unwrapList(res.data);
}

// ---------------------------------------------------------------------------
// PATCH /api/v1/rider/assignments/{id}/status
// Update own assignment status. Backend returns 404 if not own assignment.
// ---------------------------------------------------------------------------
export async function updateAssignmentStatus(
  id: string,
  status: RiderAssignmentStatus,
): Promise<RiderAssignmentDto> {
  const res = await logisticsClient.patch<SingleResponse<RiderAssignmentDto>>(
    `/rider/assignments/${id}/status`,
    { status },
  );
  return unwrapSingle(res.data);
}

// ---------------------------------------------------------------------------
// POST /api/v1/rider/location/ping
// Sends a batch of GPS pings. Returns count of accepted pings.
// ---------------------------------------------------------------------------
export async function postLocationPings(
  pings: LocationPingInput[],
): Promise<PingBatchResponse> {
  const res = await logisticsClient.post<SingleResponse<PingBatchResponse>>(
    '/rider/location/ping',
    pings,
  );
  return unwrapSingle(res.data);
}
