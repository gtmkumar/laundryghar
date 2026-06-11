/**
 * Rider tasks API.
 *
 *   GET   /api/v1/rider/tasks/today              → ListResponse<RiderTask>
 *   GET   /api/v1/rider/tasks?date=YYYY-MM-DD    → ListResponse<RiderTask>  (earnings drill-down)
 *   PATCH /api/v1/rider/tasks/{id}/status        → started | arrived | completed | failed
 *   POST  /api/v1/rider/tasks/{id}/verify-otp    → server-side OTP check (code never returned)
 *   POST  /api/v1/rider/tasks/{id}/proof-photo   → multipart photo upload (optional PoD)
 *   POST  /api/v1/rider/tasks/{id}/inspection    → pickup garment condition evidence
 *
 * When FEATURES.riderTasksApi is false the app falls back to the labelled demo
 * set (src/data/demoTasks.ts) so the flow still works offline / pre-backend.
 */
import axios from 'axios';
import { ApiError, logisticsClient, unwrapList, unwrapSingle } from '@/api/client';
import { FEATURES } from '@/constants/config';
import { DEMO_TASKS } from '@/data/demoTasks';
import type { ListResponse, RiderTask, RiderTaskStatus, SingleResponse } from '@/types/api';

export interface RiderTasksResult {
  tasks:  RiderTask[];
  isDemo: boolean;
}

/** Coerce nullable server fields the UI treats as required. */
function normalize(t: RiderTask): RiderTask {
  return {
    ...t,
    distanceKm: t.distanceKm ?? 0,
    amountDue:  t.amountDue ?? 0,
    payout:     t.payout ?? 0,
  };
}

export async function fetchRiderTasks(): Promise<RiderTasksResult> {
  if (FEATURES.riderTasksApi) {
    const res = await logisticsClient.get<ListResponse<RiderTask>>('/rider/tasks/today');
    return { tasks: unwrapList(res.data).map(normalize), isDemo: false };
  }
  return { tasks: DEMO_TASKS, isDemo: true };
}

/**
 * Fetch completed tasks for a specific IST calendar date.
 * Used by the earnings drill-down: tapping a past day calls this so the rider
 * sees the individual tasks behind that day's total payout.
 *
 * @param dateIso  Calendar date in YYYY-MM-DD format (device local / IST).
 *                 The server applies the IST boundary (UTC+05:30) so late-night
 *                 completions bucket correctly regardless of server clock timezone.
 *
 * Returns an empty array when there are no tasks for that date.
 * Falls back to an empty array (not a demo set) when the feature flag is off,
 * since historical data has no meaningful offline fallback.
 */
export async function getTasksByDate(dateIso: string): Promise<RiderTask[]> {
  if (!FEATURES.riderTasksApi) return [];
  try {
    const res = await logisticsClient.get<ListResponse<RiderTask>>('/rider/tasks', {
      params: { date: dateIso },
    });
    return unwrapList(res.data).map(normalize);
  } catch (e) {
    throw toApiError(e, `Could not load tasks for ${dateIso}. Try again.`);
  }
}

/** Turns a 4xx envelope (axios rejects on those) into an ApiError carrying the server message. */
function toApiError(e: unknown, fallback: string): ApiError {
  if (axios.isAxiosError(e) && e.response?.data) {
    const env = e.response.data as SingleResponse<unknown>;
    return new ApiError(env.message?.responseMessage ?? fallback, { status: false });
  }
  if (e instanceof ApiError) return e;
  return new ApiError(fallback);
}

/**
 * Verify the delivery OTP server-side. The code the customer reads out is sent
 * to the server, which compares it to the order's stored OTP — we never receive
 * the real code. Throws ApiError("Incorrect OTP.") on mismatch.
 */
export async function verifyTaskOtp(taskId: string, code: string): Promise<RiderTask> {
  try {
    const res = await logisticsClient.post<SingleResponse<RiderTask>>(
      `/rider/tasks/${taskId}/verify-otp`,
      { code },
    );
    return unwrapSingle(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not verify the OTP. Try again.');
  }
}

/**
 * Advance a task's status. Accepts the display statuses plus the pickup-only
 * `collected` action — it records collection at the customer without completing
 * the leg, so the rider can then drive to the store to drop.
 */
export async function updateTaskStatus(
  taskId: string,
  status: RiderTaskStatus | 'collected',
): Promise<RiderTask> {
  try {
    const res = await logisticsClient.patch<SingleResponse<RiderTask>>(
      `/rider/tasks/${taskId}/status`,
      { status },
    );
    return unwrapSingle(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not update the task. Try again.');
  }
}

/**
 * Mark a task as failed with an optional structured reason code and note.
 * The rider's failure reason is stored server-side on delivery_assignments.
 *
 * Allowed reason codes: customer_unavailable | address_issue | customer_refused | other
 */
export async function failTaskStatus(
  taskId: string,
  reason: string,
  note?: string,
): Promise<RiderTask> {
  try {
    const res = await logisticsClient.patch<SingleResponse<RiderTask>>(
      `/rider/tasks/${taskId}/status`,
      { status: 'failed', reason, note },
    );
    return unwrapSingle(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not report the failure. Try again.');
  }
}

/**
 * Upload a proof-of-delivery photo for a task. The photo is optional — the
 * existing confirm flow works without it. Sends as multipart/form-data.
 *
 * @param taskId  The delivery_assignment id (same id used by all /rider/tasks/* endpoints)
 * @param uri     Local file URI returned by expo-image-picker (e.g. file:///...)
 * @param mimeType MIME type of the selected image (image/jpeg | image/png | image/webp)
 */
export async function uploadProofPhoto(
  taskId: string,
  uri: string,
  mimeType: string,
): Promise<RiderTask> {
  try {
    const form = new FormData();
    // React Native / Expo FormData accepts { uri, name, type } for file fields.
    form.append('file', { uri, name: 'proof.jpg', type: mimeType } as unknown as Blob);

    const res = await logisticsClient.post<SingleResponse<RiderTask>>(
      `/rider/tasks/${taskId}/proof-photo`,
      form,
      {
        headers: { 'Content-Type': 'multipart/form-data' },
        // Override the default 15 s timeout — photos can be large on slow connections.
        timeout: 60_000,
      },
    );
    return unwrapSingle(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not upload photo. Try again.');
  }
}
